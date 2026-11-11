// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.Diagnostics;
using System.Runtime.InteropServices;
using JPSoftworks.RecentFilesExtension.Model;
using JPSoftworks.RecentFilesExtension.Pages;

using JPSoftworks.RecentFilesExtension.Helpers;

namespace JPSoftworks.RecentFilesExtension.Sources;

internal sealed partial class OfficeRecentFolderSource : IRecentFileSource
{
    private const int MaximumInternetShortcutSize = 64 * 1024;

    private readonly CachedShellLinksHelper _shellLinksHelper = new();
    private readonly Dictionary<string, (DateTime LastWriteTime, string Target)> _internetShortcuts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> _failedEntries = new(StringComparer.OrdinalIgnoreCase);
    private readonly RecentFolderWatcher _watcher;
    private readonly string _folderPath;

    public OfficeRecentFolderSource()
        : this(GetOfficeRecentFolderPath())
    {
    }

    internal OfficeRecentFolderSource(string folderPath)
    {
        this._folderPath = folderPath;
        this._watcher = new(folderPath, "Office Recent folder", () => this.Changed?.Invoke(this, EventArgs.Empty),
            acceptName: IsSupportedFileName);
    }

    public event EventHandler? Changed;

    public IReadOnlyList<IRecentFile> GetRecentFiles(CancellationToken cancellationToken)
    {
        if (!this._watcher.Refresh())
        {
            this._shellLinksHelper.PruneCache([]);
            this._internetShortcuts.Clear();
            this._failedEntries.Clear();
            return Array.Empty<IRecentFile>();
        }

        var stopwatch = Stopwatch.StartNew();
        var items = new List<IRecentFile>();
        var files = new DirectoryInfo(this._folderPath).GetFiles("*", SearchOption.TopDirectoryOnly);
        this._shellLinksHelper.PruneCache(files);
        var cachedPaths = this._internetShortcuts.Keys.Concat(this._failedEntries.Keys);
        foreach (var path in cachedPaths.Except(files.Select(static file => file.FullName), StringComparer.OrdinalIgnoreCase).ToArray())
        {
            this._internetShortcuts.Remove(path);
            this._failedEntries.Remove(path);
        }
        foreach (var file in files.OrderByDescending(static item => item.LastWriteTimeUtc))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (file.Extension.Equals(".url", StringComparison.OrdinalIgnoreCase) &&
                    this.TryReadInternetShortcut(file, out var target))
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
                this._failedEntries.Remove(file.FullName);
            }
            catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException or InvalidDataException)
            {
                if (!this._failedEntries.TryGetValue(file.FullName, out var timestamp) || timestamp != file.LastWriteTimeUtc)
                {
                    this._failedEntries[file.FullName] = file.LastWriteTimeUtc;
                    Logger.LogWarning($"Failed to read an Office Recent entry {ExceptionDiagnostics.Format(ex)}");
                }
            }
        }

        var result = items
            .OrderByDescending(static item => item.LastUsedTimeUtc)
            .DistinctBy(
                static item => RecentFilePath.GetDeduplicationKey(item.TargetPath),
                StringComparer.Ordinal)
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

    private bool TryReadInternetShortcut(FileInfo file, out string target)
    {
        target = string.Empty;
        if (!file.Exists || file.Length is <= 0 or > MaximumInternetShortcutSize)
        {
            this._internetShortcuts.Remove(file.FullName);
            return false;
        }

        var timestamp = file.LastWriteTimeUtc;
        if (this._internetShortcuts.TryGetValue(file.FullName, out var cached) && cached.LastWriteTime == timestamp)
        {
            target = cached.Target;
            return target.Length > 0;
        }

        var parsed = TryParseInternetShortcut(File.ReadLines(file.FullName), out target);
        this._internetShortcuts[file.FullName] = (timestamp, target);
        return parsed;
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

    public void Dispose() => this._watcher.Dispose();
}