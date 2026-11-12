// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Windows.ApplicationModel;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Storage.Packaging.Appx;
using Windows.Win32.System.Rpc;
using Windows.Win32.System.Threading;

namespace JPSoftworks.RecentFilesExtension.Helpers;

internal enum ShellIconSupport
{
    Unknown,
    Unsupported,
    Supported,
}

internal static class CmdPalHost
{
    private const string MicrosoftPublisherId = "8wekyb3d8bbwe";
    private const string DevPackageName = "Microsoft.CommandPalette.Dev";

    internal static unsafe ShellIconSupport GetCallingShellIconSupport()
    {
        // The client PID is available only on the thread handling the host's local RPC call.
        RPC_CALL_ATTRIBUTES_V2_W call = new() { Version = 2, Flags = PInvoke.RPC_QUERY_CLIENT_PID };
        if (PInvoke.RpcServerInqCallAttributes(null, &call) != RPC_STATUS.RPC_S_OK)
        {
            return ShellIconSupport.Unknown;
        }

        using var process = PInvoke.OpenProcess_SafeHandle(
            PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)(nuint)call.ClientPID.Value);
        if (process.IsInvalid)
        {
            return ShellIconSupport.Unknown;
        }

        uint length = 0;
        var result = PInvoke.GetPackageId(process, ref length, default);
        if (result != WIN32_ERROR.ERROR_INSUFFICIENT_BUFFER)
        {
            return result == WIN32_ERROR.APPMODEL_ERROR_NO_PACKAGE && IsRegisteredDevExecutable(process)
                ? ShellIconSupport.Supported
                : ShellIconSupport.Unknown;
        }

        var buffer = new byte[length];
        fixed (byte* data = buffer)
        {
            if (PInvoke.GetPackageId(process, ref length, buffer) != WIN32_ERROR.ERROR_SUCCESS)
            {
                return ShellIconSupport.Unknown;
            }

            var package = (PACKAGE_ID*)data;
            return GetPackageShellIconSupport(package->name.ToString(), package->publisherId.ToString(),
                package->version.Major, package->version.Minor);
        }
    }

    internal static ShellIconSupport GetPackageShellIconSupport(string name, string publisherId, ushort major, ushort minor)
    {
        if (!string.Equals(publisherId, MicrosoftPublisherId, StringComparison.OrdinalIgnoreCase))
        {
            return ShellIconSupport.Unknown;
        }

        if (string.Equals(name, DevPackageName, StringComparison.OrdinalIgnoreCase))
        {
            return ShellIconSupport.Supported;
        }

        if (string.Equals(name, "Microsoft.CommandPalette", StringComparison.OrdinalIgnoreCase))
        {
            return major > 0 || minor >= 13 ? ShellIconSupport.Supported : ShellIconSupport.Unsupported;
        }

        return ShellIconSupport.Unknown;
    }

    private static bool IsRegisteredDevExecutable(SafeFileHandle process)
    {
        // Launching the registered dev executable directly gives it no process package identity.
        try
        {
            var app = AppInfo.GetFromAppUserModelId(DevPackageName + "_" + MicrosoftPublisherId + "!App");
            if (app?.Package?.InstalledPath is not { Length: > 0 } directory)
            {
                return false;
            }

            var expected = Path.Combine(directory, "Microsoft.CmdPal.UI.exe");
            var path = new char[expected.Length + 1];
            uint length = (uint)path.Length;
            return PInvoke.QueryFullProcessImageName(process, 0, path, ref length) &&
                expected.AsSpan().Equals(path.AsSpan(0, (int)length), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }
}