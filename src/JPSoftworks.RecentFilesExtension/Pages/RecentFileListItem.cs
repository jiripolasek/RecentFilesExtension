// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

using JPSoftworks.RecentFilesExtension.Commands;
using JPSoftworks.RecentFilesExtension.Helpers;
using JPSoftworks.RecentFilesExtension.Model;
using JPSoftworks.RecentFilesExtension.Resources;
using Windows.Storage.Streams;
using Windows.System;

namespace JPSoftworks.RecentFilesExtension.Pages;

internal sealed partial class RecentFileListItem : ListItem
{
    internal RecentFileListItem(IRecentFile recentFile) : base(new OpenFileCommand(recentFile))
    {
        ArgumentNullException.ThrowIfNull(recentFile);

        this.Title = recentFile.DisplayName;
        this.Subtitle = recentFile.TargetPath;
        this.Icon = GetIcon(recentFile);

        List<IContextItem> moreCommands = [];
        if (!string.IsNullOrWhiteSpace(recentFile.TargetPath))
        {
            moreCommands.Add(new CommandContextItem(new ShowFileInFolderCommand(recentFile.TargetPath) { Name = Strings.Command_ShowInFolder! })
            {
                RequestedShortcut = new(VirtualKeyModifiers.Shift | VirtualKeyModifiers.Menu, (int)VirtualKey.R, 0)
            });
            moreCommands.Add(new CommandContextItem(new OpenWithCommand(recentFile)));
            moreCommands.Add(new CommandContextItem(new CopyPathCommand(recentFile)) { RequestedShortcut = new(VirtualKeyModifiers.Shift | VirtualKeyModifiers.Menu, (int)VirtualKey.C, 0) });
        }
        this.MoreCommands = [.. moreCommands];
    }

    private static IconInfo? GetIcon(IRecentFile recentShortcutFile)
    {
        if (!string.IsNullOrWhiteSpace(recentShortcutFile.TargetPath) && !recentShortcutFile.TargetPath.IsNetworkPath())
        {
            try
            {
                var stream = ThumbnailHelper.GetThumbnail(recentShortcutFile.TargetPath).Result;
                if (stream != null)
                {
                    var data = new IconData(RandomAccessStreamReference.CreateFromStream(stream)!);
                    return new(data, data);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("Failed to get the icon.", ex);
            }
        }

        if (!recentShortcutFile.FullPath.IsNetworkPath())
        {
            try
            {
                var stream = ThumbnailHelper.GetThumbnail(recentShortcutFile.FullPath).Result;
                if (stream != null)
                {
                    var data = new IconData(RandomAccessStreamReference.CreateFromStream(stream)!);
                    return new(data, data);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("Failed to get the icon.", ex);
            }
        }

        return null;
    }
}