// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.Diagnostics;
using JPSoftworks.RecentFilesExtension.Helpers;
using JPSoftworks.RecentFilesExtension.Model;
using JPSoftworks.RecentFilesExtension.Sources;

namespace JPSoftworks.RecentFilesExtension.Pages;

internal sealed partial class RecentFilesProvider : IDisposable, IRecentFilesProvider
{
    private static readonly TimeSpan RefreshDelay = TimeSpan.FromMilliseconds(500);
    private readonly IRecentFileSource _source;
    private bool _first = true;

    // cache + sync
    private readonly Lock _syncRoot = new();
    private readonly Lock _lifecycleLock = new();
    private List<IRecentFile> _cachedRecentFiles = [];
    private bool _isInitialLoadComplete;
    private bool _isRefreshInProgress;
    private bool _isDisposed;

    private readonly System.Timers.Timer _reloadTimer = new(RefreshDelay) { AutoReset = false };
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

    internal RecentFilesProvider(IRecentFilesSettings settings)
        : this(new CompositeRecentFileSource(settings))
    {
    }

    internal RecentFilesProvider(IRecentFileSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        this._source = source;
        this._cancellationToken = this._cancellationTokenSource.Token;
        this._source.Changed += this.OnSourceChanged;
        this._reloadTimer.Elapsed += (_, _) => this.RefreshCache();

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

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

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
        var loaded = false;
        Logger.LogDebug("Initial cache load started");

        try
        {
            loaded = await Task.Run(this.RefreshCache, this._cancellationToken);
            this._cancellationToken.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException) when (this._cancellationToken.IsCancellationRequested)
        {
            Logger.LogDebug($"Initial cache load canceled after {stopwatch.ElapsedMilliseconds}ms");
            return;
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to complete initial cache load after {stopwatch.ElapsedMilliseconds}ms {ExceptionDiagnostics.Format(ex)}");
        }

        int itemCount;
        lock (this._syncRoot)
        {
            this._isInitialLoadComplete = true;
            itemCount = this._cachedRecentFiles.Count;
        }

        if (loaded)
        {
            Logger.LogInformation($"Initial cache load completed | Items: {itemCount}, took {stopwatch.ElapsedMilliseconds}ms");
        }
        else
        {
            Logger.LogWarning($"Initial cache load finished without a successful refresh | Items: {itemCount}, took {stopwatch.ElapsedMilliseconds}ms");
        }
        foreach (EventHandler handler in this.InitialLoadComplete?.GetInvocationList() ?? [])
        {
            try
            {
                handler(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to notify a subscriber that initial loading completed {ExceptionDiagnostics.Format(ex)}");
            }
        }
    }

    private void OnSourceChanged(object? sender, EventArgs e)
    {
        lock (this._lifecycleLock)
        {
            if (this._isDisposed)
            {
                return;
            }

            this.ScheduleReload();
        }
    }

    private bool RefreshCache()
    {
        if (!this.TryBeginRefresh())
        {
            return false;
        }

        try
        {
            List<IRecentFile> newList = [.. this._source.GetRecentFiles(this._cancellationToken)];

            if (this._cancellationToken.IsCancellationRequested)
            {
                return false;
            }

            bool changed;
            lock (this._syncRoot)
            {
                var stopwatch = Stopwatch.StartNew();
                changed = !HaveSameItems(newList, this._cachedRecentFiles);

                Logger.LogDebug($"Recent files refreshed | Cache updated: {changed}, took {stopwatch.ElapsedMilliseconds}ms");
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

            return true;
        }
        catch (OperationCanceledException) when (this._cancellationToken.IsCancellationRequested)
        {
            // Disposing the provider cancels the current refresh.
        }
        catch (Exception ex)
        {
            this._first = true;
            Logger.LogError($"Failed to refresh recent files or notify subscribers {ExceptionDiagnostics.Format(ex)}");
        }
        finally
        {
            this.EndRefresh();
        }

        return false;
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
                this.ScheduleReload();
                return false;
            }

            this._isRefreshInProgress = true;
            return true;
        }
    }

    private void ScheduleReload()
    {
        if (!this._reloadTimer.Enabled)
        {
            this._reloadTimer.Start();
        }
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
            if (!HasSameMetadata(first[i], second[i]))
            {
                return false;
            }
        }

        return true;
    }

    internal static bool HasSameMetadata(IRecentFile first, IRecentFile second)
    {
        try
        {
            return StringComparer.OrdinalIgnoreCase.Equals(first.FullPath, second.FullPath) &&
                   StringComparer.Ordinal.Equals(first.DisplayName, second.DisplayName) &&
                   StringComparer.Ordinal.Equals(first.TargetPath, second.TargetPath) &&
                   StringComparer.OrdinalIgnoreCase.Equals(first.IconPath, second.IconPath) &&
                   first.IsFileSystemPath == second.IsFileSystemPath &&
                   first.IsDirectory() == second.IsDirectory() &&
                   first.LastUsedTimeUtc == second.LastUsedTimeUtc;
        }
        catch
        {
            // Unreadable metadata is not evidence that the cached snapshot is still current.
            return false;
        }
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

        this._source.Changed -= this.OnSourceChanged;
        this._source.Dispose();
        this._reloadTimer.Dispose();
        this._cancellationTokenSource.Dispose();
    }
}