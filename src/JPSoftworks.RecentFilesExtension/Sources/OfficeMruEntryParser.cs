// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.Globalization;
using JPSoftworks.RecentFilesExtension.Model;

namespace JPSoftworks.RecentFilesExtension.Sources;

internal readonly record struct OfficeMruEntry(
    string Path,
    DateTimeOffset? LastUsedTimeUtc);

internal static class OfficeMruEntryParser
{
    public static bool TryParse(string? value, out OfficeMruEntry entry)
    {
        entry = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var separator = value.IndexOf('*');
        var metadata = separator >= 0 ? value.AsSpan(0, separator) : ReadOnlySpan<char>.Empty;
        var payload = separator >= 0 ? value[(separator + 1)..] : value;
        if (!RecentFilePath.TryNormalize(payload, out var path))
        {
            return false;
        }

        entry = new(path, TryReadTimestamp(metadata));
        return true;
    }

    private static DateTimeOffset? TryReadTimestamp(ReadOnlySpan<char> metadata)
    {
        while (!metadata.IsEmpty)
        {
            var openingBracket = metadata.IndexOf('[');
            if (openingBracket < 0)
            {
                break;
            }

            metadata = metadata[(openingBracket + 1)..];
            var closingBracket = metadata.IndexOf(']');
            if (closingBracket < 0)
            {
                break;
            }

            var token = metadata[..closingBracket];
            metadata = metadata[(closingBracket + 1)..];
            if (token.Length <= 1 || token[0] is not ('T' or 't') ||
                !ulong.TryParse(
                    token[1..],
                    NumberStyles.AllowHexSpecifier,
                    CultureInfo.InvariantCulture,
                    out var fileTime) ||
                fileTime > long.MaxValue)
            {
                continue;
            }

            try
            {
                return new(DateTime.FromFileTimeUtc((long)fileTime), TimeSpan.Zero);
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }
        }

        return null;
    }
}