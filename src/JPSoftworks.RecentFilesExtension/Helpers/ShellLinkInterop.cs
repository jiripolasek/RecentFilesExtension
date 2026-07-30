// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace JPSoftworks.RecentFilesExtension.Helpers;

internal sealed partial class ShellLinkComObject : IDisposable
{
    private object? _comObject;
    private readonly bool _uninitializeCom;

    private ShellLinkComObject(
        object comObject,
        IShellLinkW shellLink,
        IPersistFile persistFile,
        bool uninitializeCom)
    {
        this._comObject = comObject;
        this.ShellLink = shellLink;
        this.PersistFile = persistFile;
        this._uninitializeCom = uninitializeCom;
    }

    internal IShellLinkW ShellLink { get; }

    internal IPersistFile PersistFile { get; }

    internal static ShellLinkComObject Create()
    {
        var initializeResult = ShellLinkNativeMethods.CoInitializeEx(0, CoInit.MultiThreaded);
        var uninitializeCom = initializeResult >= 0;
        if (initializeResult < 0 && initializeResult != HResults.RpcEChangedMode)
        {
            Marshal.ThrowExceptionForHR(initializeResult);
        }

        try
        {
            var classId = new Guid("00021401-0000-0000-C000-000000000046");
            var interfaceId = new Guid("000214F9-0000-0000-C000-000000000046");
            var createResult = ShellLinkNativeMethods.CoCreateInstance(
                ref classId,
                0,
                ClassContext.InProcessServer,
                ref interfaceId,
                out var shellLink);
            Marshal.ThrowExceptionForHR(createResult);

            try
            {
                return new ShellLinkComObject(
                    shellLink,
                    shellLink,
                    (IPersistFile)shellLink,
                    uninitializeCom);
            }
            catch
            {
                (shellLink as IDisposable)?.Dispose();
                throw;
            }
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

    public void Dispose()
    {
        var comObject = Interlocked.Exchange(ref this._comObject, null);
        if (comObject is null)
        {
            return;
        }

        try
        {
            (comObject as IDisposable)?.Dispose();
        }
        finally
        {
            if (this._uninitializeCom)
            {
                ShellLinkNativeMethods.CoUninitialize();
            }
        }
    }
}

internal static partial class ShellLinkNativeMethods
{
    [LibraryImport("ole32.dll")]
    internal static partial int CoInitializeEx(nint reserved, CoInit coInit);

    [LibraryImport("ole32.dll")]
    internal static partial void CoUninitialize();

    [LibraryImport("ole32.dll")]
    internal static partial int CoCreateInstance(
        ref Guid classId,
        nint outer,
        ClassContext context,
        ref Guid interfaceId,
        [MarshalUsing(typeof(UniqueComInterfaceMarshaller<IShellLinkW>))]
        out IShellLinkW instance);
}

[Flags]
internal enum CoInit : uint
{
    MultiThreaded = 0,
}

[Flags]
internal enum ClassContext : uint
{
    InProcessServer = 0x1,
}

internal static class HResults
{
    internal const int RpcEChangedMode = unchecked((int)0x80010106);
}

[GeneratedComInterface]
[Guid("000214F9-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal unsafe partial interface IShellLinkW
{
    [PreserveSig]
    int GetPath(char* filePath, int characterCount, nint findData, uint flags);

    [PreserveSig]
    int GetIDList(nint* itemIdList);

    [PreserveSig]
    int SetIDList(nint itemIdList);

    [PreserveSig]
    int GetDescription(char* name, int characterCount);

    [PreserveSig]
    int SetDescription(char* name);

    [PreserveSig]
    int GetWorkingDirectory(char* directory, int characterCount);

    [PreserveSig]
    int SetWorkingDirectory(char* directory);

    [PreserveSig]
    int GetArguments(char* arguments, int characterCount);

    [PreserveSig]
    int SetArguments(char* arguments);

    [PreserveSig]
    int GetHotkey(ushort* hotkey);

    [PreserveSig]
    int SetHotkey(ushort hotkey);

    [PreserveSig]
    int GetShowCommand(int* showCommand);

    [PreserveSig]
    int SetShowCommand(int showCommand);

    [PreserveSig]
    int GetIconLocation(char* iconPath, int characterCount, int* iconIndex);

    [PreserveSig]
    int SetIconLocation(char* iconPath, int iconIndex);

    [PreserveSig]
    int SetRelativePath(char* relativePath, uint reserved);

    [PreserveSig]
    int Resolve(nint window, uint flags);

    [PreserveSig]
    int SetPath(char* filePath);
}

[GeneratedComInterface]
[Guid("0000010C-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal partial interface IPersist
{
    [PreserveSig]
    int GetClassID(nint classId);
}

[GeneratedComInterface]
[Guid("0000010B-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal unsafe partial interface IPersistFile : IPersist
{
    [PreserveSig]
    int IsDirty();

    [PreserveSig]
    int Load(char* fileName, uint mode);

    [PreserveSig]
    int Save(char* fileName, int remember);

    [PreserveSig]
    int SaveCompleted(char* fileName);

    [PreserveSig]
    int GetCurrentFile(char** fileName);
}
