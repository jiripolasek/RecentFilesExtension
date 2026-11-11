// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.Diagnostics;
using JPSoftworks.RecentFilesExtension.Helpers;
using JPSoftworks.RecentFilesExtension.Model;
using JPSoftworks.RecentFilesExtension.Pages;

namespace JPSoftworks.RecentFilesExtension.Sources;

internal sealed partial class RecentItemsFolderSource : IRecentFileSource
{
    private const int MaxRecentFileCount = 500;
    private const string ShortcutFileSearchPattern = "*.lnk";

    private readonly CachedShellLinksHelper _shellLinksHelper = new();
    private readonly RecentFolderWatcher _watcher;
    private readonly string _folderPath;

    public RecentItemsFolderSource()
        : this(Environment.GetFolderPath(Environment.SpecialFolder.Recent))
    {
    }

    internal RecentItemsFolderSource(string folderPath)
    {
        this._folderPath = folderPath;
        this._watcher = new(folderPath, "Recent folder", () => this.Changed?.Invoke(this, EventArgs.Empty),
            ShortcutFileSearchPattern, fileChanges: NotifyFilters.FileName | NotifyFilters.LastWrite);
    }

    public event EventHandler? Changed;

    public IReadOnlyList<IRecentFile> GetRecentFiles(CancellationToken cancellationToken)
    {
        if (!this._watcher.Refresh())
        {
            this._shellLinksHelper.PruneCache([]);
            return Array.Empty<IRecentFile>();
        }

        var stopwatch = Stopwatch.StartNew();
        var files = new DirectoryInfo(this._folderPath).GetFiles(ShortcutFileSearchPattern, SearchOption.TopDirectoryOnly);
        this._shellLinksHelper.PruneCache(files);
        var shortcuts = files.OrderByDescending(static file => file.LastWriteTimeUtc);

        var items = new List<IRecentFile>();
        var addedTargetPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var fileInfo in shortcuts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (items.Count >= MaxRecentFileCount)
            {
                break;
            }

            try
            {
                if (!this._shellLinksHelper.TryGetShellLink(fileInfo, out var shortcutFile) ||
                    !addedTargetPaths.Add(shortcutFile.TargetPath))
                {
                    continue;
                }

                if (items.Count > 0 && items[^1].TargetPath.Equals(
                        Path.GetDirectoryName(shortcutFile.TargetPath),
                        StringComparison.OrdinalIgnoreCase))
                {
                    items.RemoveAt(items.Count - 1);
                }

                items.Add(shortcutFile);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to process a recent shortcut {ExceptionDiagnostics.Format(ex)}");
            }
        }

        Logger.LogDebug($"Loaded {items.Count} recent-folder items in {stopwatch.ElapsedMilliseconds}ms");
        return items;
    }

    public void Dispose() => this._watcher.Dispose();
}