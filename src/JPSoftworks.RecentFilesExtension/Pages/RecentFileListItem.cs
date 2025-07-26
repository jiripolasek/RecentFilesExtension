// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

using System.Runtime.InteropServices;
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
        // Try target path first if it exists and is not a network path
        if (!string.IsNullOrWhiteSpace(recentShortcutFile.TargetPath) && !recentShortcutFile.TargetPath.IsNetworkPath())
        {
            var icon = TryGetThumbnailIcon(recentShortcutFile.TargetPath);
            if (icon != null)
                return icon;
        }

        // Fall back to full path if it's not a network path
        if (!recentShortcutFile.FullPath.IsNetworkPath())
        {
            return TryGetThumbnailIcon(recentShortcutFile.FullPath);
        }

        return null;
    }

    private static IconInfo? TryGetThumbnailIcon(string path)
    {
        try
        {
            var stream = ThumbnailHelper.GetThumbnail(path).Result;
            if (stream != null)
            {
                var data = new IconData(RandomAccessStreamReference.CreateFromStream(stream)!);
                return new(data, data);
            }
        }
        catch (Exception ex)
        {
            HandleThumbnailException(ex, path);
        }

        return null;
    }

    private static void HandleThumbnailException(Exception ex, string path)
    {
        switch (ex)
        {
            case AggregateException aggregateEx:

                foreach (var innerEx in aggregateEx.InnerExceptions)
                {
                    if (innerEx is COMException comEx)
                    {
                        Logger.LogWarning($"Failed to get the icon for {path}. Error code = {comEx.ErrorCode}");
                    }
                    else
                    {
                        Logger.LogError("Failed to get the icon.", innerEx);
                    }
                }
                break;
            case COMException comEx:
                Logger.LogWarning($"Failed to get the icon for {path}. Error code = {comEx.ErrorCode}");
                break;
            default:
                Logger.LogError("Failed to get the icon.", ex);
                break;
        }
    }
}