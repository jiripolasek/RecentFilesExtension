// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

using System.ComponentModel;
using Windows.Win32.Foundation;
using JPSoftworks.RecentFilesExtension.Helpers;
using JPSoftworks.RecentFilesExtension.Model;
using JPSoftworks.RecentFilesExtension.Resources;

namespace JPSoftworks.RecentFilesExtension.Commands;

internal sealed partial class OpenWithCommand : InvokableCommand
{
    private readonly IRecentFile _item;
    private readonly Action<string> _openWith;

    internal OpenWithCommand(IRecentFile item)
        : this(item, ShellLauncher.OpenWith)
    {
    }

    internal OpenWithCommand(IRecentFile item, Action<string> openWith)
    {
        this._item = item;
        this._openWith = openWith;
        this.Name = Strings.Command_OpenWith!;
        this.Icon = Icons.OpenWith;
    }

    public override CommandResult Invoke()
    {
        try
        {
            this._openWith(this._item.TargetPath);
            return CommandResult.Dismiss();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == (int)WIN32_ERROR.ERROR_CANCELLED)
        {
            return CommandResult.Dismiss();
        }
        catch (Exception ex)
        {
            Logger.LogError($"Unable to show Open with {ExceptionDiagnostics.Format(ex)}");
            return CommandResult.KeepOpen();
        }
    }
}