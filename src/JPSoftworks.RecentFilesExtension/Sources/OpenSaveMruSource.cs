// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using Microsoft.Win32;
using JPSoftworks.RecentFilesExtension.Model;

namespace JPSoftworks.RecentFilesExtension.Sources;

internal sealed partial class OpenSaveMruSource : IRecentFileSource
{
    private const string CommonDialogPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\ComDlg32";
    private const string LegacyMruKeyName = "OpenSaveMRU";
    private const string PidlMruKeyName = "OpenSavePidlMRU";

    private readonly RegistryChangeWatcher[] _watchers;
    private bool _isDisposed;

    public OpenSaveMruSource()
    {
        this._watchers = [.. RegistryViews.Supported.Select(
            view => new RegistryChangeWatcher(CommonDialogPath, view))];
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
            using var commonDialog = hive.OpenSubKey(CommonDialogPath);
            if (commonDialog is null)
            {
                continue;
            }

            ReadMruTree(commonDialog, LegacyMruKeyName, isPidl: false, items, cancellationToken);
            ReadMruTree(commonDialog, PidlMruKeyName, isPidl: true, items, cancellationToken);
        }

        return items
            .OrderByDescending(static item => item.LastUsedTimeUtc)
            .DistinctBy(
                static item => RecentFilePath.GetDeduplicationKey(item.TargetPath),
                StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void ReadMruTree(
        RegistryKey commonDialog,
        string rootName,
        bool isPidl,
        List<IRecentFile> items,
        CancellationToken cancellationToken)
    {
        using var root = commonDialog.OpenSubKey(rootName);
        if (root is null)
        {
            return;
        }

        ReadMruKey(root, isPidl, items);
        foreach (var subKeyName in root.GetSubKeyNames())
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var subKey = root.OpenSubKey(subKeyName);
            if (subKey is not null)
            {
                ReadMruKey(subKey, isPidl, items);
            }
        }
    }

    internal static void ReadMruKey(RegistryKey key, bool isPidl, List<IRecentFile> items)
    {
        var keyTime = NativeRegistryMethods.GetLastWriteTimeUtc(key) ?? DateTimeOffset.MinValue;
        var orderedValueNames = MruListParser.GetOrderedValueNames(key);
        for (var rank = 0; rank < orderedValueNames.Count; rank++)
        {
            try
            {
                var value = key.GetValue(orderedValueNames[rank]);
                string path;
                if (isPidl && value is byte[] itemIdList)
                {
                    if (!ShellItemIdListPathResolver.TryResolve(itemIdList, out path))
                    {
                        continue;
                    }
                }
                else if (value is string stringValue)
                {
                    if (!RecentFilePath.TryNormalize(stringValue, out path))
                    {
                        continue;
                    }
                }
                else
                {
                    continue;
                }

                // A key update dates only the newest MRU entry, not the entire history.
                var timestamp = rank == 0 ? keyTime : DateTimeOffset.MinValue;
                items.Add(OfficeApplications.CreateRecentFile(path, timestamp));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Logger.LogError("Failed to read an Open/Save MRU entry", ex);
            }
        }
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