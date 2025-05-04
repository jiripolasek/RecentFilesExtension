// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

using JPSoftworks.RecentFilesExtension.Helpers;
using JPSoftworks.RecentFilesExtension.Model;
using JPSoftworks.RecentFilesExtension.Resources;

namespace JPSoftworks.RecentFilesExtension.Commands;

internal sealed partial class OpenWithCommand : InvokableCommand
{
    private readonly IRecentFile _item;

    internal OpenWithCommand(IRecentFile item)
    {
        this._item = item;
        this.Name = Strings.Command_OpenWith!;
        this.Icon = Icons.OpenWith;
    }

    public override CommandResult Invoke()
    {
        ShellLauncher.OpenWith(this._item.FullPath);

        return CommandResult.Dismiss();
    }
}