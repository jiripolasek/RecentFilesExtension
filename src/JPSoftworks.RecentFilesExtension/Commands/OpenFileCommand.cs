// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

using System.ComponentModel;
using System.Diagnostics;
using JPSoftworks.RecentFilesExtension.Helpers;
using JPSoftworks.RecentFilesExtension.Model;
using JPSoftworks.RecentFilesExtension.Resources;

namespace JPSoftworks.RecentFilesExtension.Commands;

internal sealed partial class OpenFileCommand : InvokableCommand
{
    private readonly RecentFile _item;

    internal OpenFileCommand(RecentFile item)
    {
        this._item = item;
        this.Name = item.IsDirectory() ? Strings.Command_OpenFolder! : Strings.Command_OpenFile!;
        this.Icon = Icons.OpenFile;
    }

    public override CommandResult Invoke()
    {
        using (var process = new Process())
        {
            process.StartInfo.FileName = this._item.FullPath;
            process.StartInfo.UseShellExecute = true;

            try
            {
                process.Start();
            }
            catch (Win32Exception ex)
            {
                Logger.LogError($"Unable to open {this._item.FullPath}", ex);
            }
        }

        return CommandResult.GoHome();
    }
}