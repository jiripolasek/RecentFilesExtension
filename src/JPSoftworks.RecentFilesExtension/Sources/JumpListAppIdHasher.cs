// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.Globalization;

namespace JPSoftworks.RecentFilesExtension.Sources;

internal static class JumpListAppIdHasher
{
    private const ulong Polynomial = 0x92C64265D32139A4;

    public static string GetDestinationId(string appUserModelId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appUserModelId);

        var checksum = ulong.MaxValue;
        foreach (var character in appUserModelId.ToUpperInvariant())
        {
            checksum = Update(checksum, (byte)character);
            checksum = Update(checksum, (byte)(character >> 8));
        }

        return checksum.ToString("x16", CultureInfo.InvariantCulture);
    }

    private static ulong Update(ulong checksum, byte value)
    {
        checksum ^= value;
        for (var bit = 0; bit < 8; bit++)
        {
            checksum = (checksum & 1) != 0
                ? (checksum >> 1) ^ Polynomial
                : checksum >> 1;
        }

        return checksum;
    }
}
