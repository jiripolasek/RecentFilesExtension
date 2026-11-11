// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.ComponentModel;
using System.Runtime.ExceptionServices;
using JPSoftworks.RecentFilesExtension.Helpers;

namespace JPSoftworks.RecentFilesExtension.Sources;

internal sealed partial class RecentFolderWatcher : IDisposable
{
    private static readonly TimeSpan InitialRetryDelay = TimeSpan.FromSeconds(5);
    private readonly Lock _gate = new();
    private readonly ITimer _retryTimer;
    private readonly DirectoryInfo? _folder;
    private readonly string _description;
    private readonly Action _changed;
    private readonly string _filter;
    private readonly NotifyFilters _fileChanges;
    private readonly Func<string?, bool>? _acceptName;
    private FileSystemWatcher? _parent;
    private FileSystemWatcher? _files;
    private RecoveryState _recovery;
    private TimeSpan _retryDelay = InitialRetryDelay;
    private bool _disposed;

    public RecentFolderWatcher(string folderPath, string description, Action changed, string filter = "*",
        Func<string?, bool>? acceptName = null, NotifyFilters fileChanges = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
        TimeProvider? timeProvider = null)
    {
        this._folder = string.IsNullOrWhiteSpace(folderPath) ? null : new DirectoryInfo(folderPath);
        this._description = description;
        this._changed = changed;
        this._filter = filter;
        this._fileChanges = fileChanges;
        this._acceptName = acceptName;
        this._retryTimer = (timeProvider ?? TimeProvider.System).CreateTimer(
            _ => this.CheckRecovery(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        try
        {
            this.Refresh(initializing: true);
        }
        catch
        {
            this.Dispose();
            throw;
        }
    }

    public bool Refresh() => this.Refresh(initializing: false);

    private bool Refresh(bool initializing, bool directoryChanged = false)
    {
        Exception? failure = null;
        bool exists;
        lock (this._gate)
        {
            if (this._disposed || this._folder is null)
            {
                return false;
            }

            if (this._recovery == RecoveryState.WaitingToRetry && !directoryChanged)
            {
                return Directory.Exists(this._folder.FullName);
            }

            exists = this.BindWatchers(this._folder, out failure);
            if (initializing && failure is not null && (exists ? this._files is null : this._parent is null))
            {
                ExceptionDispatchInfo.Throw(failure);
            }
            if (failure is not null && !this.ScheduleRetry())
            {
                failure = null;
            }
        }
        if (failure is not null)
        {
            this.LogError(failure);
        }
        return exists;
    }

    private bool BindWatchers(DirectoryInfo folder, out Exception? failure)
    {
        failure = null;
        var previousParent = this._parent;
        // Arm the nearest parent's directory-name watch before testing whether the child appeared.
        var child = folder;
        while (child.Parent is { } parent)
        {
            if (!Directory.Exists(parent.FullName))
            {
                child = parent;
                continue;
            }
            this.Bind(ref this._parent, parent.FullName, child.Name, NotifyFilters.DirectoryName, ref failure);
            if (this._parent is null || child == folder || !Directory.Exists(child.FullName))
            {
                break;
            }
            child = folder;
        }
        if (child.Parent is null)
        {
            this._parent?.Dispose();
            this._parent = null;
        }
        if (this._parent is not null && this._parent != previousParent)
        {
            // A replacement directory could have taken the same name while the parent watch was offline.
            this._files?.Dispose();
            this._files = null;
        }
        var exists = Directory.Exists(folder.FullName);
        this.Bind(ref this._files, exists ? folder.FullName : null, this._filter, this._fileChanges, ref failure);
        return exists;
    }

    private void Bind(ref FileSystemWatcher? current, string? path, string filter, NotifyFilters changes, ref Exception? failure)
    {
        if (current is not null && string.Equals(current.Path, path, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(current.Filter, filter, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        current?.Dispose();
        current = null;
        if (path is null)
        {
            return;
        }

        try
        {
            current = new FileSystemWatcher(path, filter);
            current.NotifyFilter = changes;
            current.Created += this.OnChanged;
            current.Deleted += this.OnChanged;
            current.Renamed += this.OnChanged;
            current.Changed += this.OnChanged;
            current.Error += this.OnError;
            current.EnableRaisingEvents = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or Win32Exception)
        {
            current?.Dispose();
            current = null;
            failure ??= ex;
        }
    }

    private void OnChanged(object sender, FileSystemEventArgs e)
    {
        bool directoryChanged;
        lock (this._gate)
        {
            if (this._disposed)
            {
                return;
            }
            directoryChanged = ReferenceEquals(sender, this._parent);
            if (directoryChanged)
            {
                // The same path may now name a replacement directory.
                this._files?.Dispose();
                this._files = null;
            }
            else if (!ReferenceEquals(sender, this._files) ||
                     (this._acceptName is { } accept && !accept(e.Name) && (e is not RenamedEventArgs renamed || !accept(renamed.OldName))))
            {
                return;
            }
        }
        if (directoryChanged)
        {
            this.Refresh(initializing: false, directoryChanged: true);
        }
        this._changed();
    }

    private void OnError(object sender, ErrorEventArgs e)
    {
        bool report;
        lock (this._gate)
        {
            if (ReferenceEquals(sender, this._files))
            {
                this._files = null;
            }
            else if (ReferenceEquals(sender, this._parent))
            {
                this._parent = null;
            }
            else
            {
                return;
            }
            ((FileSystemWatcher)sender).Dispose();
            report = this.ScheduleRetry();
        }
        if (report)
        {
            this.LogError(e.GetException());
            this._changed();
        }
    }

    private bool ScheduleRetry()
    {
        var firstFailure = this._recovery == RecoveryState.Healthy;
        this._recovery = RecoveryState.WaitingToRetry;
        if (firstFailure)
        {
            this._retryTimer.Change(this._retryDelay, Timeout.InfiniteTimeSpan);
        }
        return firstFailure;
    }

    private void CheckRecovery()
    {
        lock (this._gate)
        {
            if (this._disposed || this._folder is null)
            {
                return;
            }
            if (this._recovery == RecoveryState.WaitingToRetry)
            {
                // A replacement can report an error after EnableRaisingEvents returns.
                this._recovery = RecoveryState.Verifying;
                this.BindWatchers(this._folder, out var failure);
                if (failure is not null || (this._files is null && this._parent is null))
                {
                    this._recovery = RecoveryState.WaitingToRetry;
                }
                this._retryDelay = TimeSpan.FromSeconds(Math.Min(this._retryDelay.TotalSeconds * 2, 300));
                this._retryTimer.Change(this._retryDelay, Timeout.InfiniteTimeSpan);
                return;
            }

            this._recovery = RecoveryState.Healthy;
            this._retryDelay = InitialRetryDelay;
        }
        // Reconcile changes missed while the watcher was offline.
        this._changed();
    }

    private void LogError(Exception error) => Logger.LogError($"{this._description} watcher reported an error {ExceptionDiagnostics.Format(error)}");

    public void Dispose()
    {
        lock (this._gate)
        {
            this._disposed = true;
            this._retryTimer.Dispose();
            this._parent?.Dispose();
            this._parent = null;
            this._files?.Dispose();
            this._files = null;
        }
    }

    private enum RecoveryState
    {
        Healthy,
        WaitingToRetry,
        Verifying,
    }
}