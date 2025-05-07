// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

using JPSoftworks.RecentFilesExtension.Helpers;
using JPSoftworks.RecentFilesExtension.Model;

namespace JPSoftworks.RecentFilesExtension.Pages;

internal partial class RecentFilesProvider : IDisposable
{
    private const int MaxRecentFileCount = 500;

    private readonly FileSystemWatcher? _fileSystemWatcher;
    private readonly string _recentFilesFolderPath;

    // cache + sync
    private readonly Lock _syncRoot = new();
    private List<IRecentFile> _cachedRecentFiles = [];

    private readonly System.Timers.Timer _reloadTimer = new(500) { AutoReset = false };

    /// <summary>
    /// Fired whenever the *list* of recent files actually changes.
    /// </summary>
    public event EventHandler<IList<IRecentFile>>? RecentFilesChanged;

    internal RecentFilesProvider()
    {
        this._recentFilesFolderPath = Environment.GetFolderPath(Environment.SpecialFolder.Recent);

        if (!string.IsNullOrWhiteSpace(this._recentFilesFolderPath))
        {
            this._fileSystemWatcher = new FileSystemWatcher(this._recentFilesFolderPath, "*.lnk")
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                IncludeSubdirectories = false,
                EnableRaisingEvents = true
            };
            this._fileSystemWatcher.Created += this.OnFsEvent;
            this._fileSystemWatcher.Deleted += this.OnFsEvent;
            this._fileSystemWatcher.Renamed += this.OnFsEvent;
            this._fileSystemWatcher.Changed += this.OnFsEvent;

            this._reloadTimer.Elapsed += (_, _) => this.RefreshCache();
        }

        this.RefreshCache();
    }

    /// <summary>
    /// Returns the *cached* list of recent files.  
    /// Callers should subscribe to <see cref="RecentFilesChanged"/> for updates.
    /// </summary>
    internal IList<IRecentFile> GetRecentFiles()
    {
        lock (this._syncRoot)
        {
            return [.. this._cachedRecentFiles];
        }
    }

    private void OnFsEvent(object sender, FileSystemEventArgs e)
    {
        Logger.LogDebug($"File system event: {e.ChangeType} {e.Name} | {e.FullPath}");

        this._reloadTimer.Stop();
        this._reloadTimer.Start();
    }

    private void RefreshCache()
    {
        List<IRecentFile> newList;
        try
        {
            newList = this.LoadRecentFilesFromDisk();
        }
        catch (Exception ex)
        {
            Logger.LogError("Failed to reload recent files", ex);
            return;
        }

        bool changed;
        lock (this._syncRoot)
        {
            changed = newList.Count != this._cachedRecentFiles.Count
                      || !newList.Select(static i => i.TargetPath)
                                 .SequenceEqual(this._cachedRecentFiles.Select(static i => i.TargetPath), StringComparer.OrdinalIgnoreCase);

            if (changed)
            {
                this._cachedRecentFiles = newList;
            }
        }

        if (changed)
        {
            this.RecentFilesChanged?.Invoke(null, [.. newList]);
        }
    }

    private List<IRecentFile> LoadRecentFilesFromDisk()
    {
        var recentFilesShortcuts = new DirectoryInfo(this._recentFilesFolderPath)
            .GetFiles("*.lnk", SearchOption.TopDirectoryOnly)
            .OrderByDescending(static t => t.LastWriteTimeUtc);

        var items = new List<IRecentFile>();
        var addedTargetPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var counter = 0;

        foreach (var fileInfo in recentFilesShortcuts)
        {
            if (counter >= MaxRecentFileCount)
                break;

            try
            {
                var shellLink = new ShellLinkHelper();
                shellLink.RetrieveTargetPath(fileInfo.FullName);

                var target = shellLink.TargetPath;
                if (string.IsNullOrWhiteSpace(target))
                    continue;

                var isNetwork = target.StartsWith(@"\\", StringComparison.OrdinalIgnoreCase) || target.StartsWith(@"\\?\UNC", StringComparison.OrdinalIgnoreCase);
                if (!isNetwork && !File.Exists(target) && !Directory.Exists(target))
                    continue;

                // avoid duplicates
                var key = isNetwork ? target : target.ToLowerInvariant();
                if (!addedTargetPaths.Add(key))
                    continue;

                // drop parent-folder entries if immediately followed by a file in that folder
                if (items.Count > 0
                    && items[^1].TargetPath.Equals(Path.GetDirectoryName(target), StringComparison.OrdinalIgnoreCase))
                {
                    items.RemoveAt(items.Count - 1);
                }

                // skip if it's the same as last
                if (items.Count > 0
                    && items[^1].TargetPath.Equals(target, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                items.Add(new RecentShortcutFile(fileInfo.FullName, shellLink.DisplayName, target));
                counter++;
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to process shortcut {fileInfo.FullName}", ex);
            }
        }

        return items;
    }

    public void Dispose()
    {
        this._fileSystemWatcher?.Dispose();
        this._reloadTimer.Dispose();
    }
}