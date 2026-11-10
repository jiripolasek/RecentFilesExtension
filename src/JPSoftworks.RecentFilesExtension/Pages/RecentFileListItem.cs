// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using JPSoftworks.RecentFilesExtension.Commands;
using JPSoftworks.RecentFilesExtension.Helpers;
using JPSoftworks.RecentFilesExtension.Model;
using JPSoftworks.RecentFilesExtension.Resources;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Streams;
using JPCommands = JPSoftworks.RecentFilesExtension.Commands;

namespace JPSoftworks.RecentFilesExtension.Pages;

internal sealed partial class RecentFileListItem : ListItem
{
    private const int MaxConcurrentIconLoads = 4;
    private static readonly SemaphoreSlim IconLoadSemaphore = new(MaxConcurrentIconLoads);
    private static readonly CompositeFormat ExtensionFileTypeFormat = CompositeFormat.Parse(Strings.FileType_ExtensionFile);
    private readonly IRecentFile _recentFile;

    internal RecentFileListItem(IRecentFile recentFile, CancellationToken cancellationToken = default)
        : base(new JPCommands.OpenFileCommand(recentFile))
    {
        ArgumentNullException.ThrowIfNull(recentFile);

        this._recentFile = recentFile;
        this.Title = recentFile.DisplayName;
        this.Subtitle = recentFile.TargetPath;

        List<IContextItem> moreCommands = [];
        if (!string.IsNullOrWhiteSpace(recentFile.TargetPath))
        {
            if (recentFile.IsFileSystemPath)
            {
                moreCommands.Add(new CommandContextItem(new ShowFileInFolderCommand(recentFile.TargetPath) { Name = Strings.Command_ShowInFolder! }) { RequestedShortcut = KeyChords.OpenInExplorer });
                moreCommands.Add(new CommandContextItem(new OpenWithCommand(recentFile)));
            }

            moreCommands.Add(new CommandContextItem(new JPCommands.CopyPathCommand(recentFile)) { RequestedShortcut = KeyChords.CopyPath });
        }
        this.MoreCommands = [.. moreCommands];

        if (!recentFile.IsFileSystemPath && recentFile.IsDirectory())
        {
            this.Icon = Icons.FolderOpen;
        }
        else if (TryGetDirectBinaryIconPath(recentFile, out var directIconPath))
        {
            // Let CmdPal extract executable icons in-process. This avoids handing an
            // asynchronously-created WinRT stream across the extension boundary.
            this.Icon = new IconInfo(directIconPath);
        }
        else
        {
            _ = Task.Run(
                () => this.LoadIconAsync(recentFile, cancellationToken),
                CancellationToken.None);
        }
    }

    internal void ApplyAppearance(IRecentFilesSettings settings)
    {
        var recentFile = this._recentFile;
        var isDirectory = recentFile.IsDirectory();
        var fileName = Path.GetFileName(
            Uri.TryCreate(recentFile.TargetPath, UriKind.Absolute, out var uri) && !uri.IsFile
                ? Uri.UnescapeDataString(uri.AbsolutePath)
                : recentFile.TargetPath);
        var extension = isDirectory ? string.Empty : Path.GetExtension(fileName);
        if (extension.Length == fileName.Length)
        {
            extension = string.Empty;
        }

        this.Title = settings.HideFileExtensions &&
                     extension.Length > 0 &&
                     recentFile.DisplayName.Length > extension.Length &&
                     recentFile.DisplayName.EndsWith(extension, StringComparison.OrdinalIgnoreCase)
            ? recentFile.DisplayName[..^extension.Length]
            : recentFile.DisplayName;

        var type = isDirectory
            ? Strings.FileType_Folder
            : extension.Length == 0
                ? Strings.FileType_File
                : string.Format(CultureInfo.CurrentCulture, ExtensionFileTypeFormat, extension[1..].ToUpperInvariant());

        this.Subtitle = settings.ShowFileTypeInSubtitle ? $"{type} · {recentFile.TargetPath}" : recentFile.TargetPath;
        this.Tags = settings.ShowFileTypeTags
            ? [new Tag(extension.Length > 0 ? extension[1..].ToUpperInvariant() : type) { ToolTip = type }]
            : [];
        this.Details = settings.ShowDetails
            ? new Details
            {
                Title = recentFile.DisplayName,
                Metadata =
                [
                    new DetailsElement { Key = Strings.Details_Path, Data = new DetailsLink { Text = recentFile.TargetPath } },
                    new DetailsElement { Key = Strings.Details_Type, Data = new DetailsLink { Text = type } },
                    new DetailsElement
                    {
                        Key = Strings.Details_LastUsed,
                        Data = new DetailsLink
                        {
                            Text = recentFile.LastUsedTimeUtc == DateTimeOffset.MinValue
                                ? Strings.Details_UnknownLastUsed
                                : recentFile.LastUsedTimeUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture),
                        },
                    },
                ],
            }
            : null;
    }

    internal static bool TryGetDirectBinaryIconPath(IRecentFile recentFile, out string iconPath)
    {
        iconPath = string.Empty;
        if (recentFile.IsFileSystemPath ||
            string.IsNullOrWhiteSpace(recentFile.IconPath) ||
            recentFile.IconPath.IsNetworkPath() ||
            !File.Exists(recentFile.IconPath))
        {
            return false;
        }

        var extension = Path.GetExtension(recentFile.IconPath);
        if (!extension.Equals(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // CmdPal's binary-icon parser recognizes lower-case executable extensions.
        iconPath = Path.ChangeExtension(recentFile.IconPath, extension.ToLowerInvariant());
        return true;
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

        // Cloud Office entries use the Office executable as their icon source.
        if (!string.IsNullOrWhiteSpace(recentShortcutFile.IconPath) &&
            RecentFilePath.IsFileSystemPath(recentShortcutFile.IconPath) &&
            !recentShortcutFile.IconPath.IsNetworkPath())
        {
            var icon = await TryGetThumbnailIconAsync(recentShortcutFile.IconPath).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            if (icon != null)
                return icon;
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Fall back to full path if it's not a network path
        if (recentShortcutFile.IsFileSystemPath && !recentShortcutFile.FullPath.IsNetworkPath())
        {
            var icon = await TryGetThumbnailIconAsync(recentShortcutFile.FullPath).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return icon;
        }

        return null;
    }

    internal static async Task<IconInfo?> TryGetThumbnailIconAsync(string path)
    {
        try
        {
            // Reuse gallery-sized sources when the view changes without reloading items.
            IRandomAccessStream? stream = null;
            if (File.Exists(path))
            {
                try
                {
                    var file = await StorageFile.GetFileFromPathAsync(path);
                    if (file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                    {
                        stream = await file.GetScaledImageAsThumbnailAsync(
                            ThumbnailMode.SingleItem, 256, ThumbnailOptions.ResizeThumbnail);
                    }
                }
                catch (Exception ex)
                {
                    HandleThumbnailException(ex, path);
                }
            }

            stream ??= await ThumbnailHelper.GetThumbnail(path, jumbo: true).ConfigureAwait(false);
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
