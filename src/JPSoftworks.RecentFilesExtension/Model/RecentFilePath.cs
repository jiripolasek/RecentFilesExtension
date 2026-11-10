// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using JPSoftworks.RecentFilesExtension.Helpers;

namespace JPSoftworks.RecentFilesExtension.Model;

internal static class RecentFilePath
{
    public static bool TryNormalize(string? value, out string normalizedPath)
    {
        normalizedPath = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var candidate = value.Trim().Trim('"').TrimEnd('\0');
        if (Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
        {
            if (uri.IsFile)
            {
                normalizedPath = uri.LocalPath;
                return !string.IsNullOrWhiteSpace(normalizedPath);
            }

            if (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                normalizedPath = uri.AbsoluteUri;
                return true;
            }
        }

        if (!Path.IsPathRooted(candidate))
        {
            return false;
        }

        normalizedPath = candidate;
        return true;
    }

    public static bool IsFileSystemPath(string path)
    {
        return !Uri.TryCreate(path, UriKind.Absolute, out var uri) || uri.IsFile;
    }

    public static bool IsSharePointFolder(string path)
    {
        return Uri.TryCreate(path, UriKind.Absolute, out var uri) &&
               (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) &&
               IsSharePointHost(uri.Host) &&
               uri.AbsolutePath.EndsWith('/');
    }

    public static bool IsDirectory(string path)
    {
        if (!IsFileSystemPath(path))
        {
            return IsSharePointFolder(path);
        }

        // shortcut: path-only network MRUs lack folder metadata; use saved source metadata if folder classification is needed.
        return !path.IsNetworkPath() && Directory.Exists(path);
    }

    public static string GetDeduplicationKey(string path)
    {
        if (!IsFileSystemPath(path))
        {
            if (!Uri.TryCreate(path, UriKind.Absolute, out var uri))
            {
                return path;
            }

            var builder = new UriBuilder(uri)
            {
                Fragment = string.Empty,
            };
            if (IsSharePointHost(uri.Host) && !string.IsNullOrEmpty(uri.Query))
            {
                var queryParts = uri.Query[1..]
                    .Split('&', StringSplitOptions.RemoveEmptyEntries)
                    .Where(static part => !part.Equals("web=1", StringComparison.OrdinalIgnoreCase));
                builder.Query = string.Join('&', queryParts);
            }

            return builder.Uri.AbsoluteUri;
        }

        var normalizedPath = path;
        if (normalizedPath.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
        {
            normalizedPath = @"\\" + normalizedPath[8..];
        }
        else if (normalizedPath.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase))
        {
            normalizedPath = normalizedPath[4..];
        }

        try
        {
            normalizedPath = Path.GetFullPath(normalizedPath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
        }

        return Path.TrimEndingDirectorySeparator(normalizedPath);
    }

    private static bool IsSharePointHost(string host)
    {
        return host.EndsWith(".sharepoint.com", StringComparison.OrdinalIgnoreCase);
    }

    public static string GetDisplayName(string path)
    {
        if (Uri.TryCreate(path, UriKind.Absolute, out var uri) && !uri.IsFile)
        {
            var segment = uri.Segments.LastOrDefault()?.TrimEnd('/');
            return string.IsNullOrWhiteSpace(segment)
                ? uri.Host
                : Uri.UnescapeDataString(segment);
        }

        var trimmedPath = Path.TrimEndingDirectorySeparator(path);
        var fileName = Path.GetFileName(trimmedPath);
        return string.IsNullOrWhiteSpace(fileName) ? trimmedPath : fileName;
    }
}
