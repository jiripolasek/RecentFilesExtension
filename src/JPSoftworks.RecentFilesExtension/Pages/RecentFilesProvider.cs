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
                Logger.LogError($"Failed to add link {recentFileInfo.FullName}", ex);
            }
        }

        return items;
    }
}