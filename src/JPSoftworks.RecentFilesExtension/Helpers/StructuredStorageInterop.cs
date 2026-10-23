// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace JPSoftworks.RecentFilesExtension.Helpers;

internal sealed partial class StructuredStorageFile : IDisposable
{
    private const uint Read = 0x00000000;
    private const uint ShareExclusive = 0x00000010;
    private const uint ShareDenyWrite = 0x00000020;
    private const uint StatFlagNoName = 1;

    private object? _storageObject;
    private readonly IStructuredStorage _storage;
    private readonly bool _uninitializeCom;

    private StructuredStorageFile(
        IStructuredStorage storage,
        bool uninitializeCom)
    {
        this._storageObject = storage;
        this._storage = storage;
        this._uninitializeCom = uninitializeCom;
    }

    public static StructuredStorageFile Open(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var initializeResult = ShellLinkNativeMethods.CoInitializeEx(0, CoInit.MultiThreaded);
        var uninitializeCom = initializeResult >= 0;
        if (initializeResult < 0 && initializeResult != HResults.RpcEChangedMode)
        {
            Marshal.ThrowExceptionForHR(initializeResult);
        }

        try
        {
            var openResult = StructuredStorageNativeMethods.StgOpenStorage(
                path,
                0,
                Read | ShareDenyWrite,
                0,
                0,
                out var storage);
            Marshal.ThrowExceptionForHR(openResult);
            return new(storage, uninitializeCom);
        }
        catch
        {
            if (uninitializeCom)
            {
                ShellLinkNativeMethods.CoUninitialize();
            }

            throw;
        }
    }

    public byte[] ReadStream(string streamName, int maximumSize)
    {
        return this.UseStream(streamName, stream => ReadAllBytes(stream, maximumSize));
    }

    public T UseStream<T>(
        string streamName,
        int maximumSize,
        Func<IStructuredStorageStream, T> reader)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumSize);
        return this.UseStream(
            streamName,
            stream =>
            {
                EnsureMaximumSize(stream, maximumSize);
                return reader(stream);
            });
    }

    public unsafe T UseStream<T>(string streamName, Func<IStructuredStorageStream, T> reader)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(streamName);
        ArgumentNullException.ThrowIfNull(reader);

        IStructuredStorageStream stream;
        fixed (char* streamNamePointer = streamName)
        {
            var openResult = this._storage.OpenStream(
                streamNamePointer,
                0,
                Read | ShareExclusive,
                0,
                out stream);
            Marshal.ThrowExceptionForHR(openResult);
        }

        try
        {
            return reader(stream);
        }
        finally
        {
            (stream as IDisposable)?.Dispose();
        }
    }

    public void Dispose()
    {
        var storageObject = Interlocked.Exchange(ref this._storageObject, null);
        if (storageObject is null)
        {
            return;
        }

        try
        {
            (storageObject as IDisposable)?.Dispose();
        }
        finally
        {
            if (this._uninitializeCom)
            {
                ShellLinkNativeMethods.CoUninitialize();
            }
        }
    }

    private static unsafe byte[] ReadAllBytes(IStructuredStorageStream stream, int maximumSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumSize);

        var size = GetSize(stream);
        EnsureMaximumSize(size, maximumSize);

        var result = new byte[(int)size];
        if (result.Length == 0)
        {
            return result;
        }

        var totalRead = 0;
        fixed (byte* buffer = result)
        {
            while (totalRead < result.Length)
            {
                uint bytesRead;
                var readResult = stream.Read(
                    buffer + totalRead,
                    (uint)(result.Length - totalRead),
                    &bytesRead);
                Marshal.ThrowExceptionForHR(readResult);
                if (bytesRead == 0)
                {
                    break;
                }

                totalRead += (int)bytesRead;
            }
        }

        if (totalRead != result.Length)
        {
            Array.Resize(ref result, totalRead);
        }

        return result;
    }

    private static unsafe ulong GetSize(IStructuredStorageStream stream)
    {
        StructuredStorageStat stat;
        Marshal.ThrowExceptionForHR(stream.Stat(&stat, StatFlagNoName));
        return stat.Size;
    }

    private static void EnsureMaximumSize(IStructuredStorageStream stream, int maximumSize)
    {
        EnsureMaximumSize(GetSize(stream), maximumSize);
    }

    private static void EnsureMaximumSize(ulong size, int maximumSize)
    {
        if (size > (ulong)maximumSize)
        {
            throw new InvalidDataException($"Structured-storage stream is larger than {maximumSize} bytes.");
        }
    }
}

