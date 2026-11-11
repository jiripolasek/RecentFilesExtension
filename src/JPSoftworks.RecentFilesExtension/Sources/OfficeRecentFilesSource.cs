// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.Globalization;
using System.Security;
using Microsoft.Win32;
using JPSoftworks.RecentFilesExtension.Model;

using JPSoftworks.RecentFilesExtension.Helpers;

using static JPSoftworks.RecentFilesExtension.Sources.RegistryReadHelper;

namespace JPSoftworks.RecentFilesExtension.Sources;

internal sealed partial class OfficeRecentFilesSource : IRecentFileSource
{
    private const string OfficePath = @"Software\Microsoft\Office";
    private const string FileMruKeyName = "File MRU";
    private const string UserMruKeyName = "User MRU";

    private readonly string _officePath;
    private readonly RegistryChangeWatcher _watcher;
    private readonly ITimer _checkTimer;
    private readonly Lock _watchLock = new();
    private Dictionary<string, DateTimeOffset?> _mruTimestamps;
    private bool _checkPending;
    private volatile bool _isDisposed;

    public OfficeRecentFilesSource()
        : this(OfficePath)
    {
    }

    internal OfficeRecentFilesSource(string officePath, TimeProvider? timeProvider = null)
    {
        this._officePath = officePath;
        this._watcher = new(officePath, RegistryView.Default);
        try
        {
            this._checkTimer = (timeProvider ?? TimeProvider.System).CreateTimer(
                _ => this.CheckForMruChanges(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            lock (this._watchLock)
            {
                this._watcher.Changed += this.OnRegistryChanged;
                this._mruTimestamps = this.ReadMruTimestamps();
            }
        }
        catch
        {
            this.Dispose();
            throw;
        }
    }

    public event EventHandler? Changed;

    public IReadOnlyList<IRecentFile> GetRecentFiles(CancellationToken cancellationToken)
    {
        List<IRecentFile> items = [];
        foreach (var fileMru in this.GetFileMruKeys(cancellationToken))
        {
            try
            {
                ReadFileMru(fileMru, items);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
            {
                // Permissions can change after opening a key; keep the other applications' entries.
            }
        }

        return items
            .OrderByDescending(static item => item.LastUsedTimeUtc)
            .DistinctBy(
                static item => RecentFilePath.GetDeduplicationKey(item.TargetPath),
                StringComparer.Ordinal)
            .ToArray();
    }

    private IEnumerable<RegistryKey> GetFileMruKeys(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var hive = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default);
        using var office = OpenReadableKey(hive, this._officePath);
        if (office is null)
        {
            yield break;
        }

        foreach (var versionName in GetReadableSubKeyNames(office)
                     .Where(static name => Version.TryParse(name, out _))
                     .OrderByDescending(static name => Version.Parse(name)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var version = OpenReadableKey(office, versionName);
            if (version is null)
            {
                continue;
            }

            foreach (var applicationName in GetReadableSubKeyNames(version))
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var application = OpenReadableKey(version, applicationName);
                if (application is null)
                {
                    continue;
                }

                foreach (var fileMru in GetApplicationFileMruKeys(application, cancellationToken))
                {
                    yield return fileMru;
                }
            }
        }
    }

    private static IEnumerable<RegistryKey> GetApplicationFileMruKeys(
        RegistryKey application,
        CancellationToken cancellationToken)
    {
        using (var directFileMru = OpenReadableKey(application, FileMruKeyName))
        {
            if (directFileMru is not null)
            {
                yield return directFileMru;
            }
        }

        using var userMru = OpenReadableKey(application, UserMruKeyName);
        if (userMru is null)
        {
            yield break;
        }

        using (var directUserFileMru = OpenReadableKey(userMru, FileMruKeyName))
        {
            if (directUserFileMru is not null)
            {
                yield return directUserFileMru;
            }
        }

        foreach (var accountName in GetReadableSubKeyNames(userMru))
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var account = OpenReadableKey(userMru, accountName);
            using var accountFileMru = account is null ? null : OpenReadableKey(account, FileMruKeyName);
            if (accountFileMru is not null)
            {
                yield return accountFileMru;
            }
        }
    }

    internal static void ReadFileMru(RegistryKey fileMru, List<IRecentFile> items)
    {
        var keyTime = NativeRegistryMethods.GetLastWriteTimeUtc(fileMru) ?? DateTimeOffset.MinValue;
        foreach (var valueName in fileMru.GetValueNames()
                     .Where(static name => name.StartsWith("Item ", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(static name => ParseItemNumber(name)))
        {
            try
            {
                if (fileMru.GetValue(valueName) is not string value ||
                    !OfficeMruEntryParser.TryParse(value, out var entry))
                {
                    continue;
                }

                var itemNumber = ParseItemNumber(valueName);
                // The key's timestamp is only a fallback for the most recent item.
                var fallbackTime = itemNumber == 1 ? keyTime : DateTimeOffset.MinValue;
                items.Add(OfficeApplications.CreateRecentFile(
                    entry.Path,
                    entry.LastUsedTimeUtc ?? fallbackTime));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Logger.LogError($"Failed to read an Office MRU entry {ExceptionDiagnostics.Format(ex)}");
            }
        }
    }

    private static int ParseItemNumber(string valueName)
    {
        return int.TryParse(
            valueName.AsSpan("Item ".Length),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var number)
            ? number
            : int.MaxValue;
    }

    private void OnRegistryChanged(object? sender, EventArgs e)
    {
        lock (this._watchLock)
        {
            if (!this._isDisposed && !this._checkPending)
            {
                this._checkPending = this._checkTimer.Change(TimeSpan.FromMilliseconds(250), Timeout.InfiniteTimeSpan);
            }
        }
    }

    private Dictionary<string, DateTimeOffset?> ReadMruTimestamps()
    {
        var timestamps = new Dictionary<string, DateTimeOffset?>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in this.GetFileMruKeys(CancellationToken.None))
        {
            try
            {
                timestamps[key.Name] = NativeRegistryMethods.GetLastWriteTimeUtc(key);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
            {
                // A key can disappear or become unreadable during enumeration.
            }
        }
        return timestamps;
    }

    private void CheckForMruChanges()
    {
        try
        {
            lock (this._watchLock)
            {
                if (this._isDisposed)
                {
                    return;
                }
                this._checkPending = false;
                var current = this.ReadMruTimestamps();
                if (current.Count == this._mruTimestamps.Count && current.All(entry =>
                    this._mruTimestamps.TryGetValue(entry.Key, out var previous) && previous == entry.Value))
                {
                    return;
                }
                this._mruTimestamps = current;
            }
            if (!this._isDisposed)
            {
                this.Changed?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to check Office MRU changes or notify subscribers {ExceptionDiagnostics.Format(ex)}");
        }
    }

    public void Dispose()
    {
        lock (this._watchLock)
        {
            if (this._isDisposed)
            {
                return;
            }
            this._isDisposed = true;
            this._watcher.Changed -= this.OnRegistryChanged;
            this._checkTimer?.Dispose();
        }
        this._watcher.Dispose();
    }
}