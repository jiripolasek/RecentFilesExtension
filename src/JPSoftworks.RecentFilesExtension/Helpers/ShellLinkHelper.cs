// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.Buffers;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using JPSoftworks.RecentFilesExtension.Sources;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Storage.FileSystem;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.Shell.Common;
using static Windows.Win32.PInvoke;

// ReSharper disable InconsistentNaming
// ReSharper disable SuspiciousTypeConversion.Global

namespace JPSoftworks.RecentFilesExtension.Helpers;

/// <summary>
/// Helper for resolving .lnk shortcuts using CsWin32-generated interop.
/// </summary>
public sealed partial class ShellLinkHelper : IDisposable
{
    private const int MAX_PATH = 260;

    private readonly ShellLinkComObject _comObject;
    private readonly IPersistFile _persistFile;
    private readonly IPersistStream _persistStream;
    private readonly IShellLinkW _shellLink;

    public string TargetPath { get; private set; } = string.Empty;
    public string DisplayName { get; private set; } = string.Empty;
    internal bool IsDirectory { get; private set; }

    public ShellLinkHelper()
    {
        this._comObject = ShellLinkComObject.Create();
        this._shellLink = this._comObject.ShellLink;
        this._persistFile = this._comObject.PersistFile;
        this._persistStream = this._comObject.PersistStream;
    }

    public void Dispose()
    {
        this._comObject.Dispose();
    }

    internal unsafe string LoadTargetPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            throw new FileNotFoundException("Shortcut file not found.", path);
        }

        fixed (char* shortcutPath = path)
        {
            Marshal.ThrowExceptionForHR(this._persistFile.Load(shortcutPath, 0));
        }

        Span<char> buffer = stackalloc char[MAX_PATH];
        WIN32_FIND_DATAW findData = default;
        int pathResult;
        fixed (char* targetPath = buffer)
        {
            pathResult = this._shellLink.GetPath(targetPath, buffer.Length, (nint)(&findData), (uint)SLGP_FLAGS.SLGP_RAWPATH);
        }

        this.IsDirectory = (findData.dwFileAttributes & (uint)FILE_FLAGS_AND_ATTRIBUTES.FILE_ATTRIBUTE_DIRECTORY) != 0;

        if (!this.TryGetTargetPathFromIdList(out var resolvedPath))
        {
            Marshal.ThrowExceptionForHR(pathResult);
            resolvedPath = ReadNullTerminatedString(buffer);
        }

        this.TargetPath = resolvedPath;
        return this.TargetPath;
    }

    internal unsafe ShellLinkTargetData LoadTargetData(IStructuredStorageStream stream, Action<string>? reportFailure = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        reportFailure ??= Logger.LogWarning;

        var streamPointer = ComInterfaceMarshaller<IStructuredStorageStream>.ConvertToUnmanaged(stream);
        try
        {
            Marshal.ThrowExceptionForHR(this._persistStream.Load((nint)streamPointer));
        }
        finally
        {
            ComInterfaceMarshaller<IStructuredStorageStream>.Free(streamPointer);
        }

        const int bufferLength = 32768;
        var buffer = ArrayPool<char>.Shared.Rent(bufferLength);
        string targetPath, arguments, description;
        try
        {
            fixed (char* output = buffer)
            {
                buffer[0] = '\0';
                targetPath = ReadField(this._shellLink.GetPath(output, bufferLength, 0, (uint)SLGP_FLAGS.SLGP_RAWPATH), "target path");
                buffer[0] = '\0';
                arguments = ReadField(this._shellLink.GetArguments(output, bufferLength), "arguments");
                buffer[0] = '\0';
                description = ReadField(this._shellLink.GetDescription(output, bufferLength), "description");
            }
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buffer, clearArray: true);
        }

        return new(this.TryGetTargetPathFromIdList(out var resolvedPath, reportFailure) ? resolvedPath : targetPath, arguments, description);

        string ReadField(int result, string field)
        {
            if (result < 0)
            {
                reportFailure($"Failed to read Jump List {field}: HRESULT 0x{result:X8}");
                return string.Empty;
            }
            return ReadNullTerminatedString(buffer.AsSpan(0, bufferLength));
        }
    }

    private unsafe bool TryGetTargetPathFromIdList(out string path, Action<string>? reportFailure = null)
    {
        reportFailure ??= Logger.LogWarning;
        // GetPath truncates at MAX_PATH even when given a larger buffer.
        path = string.Empty;
        nint itemIdList = 0;
        try
        {
            var result = this._shellLink.GetIDList(&itemIdList);
            if (result < 0)
            {
                reportFailure($"Failed to read a shortcut item ID list: HRESULT 0x{result:X8}");
                return false;
            }

            return ShellItemIdListPathResolver.TryResolve(itemIdList, out path);
        }
        finally
        {
            if (itemIdList != 0)
            {
                Marshal.FreeCoTaskMem(itemIdList);
            }
        }
    }

    private static string ReadNullTerminatedString(ReadOnlySpan<char> value)
    {
        var terminator = value.IndexOf('\0');
        return new(terminator < 0 ? value : value[..terminator]);
    }

    internal string RetrieveDisplayName(string path)
    {
        try
        {
            this.DisplayName = GetLinkTargetDisplayName(this._shellLink);
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"Failed to read a shortcut display name {ExceptionDiagnostics.Format(ex)}");
            this.DisplayName = Path.GetFileNameWithoutExtension(path);
#if DEBUG
            this.DisplayName = "C: " + this.DisplayName;
#endif
        }

        return this.DisplayName;
    }

    private static unsafe string GetLinkTargetDisplayName(IShellLinkW link)
    {
        nint itemIdList = 0;
        PWSTR pwsz = default;

        try
        {
            Marshal.ThrowExceptionForHR(link.GetIDList(&itemIdList));
            if (itemIdList == 0)
            {
                throw new InvalidOperationException("The shortcut has no target item ID list.");
            }

            SHGetNameFromIDList(*(ITEMIDLIST*)itemIdList, SIGDN.SIGDN_NORMALDISPLAY, out pwsz)
                .ThrowOnFailure();

            return new string(pwsz.AsSpan());
        }
        finally
        {
            if (pwsz != null)
            {
                Marshal.FreeCoTaskMem((IntPtr)pwsz.Value);
            }

            if (itemIdList != 0)
            {
                Marshal.FreeCoTaskMem(itemIdList);
            }
        }
    }


}

internal readonly record struct ShellLinkTargetData(
    string TargetPath,
    string Arguments,
    string Description);