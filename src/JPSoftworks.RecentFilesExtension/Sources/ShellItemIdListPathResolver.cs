// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

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

        var pathBuffer = new char[MaximumPathLength];
        fixed (char* pathPointer = pathBuffer)
        {
            if (SHGetPathFromIDListEx(
                    (byte*)itemIdList,
                    pathPointer,
                    (uint)pathBuffer.Length,
                    0) == 0)
            {
                return false;
            }

            return RecentFilePath.TryNormalize(new string(pathPointer), out path);
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