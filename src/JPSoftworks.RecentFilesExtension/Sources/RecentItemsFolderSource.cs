// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.Diagnostics;
using JPSoftworks.RecentFilesExtension.Helpers;
using JPSoftworks.RecentFilesExtension.Model;
using JPSoftworks.RecentFilesExtension.Pages;

namespace JPSoftworks.RecentFilesExtension.Sources;

internal sealed partial class RecentItemsFolderSource : IRecentFileSource
{
    private const int MaxRecentFileCount = 500;
    private const string ShortcutFileSearchPattern = "*.lnk";

    private readonly CachedShellLinksHelper _shellLinksHelper = new();
    private readonly FileSystemWatcher? _watcher;
    private readonly string _folderPath;
    private bool _isDisposed;

    public RecentItemsFolderSource()
    {
        this._folderPath = Environment.GetFolderPath(Environment.SpecialFolder.Recent);
        if (string.IsNullOrWhiteSpace(this._folderPath) || !Directory.Exists(this._folderPath))
        {
            return;
        }

        this._watcher = new(this._folderPath, ShortcutFileSearchPattern)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
            IncludeSubdirectories = false,
            EnableRaisingEvents = true,
        };
        this._watcher.Created += this.OnFileSystemChanged;
        this._watcher.Deleted += this.OnFileSystemChanged;
        this._watcher.Renamed += this.OnFileSystemChanged;
        this._watcher.Changed += this.OnFileSystemChanged;
    }

    public event EventHandler? Changed;

    public IReadOnlyList<IRecentFile> GetRecentFiles(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(this._folderPath) || !Directory.Exists(this._folderPath))
        {
            return Array.Empty<IRecentFile>();
        }

        var stopwatch = Stopwatch.StartNew();
        var shortcuts = new DirectoryInfo(this._folderPath)
            .GetFiles(ShortcutFileSearchPattern, SearchOption.TopDirectoryOnly)
            .OrderByDescending(static file => file.LastWriteTimeUtc);

        var items = new List<IRecentFile>();
        var addedTargetPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var fileInfo in shortcuts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (items.Count >= MaxRecentFileCount)
            {
                break;
            }

            try
            {
                if (!this._shellLinksHelper.TryGetShellLink(fileInfo, out var shortcutFile) ||
                    !addedTargetPaths.Add(shortcutFile.TargetPath))
                {
                    continue;
                }

                if (items.Count > 0 && items[^1].TargetPath.Equals(
                        Path.GetDirectoryName(shortcutFile.TargetPath),
                        StringComparison.OrdinalIgnoreCase))
                {
                    items.RemoveAt(items.Count - 1);
                }

                items.Add(shortcutFile);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to process recent shortcut {fileInfo.FullName}", ex);
            }
        }

        Logger.LogDebug($"Loaded {items.Count} recent-folder items in {stopwatch.ElapsedMilliseconds}ms");
        return items;
    }

    private void OnFileSystemChanged(object sender, FileSystemEventArgs e)
    {
        if (!this._isDisposed)
        {
            this.Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose()
    {
        if (this._isDisposed)
        {
            return;
        }

        this._isDisposed = true;
        if (this._watcher is not null)
        {
            this._watcher.Created -= this.OnFileSystemChanged;
            this._watcher.Deleted -= this.OnFileSystemChanged;
            this._watcher.Renamed -= this.OnFileSystemChanged;
            this._watcher.Changed -= this.OnFileSystemChanged;
            this._watcher.Dispose();
        }
    }
}