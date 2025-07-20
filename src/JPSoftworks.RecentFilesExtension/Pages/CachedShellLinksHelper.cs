// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using JPSoftworks.RecentFilesExtension.Helpers;
using JPSoftworks.RecentFilesExtension.Model;

namespace JPSoftworks.RecentFilesExtension.Pages;

/// <summary>
/// Cache results of shell links to avoid reading them multiple times.
/// </summary>
internal class CachedShellLinksHelper
{
    // cache using a file name and check last write time
    private readonly Dictionary<string, ShellLinkCacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);
    record struct ShellLinkCacheEntry(RecentShortcutFile? ShortcutFile);

    /// <summary>
    /// Attempts to retrieve a shell link from the specified shortcut file.
    /// </summary>
    /// <remarks>This method checks the cache for an existing entry of the shortcut file. If the cache entry
    /// is valid and up-to-date, it returns the cached shortcut file. If the shortcut file does not exist or the target
    /// path is invalid, the method returns <see langword="false"/> and sets <paramref name="shortcutFile"/> to <see
    /// langword="null"/>.</remarks>
    /// <param name="shortcutFileInfo">The <see cref="FileInfo"/> object representing the shortcut file to be evaluated.</param>
    /// <param name="shortcutFile">When this method returns, contains the <see cref="RecentShortcutFile"/> object if the operation is successful;
    /// otherwise, <see langword="null"/>. This parameter is passed uninitialized.</param>
    /// <returns><see langword="true"/> if the shell link is successfully retrieved and valid; otherwise, <see langword="false"/>.</returns>
    public bool TryGetShellLink(FileInfo shortcutFileInfo, [NotNullWhen(true)] out RecentShortcutFile? shortcutFile)
    {
        if (this._cache.TryGetValue(shortcutFileInfo.FullName, out var entry))
        {
            if (entry.ShortcutFile == null)
            {
                shortcutFile = null;
                return false;
            }

            if (shortcutFileInfo.Exists && shortcutFileInfo.LastWriteTimeUtc == entry.ShortcutFile.FileInfo.LastWriteTimeUtc)
            {
                shortcutFile = entry.ShortcutFile;
                return true;
            }
            else
            {
                this._cache.Remove(shortcutFileInfo.FullName);
            }
        }

        shortcutFile = null;
        if (!shortcutFileInfo.Exists)
        {
            return false;
        }

        // build shell link helper
        var shellLink = new ShellLinkHelper();
        shellLink.RetrieveTargetPath(shortcutFileInfo.FullName);

        var target = shellLink.TargetPath;
        if (string.IsNullOrWhiteSpace(target))
        {
            this._cache[shortcutFileInfo.FullName] = new(null);
            return false;
        }

        var isNetwork = target.IsNetworkPath();
        if (!isNetwork && !File.Exists(target) && !Directory.Exists(target))
        {
            this._cache[shortcutFileInfo.FullName] = new(null);
            return false;
        }

        try
        {
            shortcutFile = new(shortcutFileInfo, shellLink.DisplayName, shellLink.TargetPath);
            this._cache[shortcutFileInfo.FullName] = new(shortcutFile);
            return true;
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to retrieve shell link from {shortcutFileInfo.FullName}", ex);
            this._cache[shortcutFileInfo.FullName] = new(null);
            shortcutFile = null;
            return false;
        }
    }
}