// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using JPSoftworks.RecentFilesExtension.Helpers;
using JPSoftworks.RecentFilesExtension.Model;

namespace JPSoftworks.RecentFilesExtension.Sources;

internal sealed partial class CompositeRecentFileSource : IRecentFileSource
{
    private const int MaxRecentFileCount = 500;
    private static readonly TimeSpan StartupRetryInterval = TimeSpan.FromSeconds(5);

    private readonly RecentFileSourceRegistration[] _registrations;
    private readonly IRecentFilesSettings? _settings;
    private readonly Lock _lifecycleLock = new();
    private readonly ITimer _startupRetryTimer;
    private readonly Dictionary<IRecentFileSource, int> _activeReads = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<IRecentFileSource> _retiredSources = new(ReferenceEqualityComparer.Instance);
    private TimeSpan _startupRetryDelay = StartupRetryInterval;
    private volatile bool _isDisposed;

    public CompositeRecentFileSource(IRecentFilesSettings settings)
        : this(
        new RecentFileSourceRegistration[]
        {
            new(static () => new RecentItemsFolderSource(), static () => true),
            new(static () => new OpenSaveMruSource(), () => settings.IncludeOpenSaveMru),
            new(static () => new OfficeRecentFolderSource(), () => settings.IncludeOfficeRecentFiles),
            new(static () => new OfficeJumpListSource(), () => settings.IncludeOfficeRecentFiles),
            new(static () => new OfficeRecentFilesSource(), () => settings.IncludeOfficeRecentFiles),
        },
        settings)
    {
    }

    internal CompositeRecentFileSource(IEnumerable<RecentFileSourceRegistration> registrations)
        : this(registrations, null)
    {
    }

    internal CompositeRecentFileSource(
        IEnumerable<RecentFileSourceRegistration> registrations,
        IRecentFilesSettings? settings,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(registrations);

        this._registrations = [.. registrations];
        this._settings = settings;
        this._startupRetryTimer = (timeProvider ?? TimeProvider.System).CreateTimer(
            _ => this.RetrySources(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        if (this._settings is not null)
        {
            this._settings.SourcesChanged += this.OnSettingsChanged;
        }
        this.UpdateSources();
    }

    public event EventHandler? Changed;

    public IReadOnlyList<IRecentFile> GetRecentFiles(CancellationToken cancellationToken)
    {
        IRecentFileSource[] sources;
        lock (this._lifecycleLock)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(this._isDisposed, this);
            sources = this._registrations
                .Where(static registration => registration.Source is not null && registration.IsEnabled())
                .Select(static registration => registration.Source!)
                .ToArray();
            foreach (var source in sources)
            {
                this._activeReads[source] = this._activeReads.GetValueOrDefault(source) + 1;
            }
        }
        var files = new List<IRecentFile>();
        try
        {
            foreach (var source in sources)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var sourceFiles = source.GetRecentFiles(cancellationToken);
                    lock (this._lifecycleLock)
                    {
                        if (!this._isDisposed && !this._retiredSources.Contains(source))
                        {
                            files.AddRange(sourceFiles);
                        }
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Failed to load {source.GetType().Name} {ExceptionDiagnostics.Format(ex)}");
                }
            }
        }
        finally
        {
            List<IRecentFileSource> stopped = [];
            lock (this._lifecycleLock)
            {
                foreach (var source in sources)
                {
                    if (--this._activeReads[source] == 0)
                    {
                        this._activeReads.Remove(source);
                        if (this._retiredSources.Remove(source))
                        {
                            stopped.Add(source);
                        }
                    }
                }
            }
            foreach (var source in stopped)
            {
                source.Dispose();
            }
        }

        return files
            .Where(static file => !file.IsFileSystemPath ||
                                  file.TargetPath.IsNetworkPath() ||
                                  File.Exists(file.TargetPath) ||
                                  Directory.Exists(file.TargetPath))
            .OrderByDescending(static file => file.LastUsedTimeUtc)
            .DistinctBy(
                static file => RecentFilePath.GetDeduplicationKey(file.TargetPath),
                StringComparer.Ordinal)
            .Take(MaxRecentFileCount)
            .ToArray();
    }

    private void OnSourceChanged(object? sender, EventArgs e)
    {
        if (this._isDisposed ||
            !this._registrations.Any(registration =>
                ReferenceEquals(registration.Source, sender) && registration.IsEnabled()))
        {
            return;
        }

        this.Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        this.UpdateSources();
        if (!this._isDisposed)
        {
            this.Changed?.Invoke(this, EventArgs.Empty);
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
            this._startupRetryTimer.Dispose();
            if (this._settings is not null)
            {
                this._settings.SourcesChanged -= this.OnSettingsChanged;
            }
        }
        this.UpdateSources();
    }

    private bool UpdateSources(bool retrying = false)
    {
        List<IRecentFileSource> stopped = [];
        var started = false;
        lock (this._lifecycleLock)
        {
            if (!retrying)
            {
                this._startupRetryDelay = StartupRetryInterval;
            }
            var retryRequired = false;
            foreach (var registration in this._registrations)
            {
                if (!this._isDisposed && registration.IsEnabled())
                {
                    if (registration.Source is null)
                    {
                        IRecentFileSource? startingSource = null;
                        try
                        {
                            startingSource = registration.CreateSource();
                            startingSource.Changed += this.OnSourceChanged;
                            registration.Source = startingSource;
                            if (registration.StartFailed)
                            {
                                Logger.LogInformation("A recent-files source recovered after a startup failure");
                            }
                            registration.StartFailed = false;
                            started = true;
                        }
                        catch (Exception ex)
                        {
                            if (startingSource is not null)
                            {
                                stopped.Add(startingSource);
                            }
                            if (!registration.StartFailed)
                            {
                                Logger.LogError($"Failed to start a recent-files source {ExceptionDiagnostics.Format(ex)}");
                                registration.StartFailed = true;
                            }
                            retryRequired = true;
                        }
                    }
                }
                else if (registration.Source is { } source)
                {
                    registration.Source = null;
                    source.Changed -= this.OnSourceChanged;
                    if (this._activeReads.ContainsKey(source))
                    {
                        this._retiredSources.Add(source);
                    }
                    else
                    {
                        stopped.Add(source);
                    }
                }
                if (this._isDisposed || !registration.IsEnabled())
                {
                    registration.StartFailed = false;
                }
            }
            if (!this._isDisposed)
            {
                this._startupRetryTimer.Change(retryRequired ? this._startupRetryDelay : Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                this._startupRetryDelay = retryRequired
                    ? TimeSpan.FromSeconds(Math.Min(this._startupRetryDelay.TotalSeconds * 2, 300))
                    : StartupRetryInterval;
            }
        }

        // A watcher may still be delivering a callback that needs to read the composite.
        foreach (var source in stopped)
        {
            source.Dispose();
        }
        return started;
    }

    private void RetrySources()
    {
        try
        {
            if (this.UpdateSources(retrying: true) && !this._isDisposed)
            {
                this.Changed?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to retry recent-files sources or notify subscribers {ExceptionDiagnostics.Format(ex)}");
        }
    }
}