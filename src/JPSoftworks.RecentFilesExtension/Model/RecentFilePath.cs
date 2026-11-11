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
        if (Path.IsPathFullyQualified(candidate))
        {
            normalizedPath = candidate.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            return true;
        }

        if (Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
        {
            if (uri.IsFile && Path.IsPathFullyQualified(uri.LocalPath))
            {
                normalizedPath = uri.LocalPath;
                return true;
            }

            if (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                normalizedPath = uri.AbsoluteUri;
                return true;
            }
        }

        return false;
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
        if (Uri.TryCreate(path, UriKind.Absolute, out var uri) && !uri.IsFile)
        {
            if (IsSharePointHost(uri.Host) || IsOneDriveHost(uri.Host))
            {
                return GetOfficeDocumentUri(uri).GetComponents(UriComponents.AbsoluteUri, UriFormat.SafeUnescaped).ToUpperInvariant();
            }
            return uri.GetLeftPart(UriPartial.Query);
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

        return Path.TrimEndingDirectorySeparator(normalizedPath).ToUpperInvariant();
    }

    public static Uri GetOfficeDocumentUri(Uri uri)
    {
        var builder = new UriBuilder(uri) { Fragment = string.Empty };
        if (IsSharePointHost(uri.Host) && !string.IsNullOrEmpty(uri.Query))
        {
            var queryParts = uri.Query[1..]
                .Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Where(static part => !part.Equals("web=1", StringComparison.OrdinalIgnoreCase));
            builder.Query = string.Join('&', queryParts);
        }
        return builder.Uri;
    }

    private static bool IsSharePointHost(string host)
    {
        return host.EndsWith(".sharepoint.com", StringComparison.OrdinalIgnoreCase) ||
               host.EndsWith(".sharepoint.us", StringComparison.OrdinalIgnoreCase) ||
               host.EndsWith(".sharepoint-mil.us", StringComparison.OrdinalIgnoreCase) ||
               host.EndsWith(".sharepoint.cn", StringComparison.OrdinalIgnoreCase) ||
               host.EndsWith(".sharepoint.de", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsOneDriveHost(string host)
    {
        return host is "onedrive.live.com" or "onedrive.com" or "www.onedrive.com" or "1drv.ms" ||
               host.EndsWith(".onedrive.live.com", StringComparison.OrdinalIgnoreCase) ||
               host.EndsWith(".docs.live.net", StringComparison.OrdinalIgnoreCase);
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

    public static string GetFileName(string path)
    {
        return Path.GetFileName(
            Uri.TryCreate(path, UriKind.Absolute, out var uri) && !uri.IsFile
                ? Uri.UnescapeDataString(uri.AbsolutePath)
                : path);
    }
}