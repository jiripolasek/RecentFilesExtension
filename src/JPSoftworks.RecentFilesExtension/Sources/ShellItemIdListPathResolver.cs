// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using JPSoftworks.RecentFilesExtension.Model;

namespace JPSoftworks.RecentFilesExtension.Sources;

internal static partial class ShellItemIdListPathResolver
{
    private const int MaximumPathLength = 32768;

    public static unsafe bool TryResolve(byte[] itemIdList, out string path)
    {
        path = string.Empty;
        if (!IsValidItemIdList(itemIdList))
        {
            return false;
        }

        fixed (byte* itemIdListPointer = itemIdList)
        {
            return TryResolve((nint)itemIdListPointer, out path);
        }
    }

    internal static unsafe bool TryResolve(nint itemIdList, out string path)
    {
        path = string.Empty;
        if (itemIdList == 0)
        {
            return false;
        }

        var pathBuffer = ArrayPool<char>.Shared.Rent(MaximumPathLength);
        try
        {
            pathBuffer[0] = '\0';
            fixed (char* pathPointer = pathBuffer)
            {
                if (SHGetPathFromIDListEx((byte*)itemIdList, pathPointer, MaximumPathLength, 0) == 0)
                {
                    return false;
                }
            }
            var length = Array.IndexOf(pathBuffer, '\0', 0, MaximumPathLength);
            return length > 0 && RecentFilePath.TryNormalize(new string(pathBuffer, 0, length), out path);
        }
        finally
        {
            ArrayPool<char>.Shared.Return(pathBuffer, clearArray: true);
        }
    }

    internal static bool IsValidItemIdList(ReadOnlySpan<byte> data)
    {
        var offset = 0;
        while (offset + sizeof(ushort) <= data.Length)
        {
            var itemLength = BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]);
            if (itemLength == 0)
            {
                return true;
            }

            if (itemLength < sizeof(ushort) || offset + itemLength > data.Length)
            {
                return false;
            }

            offset += itemLength;
        }

        return false;
    }

    [LibraryImport("shell32.dll", EntryPoint = "SHGetPathFromIDListEx")]
    private static unsafe partial int SHGetPathFromIDListEx(
        byte* itemIdList,
        char* path,
        uint pathLength,
        uint options);
}