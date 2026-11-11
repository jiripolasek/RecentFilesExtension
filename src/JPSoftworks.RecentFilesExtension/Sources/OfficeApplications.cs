// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using Microsoft.Win32;
using JPSoftworks.RecentFilesExtension.Helpers;
using JPSoftworks.RecentFilesExtension.Model;

namespace JPSoftworks.RecentFilesExtension.Sources;

internal static class OfficeApplications
{
    private const string AppPathsRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\App Paths";

    private static readonly string[] ApplicationExecutableNames =
    [
        "EXCEL.EXE",
        "WINWORD.EXE",
        "POWERPNT.EXE",
        "MSACCESS.EXE",
        "ONENOTE.EXE",
        "OUTLOOK.EXE",
        "WINPROJ.EXE",
        "MSPUB.EXE",
        "VISIO.EXE",
    ];

    private static readonly string[] AppUserModelIdVersions = ["12", "14", "15", "16"];

    private static readonly string[] InstallationFolderVersions = ["Office16", "Office15", "Office14", "Office12"];

    private static readonly HashSet<string> DestinationFiles = BuildDestinationFileNames();

    public static IReadOnlySet<string> DestinationFileNames => DestinationFiles;

    public static bool IsDestinationFileName(string? fileName)
    {
        return !string.IsNullOrWhiteSpace(fileName) && DestinationFiles.Contains(fileName);
    }

    public static bool IsOfficeExecutable(string path)
    {
        var fileName = Path.GetFileName(path);
        return ApplicationExecutableNames.Contains(fileName, StringComparer.OrdinalIgnoreCase);
    }

    public static bool TryGetUriScheme(string applicationPath, out string scheme)
    {
        scheme = Path.GetFileName(applicationPath).ToUpperInvariant() switch
        {
            "EXCEL.EXE" => "ms-excel",
            "WINWORD.EXE" => "ms-word",
            "POWERPNT.EXE" => "ms-powerpoint",
            "MSACCESS.EXE" => "ms-access",
            "WINPROJ.EXE" => "ms-project",
            "MSPUB.EXE" => "ms-publisher",
            "VISIO.EXE" => "ms-visio",
            _ => string.Empty,
        };
        return scheme.Length > 0;
    }

