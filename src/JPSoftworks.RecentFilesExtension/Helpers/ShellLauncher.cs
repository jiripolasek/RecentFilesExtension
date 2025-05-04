// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace JPSoftworks.RecentFilesExtension.Helpers;

internal static class ShellLauncher
{
    internal static unsafe bool OpenWith(string filename)
    {
        var filenamePtr = Marshal.StringToHGlobalUni(filename);
        var verbPtr = Marshal.StringToHGlobalUni("openas");

        try
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

            return PInvoke.ShellExecuteEx(ref info);
        }
        finally
        {
            Marshal.FreeHGlobal(filenamePtr);
            Marshal.FreeHGlobal(verbPtr);
        }
    }
}