internal static partial class StructuredStorageNativeMethods
{
    [LibraryImport("ole32.dll", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int StgOpenStorage(
        string name,
        nint priorityStorage,
        uint mode,
        nint excludeNames,
        uint reserved,
        [MarshalUsing(typeof(UniqueComInterfaceMarshaller<IStructuredStorage>))]
        out IStructuredStorage storage);
}

[StructLayout(LayoutKind.Sequential)]
internal struct StructuredStorageStat
{
    public nint Name;
    public uint Type;
    public ulong Size;
    public long ModificationTime;
    public long CreationTime;
    public long AccessTime;
    public uint Mode;
    public uint LocksSupported;
    public Guid ClassId;
    public uint StateBits;
    public uint Reserved;
}

[GeneratedComInterface]
[Guid("0000000B-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal unsafe partial interface IStructuredStorage
{
    [PreserveSig]
    int CreateStream(char* name, uint mode, uint reserved1, uint reserved2, nint* stream);

    [PreserveSig]
    int OpenStream(
        char* name,
        nint reserved1,
        uint mode,
        uint reserved2,
        [MarshalUsing(typeof(UniqueComInterfaceMarshaller<IStructuredStorageStream>))]
        out IStructuredStorageStream stream);

    [PreserveSig]
    int CreateStorage(char* name, uint mode, uint reserved1, uint reserved2, nint* storage);

    [PreserveSig]
    int OpenStorage(char* name, nint priorityStorage, uint mode, nint excludeNames, uint reserved, nint* storage);

    [PreserveSig]
    int CopyTo(uint excludedInterfaceCount, nint excludedInterfaces, nint excludedNames, nint destinationStorage);

    [PreserveSig]
    int MoveElementTo(char* name, nint destinationStorage, char* newName, uint flags);

    [PreserveSig]
    int Commit(uint flags);

    [PreserveSig]
    int Revert();

    [PreserveSig]
    int EnumElements(uint reserved1, nint reserved2, uint reserved3, nint* enumerator);

    [PreserveSig]
    int DestroyElement(char* name);

    [PreserveSig]
    int RenameElement(char* oldName, char* newName);

    [PreserveSig]
    int SetElementTimes(char* name, nint creationTime, nint accessTime, nint modificationTime);

    [PreserveSig]
    int SetClass(Guid* classId);

    [PreserveSig]
    int SetStateBits(uint stateBits, uint mask);

    [PreserveSig]
    int Stat(StructuredStorageStat* stat, uint flags);
}

[GeneratedComInterface]
[Guid("0000000C-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal unsafe partial interface IStructuredStorageStream
{
    [PreserveSig]
    int Read(void* buffer, uint count, uint* bytesRead);

    [PreserveSig]
    int Write(void* buffer, uint count, uint* bytesWritten);

    [PreserveSig]
    int Seek(long move, uint origin, ulong* newPosition);

    [PreserveSig]
    int SetSize(ulong newSize);

    [PreserveSig]
    int CopyTo(nint destinationStream, ulong count, ulong* bytesRead, ulong* bytesWritten);

    [PreserveSig]
    int Commit(uint flags);

    [PreserveSig]
    int Revert();

    [PreserveSig]
    int LockRegion(ulong offset, ulong count, uint lockType);

    [PreserveSig]
    int UnlockRegion(ulong offset, ulong count, uint lockType);

    [PreserveSig]
    int Stat(StructuredStorageStat* stat, uint flags);

    [PreserveSig]
    int Clone(nint* stream);
}