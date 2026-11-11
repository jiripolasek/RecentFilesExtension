// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

using System.ComponentModel;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace JPSoftworks.RecentFilesExtension.Helpers;

internal static class ShellLauncher
{
    internal static unsafe void OpenWith(string filename)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filename);

        fixed (char* filenamePtr = filename)
        fixed (char* verbPtr = "openas")
        {
            var filenamePCWSTR = new PCWSTR((char*)filenamePtr);
            var verbPCWSTR = new PCWSTR((char*)verbPtr);

            var info = new SHELLEXECUTEINFOW
            {
                cbSize = (uint)Marshal.SizeOf<SHELLEXECUTEINFOW>(),
                lpVerb = verbPCWSTR,
                lpFile = filenamePCWSTR,
                nShow = (int)SHOW_WINDOW_CMD.SW_SHOWNORMAL,
                fMask = 12 //SEEMASKINVOKEIDLIST,
            };

            if (!PInvoke.ShellExecuteEx(ref info))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }
        }
    }
}