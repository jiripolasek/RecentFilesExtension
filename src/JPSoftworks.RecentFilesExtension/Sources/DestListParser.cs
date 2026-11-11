// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.Buffers.Binary;

namespace JPSoftworks.RecentFilesExtension.Sources;

internal readonly record struct DestListEntry(
    uint StreamNumber,
    DateTimeOffset LastUsedTimeUtc,
    int PinStatus,
    uint AccessCount);

internal readonly record struct DestListData(
    uint FormatVersion,
    IReadOnlyList<DestListEntry> Entries);

internal static class DestListParser
{
    // DestList is an undocumented Windows format. Versions 1 through 6 use the two
    // layouts validated here; reject later versions until their record shape is verified.
    private const int HeaderSize = 32;
    private const int Version1FixedEntrySize = 114;
    private const int ExtendedFixedEntrySize = 130;
    private const int ExtendedEntryTrailerSize = 4;
    private const int EntryNumberOffset = 88;
    private const int LastUsedFileTimeOffset = 100;
    private const int PinStatusOffset = 108;
    private const int AccessCountOffset = 116;
    private const int Version1PathLengthOffset = 112;
    private const int ExtendedPathLengthOffset = 128;
    private const uint MaximumSupportedVersion = 6;
    private const uint MaximumEntryCount = 4096;

    public static bool TryParse(ReadOnlySpan<byte> data, out DestListData destList)
    {
        destList = default;
        if (data.IsEmpty)
        {
            destList = new(0, Array.Empty<DestListEntry>());
            return true;
        }

        if (data.Length < HeaderSize)
        {
            return false;
        }

        var version = BinaryPrimitives.ReadUInt32LittleEndian(data);
        var declaredEntryCount = BinaryPrimitives.ReadUInt32LittleEndian(data[4..]);
        if (version is < 1 or > MaximumSupportedVersion || declaredEntryCount > MaximumEntryCount)
        {
            return false;
        }

        var fixedEntrySize = version == 1 ? Version1FixedEntrySize : ExtendedFixedEntrySize;
        var pathLengthOffset = version == 1 ? Version1PathLengthOffset : ExtendedPathLengthOffset;
        var trailerSize = version == 1 ? 0 : ExtendedEntryTrailerSize;
        var entries = new List<DestListEntry>((int)declaredEntryCount);
        var offset = HeaderSize;

        for (var index = 0U; index < declaredEntryCount; index++)
        {
            if (data.Length - offset < fixedEntrySize)
            {
                return false;
            }

            var entry = data[offset..];
            var streamNumber = BinaryPrimitives.ReadUInt32LittleEndian(entry[EntryNumberOffset..]);
            var fileTime = BinaryPrimitives.ReadInt64LittleEndian(entry[LastUsedFileTimeOffset..]);
            var pinStatus = BinaryPrimitives.ReadInt32LittleEndian(entry[PinStatusOffset..]);
            var accessCount = version == 1
                ? 0
                : BinaryPrimitives.ReadUInt32LittleEndian(entry[AccessCountOffset..]);
            var pathCharacterCount = BinaryPrimitives.ReadUInt16LittleEndian(entry[pathLengthOffset..]);
            var entrySize = (long)fixedEntrySize + ((long)pathCharacterCount * sizeof(char)) + trailerSize;
            if (entrySize > data.Length - offset)
            {
                return false;
            }

            if (streamNumber != 0 && TryConvertFileTime(fileTime, out var lastUsedTimeUtc))
            {
                entries.Add(new(streamNumber, lastUsedTimeUtc, pinStatus, accessCount));
            }

            offset += (int)entrySize;
        }

        if (!data[offset..].IsEmpty && !IsAllZero(data[offset..]))
        {
            return false;
        }

        destList = new(version, entries);
        return true;
    }

    private static bool TryConvertFileTime(long fileTime, out DateTimeOffset value)
    {
        value = default;
        if (fileTime <= 0)
        {
            return false;
        }

        try
        {
            value = new(DateTime.FromFileTimeUtc(fileTime), TimeSpan.Zero);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private static bool IsAllZero(ReadOnlySpan<byte> value)
    {
        foreach (var item in value)
        {
            if (item != 0)
            {
                return false;
            }
        }

        return true;
    }
}
