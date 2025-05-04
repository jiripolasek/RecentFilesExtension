// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

using System;
using Windows.Storage.Streams;
using JPSoftworks.RecentFilesExtension.Commands;
using JPSoftworks.RecentFilesExtension.Helpers;
using JPSoftworks.RecentFilesExtension.Model;
using JPSoftworks.RecentFilesExtension.Resources;

namespace JPSoftworks.RecentFilesExtension.Pages;

internal sealed partial class RecentFileListItem : ListItem
{
    internal RecentFileListItem(IRecentFile recentFile) : base(new OpenFileCommand(recentFile))
    {
        ArgumentNullException.ThrowIfNull(recentFile);

        this.Title = recentFile.DisplayName;
        this.Subtitle = recentFile.TargetPath;
        this.Icon = GetIcon(recentFile);
        this.MoreCommands =
        [
            new CommandContextItem(
                new ShowFileInFolderCommand(recentFile.FullPath) { Name = Strings.Command_ShowInFolder! }),
            new CommandContextItem(new OpenWithCommand(recentFile)),
            new CommandContextItem(new CopyPathCommand(recentFile))
        ];
    }

    private static IconInfo? GetIcon(IRecentFile recentShortcutFile)
    {
        if (!string.IsNullOrWhiteSpace(recentShortcutFile.TargetPath))
        {
            try
            {
                var stream = ThumbnailHelper.GetThumbnail(recentShortcutFile.TargetPath).Result;
                if (stream != null)
                {
                    var data = new IconData(RandomAccessStreamReference.CreateFromStream(stream)!);
                    return new IconInfo(data, data);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("Failed to get the icon.", ex);
            }
        }

        try
        {
            var stream = ThumbnailHelper.GetThumbnail(recentShortcutFile.FullPath).Result;
            if (stream != null)
            {
                var data = new IconData(RandomAccessStreamReference.CreateFromStream(stream)!);
                return new IconInfo(data, data);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError("Failed to get the icon.", ex);
        }

        return null;
    }
}