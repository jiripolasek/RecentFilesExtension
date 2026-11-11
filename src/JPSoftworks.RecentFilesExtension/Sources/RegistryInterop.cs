// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.Runtime.InteropServices;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

using JPSoftworks.RecentFilesExtension.Helpers;

namespace JPSoftworks.RecentFilesExtension.Sources;

internal sealed partial class RegistryChangeWatcher : IDisposable
{
    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(5);
    private readonly string _keyPath;
    private readonly RegistryView _registryView;
    private readonly uint _notifyFilter;
    private readonly bool _watchSubtree;
    private readonly AutoResetEvent _changedEvent;
    private readonly Lock _lifecycleLock = new();
    private readonly Lock _callbackLock = new();
    private RegisteredWaitHandle? _wait;
    private RegistryKey? _key;
    private bool _watchingTarget;
    private bool _reportedFailure;
    private TimeSpan _retryDelay = RetryInterval;
    private volatile bool _isDisposed;

    public RegistryChangeWatcher(string keyPath, RegistryView registryView,
        uint notifyFilter = NativeRegistryMethods.NotifyChangeName | NativeRegistryMethods.NotifyChangeLastSet,
        bool watchSubtree = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyPath);
        this._keyPath = keyPath;
        this._registryView = registryView;
        this._notifyFilter = notifyFilter | NativeRegistryMethods.NotifyChangeSecurity;
        this._watchSubtree = watchSubtree;
        this._changedEvent = new(false);
        try
        {
            lock (this._lifecycleLock)
            {
                this.Arm();
            }
        }
        catch
        {
            this.Dispose();
            throw;
        }
    }

    public event EventHandler? Changed;

    internal bool IsDisposed => this._isDisposed;

    private void Arm()
    {
        this.StopWaiting();
        this._watchingTarget = false;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            this._key = this.OpenNearestReadableKey(out var nextComponent);
            if (this._key is null)
            {
                break;
            }
            var watchingTarget = nextComponent is null;
            var error = NativeRegistryMethods.RegNotifyChangeKeyValue(
                this._key.Handle,
                watchingTarget && this._watchSubtree ? 1 : 0,
                (watchingTarget ? this._notifyFilter : NativeRegistryMethods.NotifyChangeName) |
                NativeRegistryMethods.NotifyThreadAgnostic,
                this._changedEvent.SafeWaitHandle,
                1);
            if (error == 0)
            {
                var timeout = Timeout.InfiniteTimeSpan;
                if (nextComponent is not null)
                {
                    // A path component may have appeared before the ancestor watch was armed.
                    using var nearest = this.OpenNearestReadableKey(out _);
                    if (!string.Equals(nearest?.Name, this._key.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        this.StopWaiting();
                        continue;
                    }
                    timeout = GetAncestorWaitTimeout(this._key, nextComponent);
                }
                this._watchingTarget = watchingTarget;
                this.RegisterWait(timeout);
                this._reportedFailure = false;
                this._retryDelay = RetryInterval;
                return;
            }
            this.StopWaiting();
            if (error != 1018) // A deleted key can be retried immediately against its parent.
            {
                throw new System.ComponentModel.Win32Exception(error);
            }
        }
        this.RegisterWait(RetryInterval);
    }

    private void RegisterWait(TimeSpan timeout)
    {
        this._wait = ThreadPool.RegisterWaitForSingleObject(this._changedEvent,
            this.OnSignal, null, timeout, executeOnlyOnce: true);
    }

    private void OnSignal(object? state, bool timedOut)
    {
        lock (this._callbackLock)
        {
            bool notify;
            try
            {
                lock (this._lifecycleLock)
                {
                    if (this._isDisposed)
                    {
                        return;
                    }
                    var wasWatchingTarget = this._watchingTarget;
                    this.Arm();
                    notify = timedOut
                        ? this._watchingTarget && !wasWatchingTarget
                        : this._watchingTarget || wasWatchingTarget;
                }
            }
            catch (Exception ex)
            {
                bool report;
                lock (this._lifecycleLock)
                {
                    if (this._isDisposed)
                    {
                        return;
                    }
                    report = !this._reportedFailure;
                    this._reportedFailure = true;
                    this.StopWaiting();
                    this._watchingTarget = false;
                    this.RegisterWait(this._retryDelay);
                    this._retryDelay = TimeSpan.FromSeconds(Math.Min(this._retryDelay.TotalSeconds * 2, 300));
                }
                if (report)
                {
                    Logger.LogError($"Registry change watcher will retry after a failure {ExceptionDiagnostics.Format(ex)}");
                }
                return;
            }

            // Re-arm before invoking subscribers, and serialize callbacks even when another event is already queued.
            if (notify)
            {
                foreach (EventHandler handler in this.Changed?.GetInvocationList() ?? [])
                {
                    if (this._isDisposed)
                    {
                        return;
                    }
                    try
                    {
                        handler(this, EventArgs.Empty);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError($"Failed to notify a registry change subscriber {ExceptionDiagnostics.Format(ex)}");
                    }
                }
            }
        }
    }

    internal static TimeSpan GetAncestorWaitTimeout(RegistryKey ancestor, string nextComponent)
    {
        // Creation signals the ancestor. Restoring access to an existing child may not.
        var children = RegistryReadHelper.TryGetSubKeyNames(ancestor);
        return children is not null && !children.Contains(nextComponent, StringComparer.OrdinalIgnoreCase)
            ? Timeout.InfiniteTimeSpan
            : RetryInterval;
    }

    private RegistryKey? OpenNearestReadableKey(out string? nextComponent)
    {
        nextComponent = null;
        using var hive = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, this._registryView);
        var candidate = this._keyPath;
        while (!string.IsNullOrEmpty(candidate))
        {
            var key = RegistryReadHelper.OpenReadableKey(hive, candidate);
            if (key is not null)
            {
                return key;
            }
            var separator = candidate.LastIndexOf('\\');
            nextComponent = candidate[(separator + 1)..];
            candidate = separator < 0 ? string.Empty : candidate[..separator];
        }
        return RegistryReadHelper.OpenReadableKey(hive, string.Empty);
    }

    private void StopWaiting()
    {
        this._wait?.Unregister(null);
        this._wait = null;
        this._key?.Dispose();
        this._key = null;
        this._changedEvent.Reset();
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
            this.StopWaiting();
            this._changedEvent.Dispose();
        }
    }
}

