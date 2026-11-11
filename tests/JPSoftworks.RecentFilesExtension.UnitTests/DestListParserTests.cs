// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.Buffers.Binary;
using JPSoftworks.RecentFilesExtension.Sources;

namespace JPSoftworks.RecentFilesExtension.UnitTests;

[TestClass]
public sealed class DestListParserTests
{
    [TestMethod]
    public void TryParseReadsVersion6TimestampAndMetadata()
    {
        var expectedTime = new DateTimeOffset(2026, 8, 16, 7, 2, 18, TimeSpan.Zero).AddTicks(1_594_661);
        var data = CreateDestList(
            6,
            new TestEntry(0x78, expectedTime, -1, 2, "cloud-item"),
            new TestEntry(0x79, expectedTime.AddMinutes(-1), 3, 5, "local-item"));

        var parsed = DestListParser.TryParse(data, out var destList);

        Assert.IsTrue(parsed);
        Assert.AreEqual(6U, destList.FormatVersion);
        Assert.HasCount(2, destList.Entries);
        Assert.AreEqual(0x78U, destList.Entries[0].StreamNumber);
        Assert.AreEqual(expectedTime, destList.Entries[0].LastUsedTimeUtc);
        Assert.AreEqual(-1, destList.Entries[0].PinStatus);
        Assert.AreEqual(2U, destList.Entries[0].AccessCount);
    }

    [TestMethod]
    public void TryParseReadsVersion1Entry()
    {
        var expectedTime = new DateTimeOffset(2014, 7, 24, 21, 11, 19, TimeSpan.Zero);
        var data = CreateDestList(1, new TestEntry(4, expectedTime, -1, 0, "legacy"));

        var parsed = DestListParser.TryParse(data, out var destList);

        Assert.IsTrue(parsed);
        Assert.HasCount(1, destList.Entries);
        Assert.AreEqual(expectedTime, destList.Entries[0].LastUsedTimeUtc);
        Assert.AreEqual(0U, destList.Entries[0].AccessCount);
    }

    [TestMethod]
    public void TryParseRejectsFutureVersion()
    {
        var data = CreateDestList(
            7,
            new TestEntry(1, DateTimeOffset.UtcNow, -1, 1, string.Empty));

        Assert.IsFalse(DestListParser.TryParse(data, out _));
    }

    [TestMethod]
    public void TryParseRejectsTruncatedEntry()
    {
        var data = CreateDestList(
            6,
            new TestEntry(1, DateTimeOffset.UtcNow, -1, 1, "truncated"));

        Assert.IsFalse(DestListParser.TryParse(data.AsSpan(0, data.Length - 1), out _));
    }

    [TestMethod]
    public void TryParseRejectsExcessiveDeclaredEntryCount()
    {
        var data = new byte[32];
        BinaryPrimitives.WriteUInt32LittleEndian(data, 6);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), 4097);

        Assert.IsFalse(DestListParser.TryParse(data, out _));
    }

    [TestMethod]
    public void TryParseRejectsUnexpectedTrailingData()
    {
        var data = CreateDestList(
            6,
            new TestEntry(1, DateTimeOffset.UtcNow, -1, 1, "entry"));
        Array.Resize(ref data, data.Length + 1);
        data[^1] = 1;

        Assert.IsFalse(DestListParser.TryParse(data, out _));
    }

    private static byte[] CreateDestList(uint version, params TestEntry[] entries)
    {
        var fixedEntrySize = version == 1 ? 114 : 130;
        var trailerSize = version == 1 ? 0 : 4;
        var size = 32 + entries.Sum(entry => fixedEntrySize + (entry.Path.Length * sizeof(char)) + trailerSize);
        var result = new byte[size];
        BinaryPrimitives.WriteUInt32LittleEndian(result, version);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), (uint)entries.Length);
        var offset = 32;

        foreach (var entry in entries)
        {
            var span = result.AsSpan(offset);
            BinaryPrimitives.WriteUInt32LittleEndian(span[88..], entry.StreamNumber);
            BinaryPrimitives.WriteInt64LittleEndian(span[100..], entry.LastUsedTimeUtc.UtcDateTime.ToFileTimeUtc());
            BinaryPrimitives.WriteInt32LittleEndian(span[108..], entry.PinStatus);
            var pathLengthOffset = version == 1 ? 112 : 128;
            if (version != 1)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(span[116..], entry.AccessCount);
            }

            BinaryPrimitives.WriteUInt16LittleEndian(span[pathLengthOffset..], (ushort)entry.Path.Length);
            System.Text.Encoding.Unicode.GetBytes(entry.Path, span[fixedEntrySize..]);
            offset += fixedEntrySize + (entry.Path.Length * sizeof(char)) + trailerSize;
        }

        return result;
    }

    private sealed record TestEntry(
        uint StreamNumber,
        DateTimeOffset LastUsedTimeUtc,
        int PinStatus,
        uint AccessCount,
        string Path);
}
