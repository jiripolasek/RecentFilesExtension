// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.Runtime.InteropServices;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace JPSoftworks.RecentFilesExtension.Sources;

internal static class RegistryViews
{
    public static RegistryView[] Supported { get; } = Environment.Is64BitOperatingSystem
        ? [RegistryView.Registry64, RegistryView.Registry32]
        : [RegistryView.Default];
}

internal sealed partial class RegistryChangeWatcher : IDisposable
{
    private readonly string _keyPath;
    private readonly RegistryView _registryView;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly Thread _watchThread;
    private bool _isDisposed;

    public RegistryChangeWatcher(string keyPath, RegistryView registryView)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyPath);

        this._keyPath = keyPath;
        this._registryView = registryView;
        this._watchThread = new(this.Watch)
        {
            IsBackground = true,
            Name = $"RecentFiles registry watcher ({registryView})",
        };
        this._watchThread.Start();
    }

    public event EventHandler? Changed;

    private void Watch()
    {
        using var changedEvent = new AutoResetEvent(false);
        var waitHandles = new WaitHandle[]
        {
            changedEvent,
            this._cancellationTokenSource.Token.WaitHandle,
        };

        while (!this._cancellationTokenSource.IsCancellationRequested)
        {
            try
            {
                using var key = this.OpenNearestExistingKey();
                if (key is null)
                {
                    return;
                }

                var error = NativeRegistryMethods.RegNotifyChangeKeyValue(
                    key.Handle,
                    1,
                    NativeRegistryMethods.NotifyChangeName |
                    NativeRegistryMethods.NotifyChangeLastSet |
                    NativeRegistryMethods.NotifyThreadAgnostic,
                    changedEvent.SafeWaitHandle,
                    1);
                if (error != 0)
                {
                    Logger.LogWarning($"Registry change notification failed with error {error}");
                    return;
                }

                if (WaitHandle.WaitAny(waitHandles) != 0 &&
                    !this._cancellationTokenSource.IsCancellationRequested)
                {
                    continue;
                }

                if (!this._cancellationTokenSource.IsCancellationRequested)
                {
                    this.Changed?.Invoke(this, EventArgs.Empty);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ObjectDisposedException)
            {
                if (!this._cancellationTokenSource.IsCancellationRequested)
                {
                    Logger.LogError("Registry change watcher stopped unexpectedly", ex);
                }

                return;
            }
        }
    }

    private RegistryKey? OpenNearestExistingKey()
    {
        using var hive = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, this._registryView);
        var candidate = this._keyPath;
        while (!string.IsNullOrEmpty(candidate))
        {
            var key = hive.OpenSubKey(candidate, writable: false);
            if (key is not null)
            {
                return key;
            }

            var separator = candidate.LastIndexOf('\\');
            candidate = separator < 0 ? string.Empty : candidate[..separator];
        }

        return null;
    }

    public void Dispose()
    {
        if (this._isDisposed)
        {
            return;
        }

        this._isDisposed = true;
        this._cancellationTokenSource.Cancel();
        if (Thread.CurrentThread != this._watchThread)
        {
            this._watchThread.Join(TimeSpan.FromSeconds(2));
        }

        this._cancellationTokenSource.Dispose();
    }
}

internal static partial class NativeRegistryMethods
{
    public const uint NotifyChangeName = 0x00000001;
    public const uint NotifyChangeLastSet = 0x00000004;
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
        if (error != 0 || fileTime < 0)
        {
            return null;
        }

        try
        {
            return new(DateTime.FromFileTimeUtc(fileTime), TimeSpan.Zero);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }
}