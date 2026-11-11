// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

using System.Diagnostics;
using JPSoftworks.RecentFilesExtension.Helpers;
using JPSoftworks.RecentFilesExtension.Model;
using JPSoftworks.RecentFilesExtension.Resources;

namespace JPSoftworks.RecentFilesExtension.Commands;

internal sealed partial class OpenFileCommand : InvokableCommand
{
    private readonly IRecentFile _item;

    internal OpenFileCommand(IRecentFile item)
    {
        this._item = item;
        var isDirectory = item.IsDirectory();
        this.Name = isDirectory ? Strings.Command_OpenFolder! : Strings.Command_OpenFile!;
        this.Icon = isDirectory ? Icons.FolderOpen : Icons.OpenFile;
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
            catch (Exception ex)
            {
                Logger.LogError($"Unable to open the recent file {ExceptionDiagnostics.Format(ex)}");
                return CommandResult.KeepOpen();
            }
        }

        return CommandResult.Dismiss();
    }
}