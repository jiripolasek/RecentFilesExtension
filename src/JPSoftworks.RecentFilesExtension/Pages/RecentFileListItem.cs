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
using JPCommands = JPSoftworks.RecentFilesExtension.Commands;

namespace JPSoftworks.RecentFilesExtension.Pages;

internal sealed partial class RecentFileListItem : ListItem
{
    private const int MaxConcurrentIconLoads = 4;
    private static readonly SemaphoreSlim IconLoadSemaphore = new(MaxConcurrentIconLoads);

    internal RecentFileListItem(IRecentFile recentFile, CancellationToken cancellationToken = default)
        : base(new JPCommands.OpenFileCommand(recentFile))
    {
        ArgumentNullException.ThrowIfNull(recentFile);

        this.Title = recentFile.DisplayName;
        this.Subtitle = recentFile.TargetPath;

        List<IContextItem> moreCommands = [];
        if (!string.IsNullOrWhiteSpace(recentFile.TargetPath))
        {
            moreCommands.Add(new CommandContextItem(new ShowFileInFolderCommand(recentFile.TargetPath) { Name = Strings.Command_ShowInFolder! }) { RequestedShortcut = KeyChords.OpenInExplorer });
            moreCommands.Add(new CommandContextItem(new OpenWithCommand(recentFile)));
            moreCommands.Add(new CommandContextItem(new JPCommands.CopyPathCommand(recentFile)) { RequestedShortcut = KeyChords.CopyPath });
        }
        this.MoreCommands = [.. moreCommands];

        _ = Task.Run(
            () => this.LoadIconAsync(recentFile, cancellationToken),
            CancellationToken.None);
    }

    private async Task LoadIconAsync(IRecentFile recentFile, CancellationToken cancellationToken)
    {
        var enteredSemaphore = false;

        try
        {
            await IconLoadSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            enteredSemaphore = true;

            var icon = await GetIconAsync(recentFile, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            if (icon != null)
            {
                this.Icon = icon;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected when a search is superseded or the page is disposed.
        }
        catch (Exception ex)
        {
            Logger.LogError("Failed to load the icon.", ex);
        }
        finally
        {
            if (enteredSemaphore)
            {
                IconLoadSemaphore.Release();
            }
        }
    }

    private static async Task<IconInfo?> GetIconAsync(
        IRecentFile recentShortcutFile,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Try target path first if it exists and is not a network path
        if (!string.IsNullOrWhiteSpace(recentShortcutFile.TargetPath) && !recentShortcutFile.TargetPath.IsNetworkPath())
        {
            var icon = await TryGetThumbnailIconAsync(recentShortcutFile.TargetPath).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            if (icon != null)
                return icon;
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Fall back to full path if it's not a network path
        if (!recentShortcutFile.FullPath.IsNetworkPath())
        {
            var icon = await TryGetThumbnailIconAsync(recentShortcutFile.FullPath).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return icon;
        }

        return null;
    }

    private static async Task<IconInfo?> TryGetThumbnailIconAsync(string path)
    {
        try
        {
            var stream = await ThumbnailHelper.GetThumbnail(path).ConfigureAwait(false);
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
