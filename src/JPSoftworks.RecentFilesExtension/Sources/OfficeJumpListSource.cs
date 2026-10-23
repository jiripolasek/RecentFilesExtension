// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using JPSoftworks.RecentFilesExtension.Helpers;
using JPSoftworks.RecentFilesExtension.Model;

namespace JPSoftworks.RecentFilesExtension.Sources;

internal sealed partial class OfficeJumpListSource : IRecentFileSource
{
    private const string DestinationFileExtension = ".automaticDestinations-ms";
    private const int MaximumDestListSize = 16 * 1024 * 1024;
    private const int MaximumShellLinkSize = 4 * 1024 * 1024;

    private readonly FileSystemWatcher? _watcher;
    private readonly string _folderPath;
    private bool _isDisposed;

    public OfficeJumpListSource()
    {
        var recentFolder = Environment.GetFolderPath(Environment.SpecialFolder.Recent);
        this._folderPath = string.IsNullOrWhiteSpace(recentFolder)
            ? string.Empty
            : Path.Combine(recentFolder, "AutomaticDestinations");
        if (string.IsNullOrWhiteSpace(this._folderPath) || !Directory.Exists(this._folderPath))
        {
            return;
        }

        this._watcher = new(this._folderPath, $"*{DestinationFileExtension}")
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
        foreach (var fileName in OfficeApplications.DestinationFileNames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = Path.Combine(this._folderPath, fileName);
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                ReadDestinationFile(path, items, cancellationToken);
            }
            catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException or InvalidDataException)
            {
                Logger.LogError($"Failed to read Office Jump List {fileName}", ex);
            }
        }

        var result = items
            .OrderByDescending(static item => item.LastUsedTimeUtc)
            .DistinctBy(
                static item => RecentFilePath.GetDeduplicationKey(item.TargetPath),
                StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Logger.LogDebug($"Loaded {result.Length} Office Jump List items in {stopwatch.ElapsedMilliseconds}ms");
        return result;
    }

    private static void ReadDestinationFile(
        string path,
        List<IRecentFile> items,
        CancellationToken cancellationToken)
    {
        using var storage = StructuredStorageFile.Open(path);
        var destListBytes = storage.ReadStream("DestList", MaximumDestListSize);
        if (!DestListParser.TryParse(destListBytes, out var destList))
        {
            throw new InvalidDataException("The Jump List DestList stream has an unsupported or malformed layout.");
        }

        foreach (var entry in destList.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var streamName = entry.StreamNumber.ToString("x", CultureInfo.InvariantCulture);
                var targetData = storage.UseStream(
                    streamName,
                    MaximumShellLinkSize,
                    stream =>
                    {
                        using var shellLink = new ShellLinkHelper();
                        return shellLink.LoadTargetData(stream);
                    });
                if (OfficeJumpListTarget.TryGetTarget(targetData, out var target))
                {
                    OfficeJumpListTarget.TryGetApplicationPath(targetData, out var applicationPath);
                    items.Add(OfficeApplications.CreateRecentFile(
                        target,
                        entry.LastUsedTimeUtc,
                        applicationPath));
                }
            }
            catch (Exception ex) when (ex is COMException or IOException or InvalidDataException)
            {
                Logger.LogWarning($"Failed to read an Office Jump List entry: {ex.Message}");
            }
        }
    }

    private void OnFileSystemChanged(object sender, FileSystemEventArgs e)
    {
        if (this._isDisposed ||
            (!OfficeApplications.IsDestinationFileName(e.Name) &&
             (e is not RenamedEventArgs renamed ||
              !OfficeApplications.IsDestinationFileName(renamed.OldName))))
        {
            return;
        }

        this.Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        if (!this._isDisposed)
        {
            Logger.LogError("Office Jump List watcher reported an error", e.GetException());
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

internal static class OfficeJumpListTarget
{
    public static bool TryGetTarget(ShellLinkTargetData data, out string target)
    {
        if (TryNormalizeCandidate(data.Arguments, out target) ||
            TryNormalizeCandidate(data.Description, out target) ||
            TryNormalizeCandidate(data.TargetPath, out target))
        {
            return true;
        }

        target = string.Empty;
        return false;
    }

    public static bool TryGetApplicationPath(ShellLinkTargetData data, out string applicationPath)
    {
        if (RecentFilePath.TryNormalize(data.TargetPath, out applicationPath) &&
            RecentFilePath.IsFileSystemPath(applicationPath) &&
            OfficeApplications.IsOfficeExecutable(applicationPath))
        {
            return true;
        }

        applicationPath = string.Empty;
        return false;
    }

    public static bool TryCreateDesktopLaunchUri(
        string applicationPath,
        string target,
        out string launchUri)
    {
        launchUri = string.Empty;
        return OfficeApplications.TryGetUriScheme(applicationPath, out var scheme) &&
               OfficeApplications.TryCreateDesktopLaunchUri(scheme, target, out launchUri);
    }

    private static bool TryNormalizeCandidate(string? candidate, out string target)
    {
        target = string.Empty;
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        if (TryNormalizeTarget(candidate, out target))
        {
            return true;
        }

        var remaining = candidate.AsSpan();
        while (!remaining.IsEmpty)
        {
            var openingQuote = remaining.IndexOf('"');
            if (openingQuote < 0)
            {
                break;
            }

            remaining = remaining[(openingQuote + 1)..];
            var closingQuote = remaining.IndexOf('"');
            if (closingQuote < 0)
            {
                break;
            }

            if (TryNormalizeTarget(remaining[..closingQuote].ToString(), out target))
            {
                return true;
            }

            remaining = remaining[(closingQuote + 1)..];
        }

        var urlStart = candidate.IndexOf("https://", StringComparison.OrdinalIgnoreCase);
        if (urlStart < 0)
        {
            urlStart = candidate.IndexOf("http://", StringComparison.OrdinalIgnoreCase);
        }

        return urlStart >= 0 && TryNormalizeTarget(candidate[urlStart..].Trim().Trim('"'), out target);
    }

    private static bool TryNormalizeTarget(string candidate, out string target)
    {
        var trimmedCandidate = candidate.Trim();
        if (trimmedCandidate.Contains('"') ||
            trimmedCandidate.StartsWith('/') ||
            trimmedCandidate.StartsWith('-') ||
            !RecentFilePath.TryNormalize(trimmedCandidate, out target) ||
            (RecentFilePath.IsFileSystemPath(target) && OfficeApplications.IsOfficeExecutable(target)))
        {
            target = string.Empty;
            return false;
        }

        return true;
    }
}