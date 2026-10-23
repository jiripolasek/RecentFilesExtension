// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.Globalization;
using Microsoft.Win32;
using JPSoftworks.RecentFilesExtension.Model;

namespace JPSoftworks.RecentFilesExtension.Sources;

internal sealed partial class OfficeRecentFilesSource : IRecentFileSource
{
    private const string OfficePath = @"Software\Microsoft\Office";
    private const string FileMruKeyName = "File MRU";
    private const string UserMruKeyName = "User MRU";

    private readonly RegistryChangeWatcher[] _watchers;
    private bool _isDisposed;

    public OfficeRecentFilesSource()
    {
        this._watchers = [.. RegistryViews.Supported.Select(
            view => new RegistryChangeWatcher(OfficePath, view))];
        foreach (var watcher in this._watchers)
        {
            watcher.Changed += this.OnRegistryChanged;
        }
    }

    public event EventHandler? Changed;

    public IReadOnlyList<IRecentFile> GetRecentFiles(CancellationToken cancellationToken)
    {
        List<IRecentFile> items = [];
        foreach (var view in RegistryViews.Supported)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var hive = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, view);
            using var office = hive.OpenSubKey(OfficePath);
            if (office is null)
            {
                continue;
            }

            foreach (var versionName in office.GetSubKeyNames()
                         .Where(static name => Version.TryParse(name, out _))
                         .OrderByDescending(static name => Version.Parse(name)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var version = office.OpenSubKey(versionName);
                if (version is null)
                {
                    continue;
                }

                foreach (var applicationName in version.GetSubKeyNames())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using var application = version.OpenSubKey(applicationName);
                    if (application is not null)
                    {
                        ReadApplication(application, items, cancellationToken);
                    }
                }
            }
        }

        return items
            .OrderByDescending(static item => item.LastUsedTimeUtc)
            .DistinctBy(
                static item => RecentFilePath.GetDeduplicationKey(item.TargetPath),
                StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void ReadApplication(
        RegistryKey application,
        List<IRecentFile> items,
        CancellationToken cancellationToken)
    {
        using (var directFileMru = application.OpenSubKey(FileMruKeyName))
        {
            if (directFileMru is not null)
            {
                ReadFileMru(directFileMru, items);
            }
        }

        using var userMru = application.OpenSubKey(UserMruKeyName);
        if (userMru is null)
        {
            return;
        }

        using (var directUserFileMru = userMru.OpenSubKey(FileMruKeyName))
        {
            if (directUserFileMru is not null)
            {
                ReadFileMru(directUserFileMru, items);
            }
        }

        foreach (var accountName in userMru.GetSubKeyNames())
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var account = userMru.OpenSubKey(accountName);
            using var accountFileMru = account?.OpenSubKey(FileMruKeyName);
            if (accountFileMru is not null)
            {
                ReadFileMru(accountFileMru, items);
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
                Logger.LogError("Failed to read an Office MRU entry", ex);
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
        foreach (var watcher in this._watchers)
        {
            watcher.Changed -= this.OnRegistryChanged;
            watcher.Dispose();
        }
    }
}