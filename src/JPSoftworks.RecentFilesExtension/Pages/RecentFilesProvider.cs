// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

using JPSoftworks.RecentFilesExtension.Helpers;
using JPSoftworks.RecentFilesExtension.Model;

namespace JPSoftworks.RecentFilesExtension.Pages;

internal static class RecentFilesProvider
{
    private const int MaxRecentFileCount = 100;

    internal static IList<RecentShortcutFile> GetRecentFiles()
    {
        var recentFiles = Environment.GetFolderPath(Environment.SpecialFolder.Recent);
        if (string.IsNullOrWhiteSpace(recentFiles))
        {
            return [];
        }

        var recentFilesShortcuts = new DirectoryInfo(recentFiles)
            .GetFiles("*.lnk", SearchOption.TopDirectoryOnly)
            .OrderByDescending(static t => t.LastWriteTimeUtc);

        var items = new List<RecentShortcutFile>();
        var addedTargetPaths = new HashSet<string>();

        var counter = 0;
        foreach (var recentFileInfo in recentFilesShortcuts)
        {
            try
            {
                var shellLink = new ShellLinkHelper();
                shellLink.RetrieveTargetPath(new string(recentFileInfo.FullName));

                if (string.IsNullOrWhiteSpace(shellLink.TargetPath))
                    continue;
                var isNetworkPath = shellLink.TargetPath.StartsWith(@"\\", StringComparison.InvariantCultureIgnoreCase) || shellLink.TargetPath.StartsWith(@"\\?\UNC", StringComparison.InvariantCultureIgnoreCase);
                if (!isNetworkPath && !File.Exists(shellLink.TargetPath) && !Directory.Exists(shellLink.TargetPath))
                    continue;
                if (addedTargetPaths.Contains(shellLink.TargetPath))
                    continue;

                addedTargetPaths.Add(isNetworkPath ? shellLink.TargetPath : shellLink.TargetPath.ToLowerInvariant());

                // a recent file can be accompanied by a recent parent folder; let's prefer the file over the folder and not add the folder
                // the folder can (and usually is) preceding the file in the recent files list, so we can just remove it from the list 

                // check the last item in the list, and if it is a parent folder of the current item, remove it from the list
                if (items.Count > 0 && items[^1].TargetPath.Equals(Path.GetDirectoryName(shellLink.TargetPath), StringComparison.OrdinalIgnoreCase))
                {
                    items.RemoveAt(items.Count - 1);
                }

                // add the current item to the list (if it is not a parent folder of the last item in the list)
                if (items.Count > 0 && items[^1].TargetPath.Equals(shellLink.TargetPath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                items.Add(new RecentShortcutFile(
                    recentFileInfo.FullName,
                    shellLink.DisplayName,
                    shellLink.TargetPath));

                if (++counter > MaxRecentFileCount)
                {
                    break;
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to add shortcut {recentFileInfo.FullName}", ex);
            }
        }

        return items;
    }
}