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
internal class CachedShellLinksHelper(TimeProvider? timeProvider = null)
{
    private readonly Dictionary<string, (DateTime LastWriteTime, RecentShortcutFile? File, DateTimeOffset RetryAfter)> _cache = new(StringComparer.OrdinalIgnoreCase);

    public void PruneCache(IEnumerable<FileInfo> files)
    {
        var paths = files.Select(static file => file.FullName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var path in this._cache.Keys.Except(paths, StringComparer.OrdinalIgnoreCase).ToArray())
        {
            this._cache.Remove(path);
        }
    }

    /// <summary>
    /// Attempts to retrieve a shell link from the specified shortcut file.
    /// </summary>
    /// <remarks>The caller supplies fresh metadata from the current directory listing. This method checks the cache for an existing entry of the shortcut file. If the cache entry
    /// is valid and up-to-date, it returns the cached shortcut file. If the shortcut file does not exist or the target
    /// path is invalid, the method returns <see langword="false"/> and sets <paramref name="shortcutFile"/> to <see
    /// langword="null"/>. Failed reads are retried after a timestamp change or five minutes without repeating the warning.</remarks>
    /// <param name="shortcutFileInfo">The <see cref="FileInfo"/> object representing the shortcut file to be evaluated.</param>
    /// <param name="shortcutFile">When this method returns, contains the <see cref="RecentShortcutFile"/> object if the operation is successful;
    /// otherwise, <see langword="null"/>. This parameter is passed uninitialized.</param>
    /// <returns><see langword="true"/> if the shell link is successfully retrieved and valid; otherwise, <see langword="false"/>.</returns>
    public bool TryGetShellLink(FileInfo shortcutFileInfo, [NotNullWhen(true)] out RecentShortcutFile? shortcutFile)
    {
        shortcutFile = null;
        if (!shortcutFileInfo.Exists)
        {
            this._cache.Remove(shortcutFileInfo.FullName);
            return false;
        }

        var timestamp = shortcutFileInfo.LastWriteTimeUtc;
        var now = (timeProvider ?? TimeProvider.System).GetUtcNow();
        var sameVersion = this._cache.TryGetValue(shortcutFileInfo.FullName, out var cached) && cached.LastWriteTime == timestamp;
        if (sameVersion && (cached.File is not null || cached.RetryAfter > now))
        {
            shortcutFile = cached.File;
            return shortcutFile is not null;
        }

        try
        {
            using var shellLink = new ShellLinkHelper();
            var target = shellLink.LoadTargetPath(shortcutFileInfo.FullName);
            if (string.IsNullOrWhiteSpace(target) ||
                !target.IsNetworkPath() && !File.Exists(target) && !Directory.Exists(target))
            {
                this._cache.Remove(shortcutFileInfo.FullName);
                return false;
            }

            var displayName = shellLink.RetrieveDisplayName(shortcutFileInfo.FullName);
            shortcutFile = new(shortcutFileInfo, displayName, target, shellLink.IsDirectory);
            this._cache[shortcutFileInfo.FullName] = (timestamp, shortcutFile, DateTimeOffset.MaxValue);
            return true;
        }
        catch (Exception ex)
        {
            this._cache[shortcutFileInfo.FullName] = (timestamp, null, now.AddMinutes(5));
            if (!sameVersion)
            {
                Logger.LogWarning($"Failed to retrieve a shell link {ExceptionDiagnostics.Format(ex)}");
            }
            return false;
        }
    }
}