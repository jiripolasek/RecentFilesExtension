// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.Diagnostics;
using JPSoftworks.RecentFilesExtension.Helpers;
using JPSoftworks.RecentFilesExtension.Model;

namespace JPSoftworks.RecentFilesExtension.Pages;

internal partial class RecentFilesProvider : IDisposable, IRecentFilesProvider
{
    private static readonly TimeSpan RefreshDebounceInterval = TimeSpan.FromMilliseconds(500);
    private const int MaxRecentFileCount = 500;
    private const string ShortcutFileSearchPattern = "*.lnk";

    private readonly CachedShellLinksHelper _shellLinksHelper = new();
    private readonly FileSystemWatcher? _fileSystemWatcher;
    private readonly string _recentFilesFolderPath;
    private bool _first = true;

    // cache + sync
    private readonly Lock _syncRoot = new();
    private readonly Lock _lifecycleLock = new();
    private List<IRecentFile> _cachedRecentFiles = [];
    private bool _isInitialLoadComplete;
    private bool _isRefreshInProgress;
    private bool _isDisposed;

    private readonly System.Timers.Timer _reloadTimer = new(RefreshDebounceInterval) { AutoReset = false };
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly CancellationToken _cancellationToken;

    /// <summary>
    /// Fired whenever the list of recent files actually changes.
    /// </summary>
    public event EventHandler<IList<IRecentFile>>? RecentFilesChanged;

    /// <summary>
    /// Fired when the initial cache loading is complete.
    /// </summary>
    public event EventHandler? InitialLoadComplete;

    internal RecentFilesProvider()
    {
        this._cancellationToken = this._cancellationTokenSource.Token;
        this._recentFilesFolderPath = Environment.GetFolderPath(Environment.SpecialFolder.Recent);

        if (!string.IsNullOrWhiteSpace(this._recentFilesFolderPath))
        {
            this._fileSystemWatcher = new(this._recentFilesFolderPath, ShortcutFileSearchPattern)
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

        // Start initial cache loading on background thread
        _ = Task.Run(this.InitialCacheLoadAsync, this._cancellationToken);
    }

    /// <summary>
    /// Returns the *cached* list of recent files.
    /// Callers should subscribe to <see cref="RecentFilesChanged"/> for updates.
    /// </summary>
    public IList<IRecentFile> GetRecentFiles()
    {
        lock (this._syncRoot)
        {
            return [.. this._cachedRecentFiles];
        }
    }

    /// <summary>
    /// Returns true if the initial cache load has completed.
    /// </summary>
    public bool IsInitialLoadComplete
    {
        get
        {
            lock (this._syncRoot)
            {
                return this._isInitialLoadComplete;
            }
        }
    }

    /// <summary>
    /// Asynchronously waits for the initial cache load to complete.
    /// </summary>
    /// <param name="timeout">Maximum time to wait. If null, waits indefinitely.</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if load completed within timeout, false if timed out</returns>
    public async Task<bool> WaitForInitialLoadAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        if (this.IsInitialLoadComplete)
            return true;

        using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, this._cancellationToken);

        var tcs = new TaskCompletionSource<bool>();

        void OnInitialLoadComplete(object? sender, EventArgs e)
        {
            tcs.TrySetResult(true);
        }

