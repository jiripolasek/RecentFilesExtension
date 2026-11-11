// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.Security;
using Microsoft.Win32;
using JPSoftworks.RecentFilesExtension.Model;

using JPSoftworks.RecentFilesExtension.Helpers;

using static JPSoftworks.RecentFilesExtension.Sources.RegistryReadHelper;

namespace JPSoftworks.RecentFilesExtension.Sources;

internal sealed partial class OpenSaveMruSource : IRecentFileSource
{
    private const string CommonDialogPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\ComDlg32";
    private const string LegacyMruKeyName = "OpenSaveMRU";
    private const string PidlMruKeyName = "OpenSavePidlMRU";

    private readonly string _commonDialogPath;
    private readonly RegistryChangeWatcher _watcher;
    private bool _isDisposed;

    public OpenSaveMruSource()
        : this(CommonDialogPath)
    {
    }

    internal OpenSaveMruSource(string commonDialogPath)
    {
        this._commonDialogPath = commonDialogPath;
        this._watcher = new(commonDialogPath, RegistryView.Default);
        this._watcher.Changed += this.OnRegistryChanged;
    }

    public event EventHandler? Changed;

    public IReadOnlyList<IRecentFile> GetRecentFiles(CancellationToken cancellationToken)
    {
        List<IRecentFile> items = [];
        cancellationToken.ThrowIfCancellationRequested();
        using var hive = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default);
        using var commonDialog = OpenReadableKey(hive, this._commonDialogPath);
        if (commonDialog is not null)
        {
            ReadMruTree(commonDialog, LegacyMruKeyName, isPidl: false, items, cancellationToken);
            ReadMruTree(commonDialog, PidlMruKeyName, isPidl: true, items, cancellationToken);
        }

        return items
            .OrderByDescending(static item => item.LastUsedTimeUtc)
            .DistinctBy(
                static item => RecentFilePath.GetDeduplicationKey(item.TargetPath),
                StringComparer.Ordinal)
            .ToArray();
    }

    private static void ReadMruTree(
        RegistryKey commonDialog,
        string rootName,
        bool isPidl,
        List<IRecentFile> items,
        CancellationToken cancellationToken)
    {
        using var root = OpenReadableKey(commonDialog, rootName);
        if (root is null)
        {
            return;
        }

        ReadMruKey(root, isPidl, items);
        foreach (var subKeyName in GetReadableSubKeyNames(root))
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var subKey = OpenReadableKey(root, subKeyName);
            if (subKey is not null)
            {
                ReadMruKey(subKey, isPidl, items);
            }
        }
    }

    internal static void ReadMruKey(RegistryKey key, bool isPidl, List<IRecentFile> items)
    {
        DateTimeOffset keyTime;
        IReadOnlyList<string> orderedValueNames;
        try
        {
            keyTime = NativeRegistryMethods.GetLastWriteTimeUtc(key) ?? DateTimeOffset.MinValue;
            orderedValueNames = MruListParser.GetOrderedValueNames(key);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            return;
        }
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
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
            {
                Logger.LogError($"Failed to read an Open/Save MRU entry {ExceptionDiagnostics.Format(ex)}");
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
        this._watcher.Changed -= this.OnRegistryChanged;
        this._watcher.Dispose();
    }
}