    public static bool TryGetDocumentApplication(
        string target,
        out string executableName,
        out string uriScheme)
    {
        executableName = string.Empty;
        uriScheme = string.Empty;
        if (!Uri.TryCreate(target, UriKind.Absolute, out var uri) ||
            (!uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
             !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var extension = Path.GetExtension(Uri.UnescapeDataString(uri.AbsolutePath));
        (executableName, uriScheme) = extension.ToUpperInvariant() switch
        {
            ".XLS" or ".XLSX" or ".XLSM" or ".XLSB" or ".XLT" or ".XLTX" or ".XLTM" or ".XLAM" or ".CSV" or ".ODS"
                => ("EXCEL.EXE", "ms-excel"),
            ".DOC" or ".DOCX" or ".DOCM" or ".DOT" or ".DOTX" or ".DOTM" or ".RTF" or ".ODT"
                => ("WINWORD.EXE", "ms-word"),
            ".PPT" or ".PPTX" or ".PPTM" or ".PPS" or ".PPSX" or ".PPSM" or ".POT" or ".POTX" or ".POTM" or ".ODP"
                => ("POWERPNT.EXE", "ms-powerpoint"),
            ".ACCDB" or ".MDB" => ("MSACCESS.EXE", "ms-access"),
            ".MPP" or ".MPT" => ("WINPROJ.EXE", "ms-project"),
            ".PUB" => ("MSPUB.EXE", "ms-publisher"),
            ".VSD" or ".VSDX" or ".VSDM" or ".VSSX" or ".VSTX"
                => ("VISIO.EXE", "ms-visio"),
            _ => (string.Empty, string.Empty),
        };
        return executableName.Length > 0;
    }

    public static bool TryFindExecutable(string executableName, out string executablePath)
    {
        executablePath = string.Empty;
        if (!ApplicationExecutableNames.Contains(executableName, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        foreach (var hiveName in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        {
            try
            {
                using var hive = RegistryKey.OpenBaseKey(hiveName, RegistryView.Default);
                using var key = hive.OpenSubKey($@"{AppPathsRegistryPath}\{executableName}");
                if (key?.GetValue(null) is string candidate &&
                    RecentFilePath.TryNormalize(candidate, out var normalizedPath) &&
                    RecentFilePath.IsFileSystemPath(normalizedPath) &&
                    File.Exists(normalizedPath))
                {
                    executablePath = normalizedPath;
                    return true;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Logger.LogWarning($"Failed to resolve the Office application path {ExceptionDiagnostics.Format(ex)}");
            }
        }

        foreach (var candidate in GetConventionalExecutablePaths(executableName))
        {
            if (File.Exists(candidate))
            {
                executablePath = candidate;
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> GetConventionalExecutablePaths(string executableName)
    {
        var programFilesFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var specialFolder in new[]
                 {
                     Environment.SpecialFolder.ProgramFiles,
                     Environment.SpecialFolder.ProgramFilesX86,
                 })
        {
            var programFiles = Environment.GetFolderPath(specialFolder);
            if (!string.IsNullOrWhiteSpace(programFiles) && programFilesFolders.Add(programFiles))
            {
                foreach (var versionFolder in InstallationFolderVersions)
                {
                    yield return Path.Combine(
                        programFiles,
                        "Microsoft Office",
                        "root",
                        versionFolder,
                        executableName);
                    yield return Path.Combine(
                        programFiles,
                        "Microsoft Office",
                        versionFolder,
                        executableName);
                }
            }
        }
    }

    public static bool TryCreateDesktopLaunchUri(string uriScheme, string target, out string launchUri)
    {
        launchUri = string.Empty;
        if (string.IsNullOrWhiteSpace(uriScheme) ||
            !Uri.TryCreate(target, UriKind.Absolute, out var targetUri) ||
            (!targetUri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
             !targetUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        launchUri = $"{uriScheme}:ofe|u|{RecentFilePath.GetOfficeDocumentUri(targetUri).AbsoluteUri}";
        return true;
    }

    public static RecentPathFile CreateRecentFile(
        string target,
        DateTimeOffset lastUsedTimeUtc,
        string? applicationPath = null,
        Func<string, string?>? executableResolver = null)
    {
        executableResolver ??= static name => TryFindExecutable(name, out var path) ? path : null;
        var launchPath = string.Empty;
        var iconPath = string.Empty;
        if (!string.IsNullOrWhiteSpace(applicationPath) &&
            !applicationPath.IsNetworkPath() &&
            File.Exists(applicationPath) &&
            TryGetUriScheme(applicationPath, out var applicationScheme) &&
            TryCreateDesktopLaunchUri(applicationScheme, target, out launchPath))
        {
            iconPath = applicationPath;
        }
        else if (TryGetDocumentApplication(
                     target,
                     out var executableName,
                     out var documentScheme) &&
                 executableResolver(executableName) is { Length: > 0 } executablePath &&
                 TryCreateDesktopLaunchUri(documentScheme, target, out launchPath))
        {
            iconPath = executablePath;
        }

        return new(target, lastUsedTimeUtc, launchPath, iconPath);
    }

    private static HashSet<string> BuildDestinationFileNames()
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var executableName in ApplicationExecutableNames)
        {
            foreach (var version in AppUserModelIdVersions)
            {
                var appUserModelId = $"Microsoft.Office.{executableName}.{version}";
                result.Add($"{JumpListAppIdHasher.GetDestinationId(appUserModelId)}.automaticDestinations-ms");
            }
        }

        return result;
    }
}