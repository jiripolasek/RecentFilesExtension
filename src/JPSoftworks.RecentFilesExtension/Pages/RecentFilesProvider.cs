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
    private List<IRecentFile> _cachedRecentFiles = [];
    private bool _isInitialLoadComplete;

    private readonly System.Timers.Timer _reloadTimer = new(RefreshDebounceInterval) { AutoReset = false };
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly SemaphoreSlim _refreshSemaphore = new(1, 1);

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
        _ = Task.Run(this.InitialCacheLoadAsync, this._cancellationTokenSource.Token);
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
            cancellationToken, this._cancellationTokenSource.Token);

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
        try
        {
            await Task.Run(this.RefreshCache, this._cancellationTokenSource.Token);

            lock (this._syncRoot)
            {
                this._isInitialLoadComplete = true;
            }

            this.InitialLoadComplete?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException)
        {
            // Expected when disposing
        }
        catch (Exception ex)
        {
            Logger.LogError("Failed to complete initial cache load", ex);
            
            lock (this._syncRoot)
            {
                this._isInitialLoadComplete = true;
            }

            this.InitialLoadComplete?.Invoke(this, EventArgs.Empty);
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
        if (this._cancellationTokenSource.Token.IsCancellationRequested)
            return;

        // Try to acquire the semaphore immediately - if can't, it means refresh is already in progress
        if (!this._refreshSemaphore.Wait(0))
        {
            // If a refresh is already in progress, schedule another one for later
            // This ensures we don't miss changes that occurred during the current refresh
            this._reloadTimer.Stop();
            this._reloadTimer.Start();
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

            bool changed;
            lock (this._syncRoot)
            {
                var stopwatch = Stopwatch.StartNew();
                changed = newList.Count != this._cachedRecentFiles.Count
                          || !newList.Select(static i => i.TargetPath)
                                     .SequenceEqual(this._cachedRecentFiles.Select(static i => i.TargetPath), StringComparer.OrdinalIgnoreCase);
                
                Logger.LogDebug($"Refresh 30 | Cache updated: {changed}, took {stopwatch.ElapsedMilliseconds}ms");
                stopwatch.Stop();

                if (changed)
                {
                    this._cachedRecentFiles = newList;
                }
            }

            if (changed || this._first)
            {
                this._first = false;
                this.RecentFilesChanged?.Invoke(null, [.. newList]);
            }
        }
        finally
        {
            this._refreshSemaphore.Release();
        }
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

            if (this._cancellationTokenSource.Token.IsCancellationRequested)
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

        Logger.LogDebug($"Refresh 20 | Finished in {stopwatch.ElapsedMilliseconds}ms");
        
        stopwatch.Stop();

        return items;
    }

    public void Dispose()
    {
        this._cancellationTokenSource.Cancel();
        this._fileSystemWatcher?.Dispose();
        this._reloadTimer.Dispose();
        this._refreshSemaphore.Dispose();
        this._cancellationTokenSource.Dispose();
    }
}