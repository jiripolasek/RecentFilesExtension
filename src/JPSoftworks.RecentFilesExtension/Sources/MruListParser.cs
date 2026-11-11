// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.Buffers.Binary;
using System.Globalization;
using Microsoft.Win32;

namespace JPSoftworks.RecentFilesExtension.Sources;

internal static class MruListParser
{
    public static IReadOnlyList<string> GetOrderedValueNames(RegistryKey key)
    {
        List<string> orderedNames = [];
        if (key.GetValue("MRUListEx") is byte[] mruListEx)
        {
            orderedNames.AddRange(ParseMruListEx(mruListEx)
                .Select(static value => value.ToString(CultureInfo.InvariantCulture)));
        }
        else if (key.GetValue("MRUList") is string legacyMruList)
        {
            orderedNames.AddRange(ParseLegacyMruList(legacyMruList));
        }

        var availableNames = key.GetValueNames()
            .Where(static name =>
                !name.Equals("MRUListEx", StringComparison.OrdinalIgnoreCase) &&
                !name.Equals("MRUList", StringComparison.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        orderedNames.RemoveAll(name => !availableNames.Remove(name));
        orderedNames.AddRange(availableNames.OrderBy(static name => name, ValueNameComparer.Instance));
        return orderedNames;
    }

    internal static IReadOnlyList<int> ParseMruListEx(ReadOnlySpan<byte> data)
    {
        List<int> result = [];
        for (var offset = 0; offset + sizeof(int) <= data.Length; offset += sizeof(int))
        {
            var value = BinaryPrimitives.ReadInt32LittleEndian(data[offset..]);
            if (value == -1)
            {
                break;
            }

            if (value >= 0 && !result.Contains(value))
            {
                result.Add(value);
            }
        }

        return result;
    }

    internal static IReadOnlyList<string> ParseLegacyMruList(string data)
    {
        return data
            .Where(static value => value != '\0')
            .Select(static value => value.ToString())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private sealed class ValueNameComparer : IComparer<string>
    {
        public static ValueNameComparer Instance { get; } = new();

        public int Compare(string? x, string? y)
        {
            if (int.TryParse(x, NumberStyles.None, CultureInfo.InvariantCulture, out var xNumber) &&
                int.TryParse(y, NumberStyles.None, CultureInfo.InvariantCulture, out var yNumber))
            {
                return xNumber.CompareTo(yNumber);
            }

            return StringComparer.OrdinalIgnoreCase.Compare(x, y);
        }
    }
}