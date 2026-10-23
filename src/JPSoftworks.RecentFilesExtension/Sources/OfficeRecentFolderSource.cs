// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.Diagnostics;
using JPSoftworks.RecentFilesExtension.Model;
using JPSoftworks.RecentFilesExtension.Pages;

namespace JPSoftworks.RecentFilesExtension.Sources;

internal sealed partial class OfficeRecentFolderSource : IRecentFileSource
{
    private const int MaximumInternetShortcutSize = 64 * 1024;

    private readonly CachedShellLinksHelper _shellLinksHelper = new();
    private readonly FileSystemWatcher? _watcher;
    private readonly string _folderPath;
    private bool _isDisposed;

    public OfficeRecentFolderSource()
        : this(GetOfficeRecentFolderPath())
    {
    }

    internal OfficeRecentFolderSource(string folderPath)
    {
        this._folderPath = folderPath;
        if (string.IsNullOrWhiteSpace(this._folderPath) || !Directory.Exists(this._folderPath))
        {
            return;
        }

        this._watcher = new(this._folderPath)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            IncludeSubdirectories = false,
            EnableRaisingEvents = true,
        };
        this._watcher.Created += this.OnFileSystemChanged;
        this._watcher.Deleted += this.OnFileSystemChanged;
        this._watcher.Renamed += this.OnFileSystemChanged;
        this._watcher.Changed += this.OnFileSystemChanged;
        this._watcher.Error += this.OnWatcherError;
    }

    public event EventHandler? Changed;

    public IReadOnlyList<IRecentFile> GetRecentFiles(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(this._folderPath) || !Directory.Exists(this._folderPath))
        {
            return Array.Empty<IRecentFile>();
        }

        var stopwatch = Stopwatch.StartNew();
        var items = new List<IRecentFile>();
        foreach (var file in new DirectoryInfo(this._folderPath)
                     .GetFiles("*", SearchOption.TopDirectoryOnly)
                     .OrderByDescending(static item => item.LastWriteTimeUtc))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (file.Extension.Equals(".url", StringComparison.OrdinalIgnoreCase) &&
                    TryReadInternetShortcut(file, out var target))
                {
                    items.Add(OfficeApplications.CreateRecentFile(
                        target,
                        new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero)));
                }
                else if (file.Extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase) &&
                         this._shellLinksHelper.TryGetShellLink(file, out var shortcut))
                {
                    items.Add(shortcut);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                Logger.LogWarning($"Failed to read an Office Recent entry: {ex.Message}");
            }
        }

        var result = items
            .OrderByDescending(static item => item.LastUsedTimeUtc)
            .DistinctBy(
                static item => RecentFilePath.GetDeduplicationKey(item.TargetPath),
                StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Logger.LogDebug($"Loaded {result.Length} Office Recent folder items in {stopwatch.ElapsedMilliseconds}ms");
        return result;
    }

    internal static bool TryParseInternetShortcut(IEnumerable<string> lines, out string target)
    {
        target = string.Empty;
        var isInternetShortcutSection = false;
        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();
            if (trimmedLine.StartsWith('[') && trimmedLine.EndsWith(']'))
            {
                isInternetShortcutSection = trimmedLine.Equals(
                    "[InternetShortcut]",
                    StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (isInternetShortcutSection &&
                trimmedLine.StartsWith("URL=", StringComparison.OrdinalIgnoreCase) &&
                RecentFilePath.TryNormalize(trimmedLine["URL=".Length..], out target))
            {
                return true;
            }
        }

        target = string.Empty;
        return false;
    }

    private static bool TryReadInternetShortcut(FileInfo file, out string target)
    {
        target = string.Empty;
        file.Refresh();
        if (!file.Exists || file.Length is <= 0 or > MaximumInternetShortcutSize)
        {
            return false;
        }

        return TryParseInternetShortcut(File.ReadLines(file.FullName), out target);
    }

    private static string GetOfficeRecentFolderPath()
    {
        var applicationData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return string.IsNullOrWhiteSpace(applicationData)
            ? string.Empty
            : Path.Combine(applicationData, "Microsoft", "Office", "Recent");
    }

    private static bool IsSupportedFileName(string? fileName)
    {
        var extension = Path.GetExtension(fileName);
        return string.Equals(extension, ".url", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(extension, ".lnk", StringComparison.OrdinalIgnoreCase);
    }

    private void OnFileSystemChanged(object sender, FileSystemEventArgs e)
    {
        if (this._isDisposed ||
            (!IsSupportedFileName(e.Name) &&
             (e is not RenamedEventArgs renamed || !IsSupportedFileName(renamed.OldName))))
        {
            return;
        }

        this.Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        if (!this._isDisposed)
        {
            Logger.LogError("Office Recent folder watcher reported an error", e.GetException());
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
        if (this._watcher is not null)
        {
            this._watcher.Created -= this.OnFileSystemChanged;
            this._watcher.Deleted -= this.OnFileSystemChanged;
            this._watcher.Renamed -= this.OnFileSystemChanged;
            this._watcher.Changed -= this.OnFileSystemChanged;
            this._watcher.Error -= this.OnWatcherError;
            this._watcher.Dispose();
        }
    }
}