        try
        {
            this.InitialLoadComplete += OnInitialLoadComplete;

            // Check again after subscribing to avoid race condition
            if (this.IsInitialLoadComplete)
                return true;

            if (timeout.HasValue)
            {
                using var timeoutCts = new CancellationTokenSource(timeout.Value);
                using var finalCts = CancellationTokenSource.CreateLinkedTokenSource(combinedCts.Token, timeoutCts.Token);

                try
                {
                    await tcs.Task.WaitAsync(finalCts.Token);
                    return true;
                }
                catch (OperationCanceledException) when (timeoutCts.Token.IsCancellationRequested)
                {
                    return false; // Timeout
                }
            }
            else
            {
                await tcs.Task.WaitAsync(combinedCts.Token);
                return true;
            }
        }
        finally
        {
            this.InitialLoadComplete -= OnInitialLoadComplete;
        }
    }

    private async Task InitialCacheLoadAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        Logger.LogDebug("Initial cache load started");

        try
        {
            await Task.Run(this.RefreshCache, this._cancellationToken);
            this._cancellationToken.ThrowIfCancellationRequested();

            int itemCount;
            lock (this._syncRoot)
            {
                this._isInitialLoadComplete = true;
                itemCount = this._cachedRecentFiles.Count;
            }

            Logger.LogDebug($"Initial cache load completed | Items: {itemCount}, took {stopwatch.ElapsedMilliseconds}ms");
            this.InitialLoadComplete?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException)
        {
            Logger.LogDebug($"Initial cache load canceled after {stopwatch.ElapsedMilliseconds}ms");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to complete initial cache load after {stopwatch.ElapsedMilliseconds}ms", ex);

            lock (this._syncRoot)
            {
                this._isInitialLoadComplete = true;
            }

            this.InitialLoadComplete?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnFsEvent(object sender, FileSystemEventArgs e)
    {
        lock (this._lifecycleLock)
        {
            if (this._isDisposed)
            {
                return;
            }

            Logger.LogDebug($"File system event: {e.ChangeType} {e.Name} | {e.FullPath}");
            this.RestartReloadTimer();
        }
    }

    private void RefreshCache()
    {
        if (!this.TryBeginRefresh())
        {
            return;
        }

        try
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

            if (this._cancellationToken.IsCancellationRequested)
            {
                return;
            }

            bool changed;
            lock (this._syncRoot)
            {
                var stopwatch = Stopwatch.StartNew();
                changed = !HaveSameItems(newList, this._cachedRecentFiles);

                Logger.LogDebug($"Refresh 30 | Cache updated: {changed}, took {stopwatch.ElapsedMilliseconds}ms");
                stopwatch.Stop();

                if (changed)
                {
                    this._cachedRecentFiles = newList;
                }
            }

            if (!this._cancellationToken.IsCancellationRequested && (changed || this._first))
            {
                this._first = false;
                this.RecentFilesChanged?.Invoke(null, [.. newList]);
            }
        }
        finally
        {
            this.EndRefresh();
        }
    }

    private bool TryBeginRefresh()
    {
        lock (this._lifecycleLock)
        {
            if (this._isDisposed || this._cancellationToken.IsCancellationRequested)
            {
                return false;
            }

            if (this._isRefreshInProgress)
            {
                this.RestartReloadTimer();
                return false;
            }

            this._isRefreshInProgress = true;
            return true;
        }
    }

    private void RestartReloadTimer()
    {
        this._reloadTimer.Stop();
        this._reloadTimer.Start();
    }

    private void EndRefresh()
    {
        lock (this._lifecycleLock)
        {
            this._isRefreshInProgress = false;
        }
    }

    private static bool HaveSameItems(List<IRecentFile> first, List<IRecentFile> second)
    {
        if (first.Count != second.Count)
        {
            return false;
        }

        for (var i = 0; i < first.Count; i++)
        {
            var firstItem = first[i];
            var secondItem = second[i];

            if (!StringComparer.OrdinalIgnoreCase.Equals(firstItem.FullPath, secondItem.FullPath) ||
                !StringComparer.Ordinal.Equals(firstItem.DisplayName, secondItem.DisplayName) ||
                !StringComparer.Ordinal.Equals(firstItem.TargetPath, secondItem.TargetPath))
            {
                return false;
            }
        }

        return true;
    }

    private List<IRecentFile> LoadRecentFilesFromDisk()
    {
        var stopwatch = Stopwatch.StartNew();

        var recentFilesShortcuts = new DirectoryInfo(this._recentFilesFolderPath)
            .GetFiles(ShortcutFileSearchPattern, SearchOption.TopDirectoryOnly)
            .OrderByDescending(static t => t.LastWriteTimeUtc)
            .ToArray();

        Logger.LogDebug($"Refresh 10 | Found {recentFilesShortcuts.Length} recent files shortcuts in {this._recentFilesFolderPath} in {stopwatch.ElapsedMilliseconds}ms");

        var items = new List<IRecentFile>();
        var addedTargetPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var counter = 0;

        foreach (var fileInfo in recentFilesShortcuts)
        {
            if (counter >= MaxRecentFileCount)
                break;

            if (this._cancellationToken.IsCancellationRequested)
                break;

            try
            {
                if (!this._shellLinksHelper.TryGetShellLink(fileInfo, out var shortcutFile))
                    continue;

                // avoid duplicates
                var isNetwork = shortcutFile.TargetPath.IsNetworkPath();
                var key = isNetwork ? shortcutFile.TargetPath : shortcutFile.TargetPath.ToLowerInvariant();
                if (!addedTargetPaths.Add(key))
                    continue;

                // drop parent-folder entries if immediately followed by a file in that folder
                if (items.Count > 0 && items[^1].TargetPath.Equals(Path.GetDirectoryName(shortcutFile.TargetPath), StringComparison.OrdinalIgnoreCase))
                {
                    counter--;
                    items.RemoveAt(items.Count - 1);
                }

                // skip if it's the same as last
                if (items.Count > 0 && items[^1].TargetPath.Equals(shortcutFile.TargetPath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                items.Add(shortcutFile);
                counter++;
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to process shortcut {fileInfo.FullName}", ex);
            }
        }

        Logger.LogDebug($"Refresh 20 | Loaded {items.Count} recent files in {stopwatch.ElapsedMilliseconds}ms");

        stopwatch.Stop();

        return items;
    }

    public void Dispose()
    {
        lock (this._lifecycleLock)
        {
            if (this._isDisposed)
            {
                return;
            }

            this._isDisposed = true;
            this._cancellationTokenSource.Cancel();
        }

        this._fileSystemWatcher?.Dispose();
        this._reloadTimer.Dispose();
        this._cancellationTokenSource.Dispose();
    }
}
