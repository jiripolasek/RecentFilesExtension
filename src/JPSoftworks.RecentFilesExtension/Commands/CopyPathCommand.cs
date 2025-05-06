// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

using JPSoftworks.RecentFilesExtension.Helpers;
using JPSoftworks.RecentFilesExtension.Model;
using JPSoftworks.RecentFilesExtension.Resources;

namespace JPSoftworks.RecentFilesExtension.Commands;

internal sealed partial class CopyPathCommand : InvokableCommand
{
    private readonly IRecentFile _item;

    internal CopyPathCommand(IRecentFile item)
    {
        this._item = item;
        this.Name = Strings.Command_CopyPath!;
        this.Icon = Icons.CopyPath;
    }

    public override CommandResult Invoke()
    {
        try
        {
            ClipboardHelper.SetText(this._item.TargetPath);
        }
        catch
        {
        }

        return CommandResult.KeepOpen();
    }
}