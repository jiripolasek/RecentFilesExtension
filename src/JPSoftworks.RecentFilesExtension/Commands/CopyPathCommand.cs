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
    private readonly Action<string> _setClipboardText;
    private readonly Func<string> _getClipboardText;

    internal CopyPathCommand(IRecentFile item)
        : this(item, ClipboardHelper.SetText, ClipboardHelper.GetText)
    {
    }

    internal CopyPathCommand(IRecentFile item, Action<string> setClipboardText, Func<string> getClipboardText)
    {
        this._item = item;
        this._setClipboardText = setClipboardText;
        this._getClipboardText = getClipboardText;
        this.Name = Strings.Command_CopyPath!;
        this.Icon = Icons.CopyPath;
    }

    public override CommandResult Invoke()
    {
        try
        {
            var path = this._item.TargetPath;
            this._setClipboardText(path);
            // The SDK helper returns normally when it exhausts its clipboard retries.
            if (!string.Equals(this._getClipboardText(), path, StringComparison.Ordinal))
            {
                Logger.LogWarning("The clipboard did not retain the copied path.");
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to copy the recent file path to the clipboard {ExceptionDiagnostics.Format(ex)}");
        }

        return CommandResult.KeepOpen();
    }
}