internal static partial class NativeRegistryMethods
{
    public const uint NotifyChangeName = 0x00000001;
    public const uint NotifyChangeLastSet = 0x00000004;
    public const uint NotifyChangeSecurity = 0x00000008;
    public const uint NotifyThreadAgnostic = 0x10000000;

    [LibraryImport("advapi32.dll")]
    public static partial int RegNotifyChangeKeyValue(
        SafeRegistryHandle key,
        int watchSubtree,
        uint notifyFilter,
        SafeWaitHandle eventHandle,
        int asynchronous);

    [LibraryImport("advapi32.dll", EntryPoint = "RegQueryInfoKeyW")]
    private static partial int RegQueryInfoKey(
        SafeRegistryHandle key,
        nint keyClass,
        nint keyClassLength,
        nint reserved,
        nint subKeyCount,
        nint maxSubKeyLength,
        nint maxClassLength,
        nint valueCount,
        nint maxValueNameLength,
        nint maxValueLength,
        nint securityDescriptorLength,
        out long lastWriteFileTime);

    public static DateTimeOffset? GetLastWriteTimeUtc(RegistryKey key)
    {
        var error = RegQueryInfoKey(
            key.Handle,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            out var fileTime);
        if (error != 0)
        {
            Logger.LogWarning($"Failed to read the last-write time of a registry key: error {error}");
            return null;
        }

        try
        {
            return new(DateTime.FromFileTimeUtc(fileTime), TimeSpan.Zero);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            Logger.LogError($"A registry key returned an invalid last-write time {ExceptionDiagnostics.Format(ex)}");
            return null;
        }
    }
}