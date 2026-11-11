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

    private readonly Dictionary<string, CachedDestination> _destinations = new(StringComparer.OrdinalIgnoreCase);
    private readonly RecentFolderWatcher _watcher;
    private readonly string _folderPath;
    private readonly TimeProvider _timeProvider;

    public OfficeJumpListSource()
        : this(GetAutomaticDestinationsPath())
    {
    }

    internal OfficeJumpListSource(string folderPath, TimeProvider? timeProvider = null)
    {
        this._folderPath = folderPath;
        this._timeProvider = timeProvider ?? TimeProvider.System;
        this._watcher = new(folderPath, "Office Jump List", () => this.Changed?.Invoke(this, EventArgs.Empty),
            $"*{DestinationFileExtension}", OfficeApplications.IsDestinationFileName, timeProvider: this._timeProvider);
    }

    public event EventHandler? Changed;

    public IReadOnlyList<IRecentFile> GetRecentFiles(CancellationToken cancellationToken)
    {
        if (!this._watcher.Refresh())
        {
            this._destinations.Clear();
            return Array.Empty<IRecentFile>();
        }

        var stopwatch = Stopwatch.StartNew();
        var items = new List<IRecentFile>();
        foreach (var fileName in OfficeApplications.DestinationFileNames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = Path.Combine(this._folderPath, fileName);
            var file = new FileInfo(path);
            if (!file.Exists)
            {
                this._destinations.Remove(path);
                continue;
            }
            var timestamp = file.LastWriteTimeUtc;
            var sameVersion = this._destinations.TryGetValue(path, out var cached) && cached.Timestamp == timestamp;
            var retryAfter = sameVersion ? cached.RetryAfter : null;
            var reported = retryAfter.HasValue;
            void ReportFailure(string diagnostic)
            {
                if (!reported)
                {
                    Logger.LogWarning($"Failed to read an Office Jump List: {diagnostic}");
                    reported = true;
                }
                retryAfter ??= this._timeProvider.GetUtcNow().AddMinutes(5);
            }

            var entries = cached.Entries;
            if (!sameVersion || (retryAfter is { } due && this._timeProvider.GetUtcNow() >= due))
            {
                retryAfter = null;
                entries = [];
                try
                {
                    ReadDestinationFile(path, entries, ReportFailure, cancellationToken);
                }
                catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException or InvalidDataException)
                {
                    ReportFailure(ExceptionDiagnostics.Format(ex));
                    if (entries.Count == 0 && cached.Entries is { Count: > 0 })
                    {
                        entries = cached.Entries;
                    }
                }
            }

            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    items.Add(OfficeApplications.CreateRecentFile(entry.Target, entry.LastUsedTimeUtc, entry.ApplicationPath));
                }
                catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException or InvalidDataException)
                {
                    ReportFailure(ExceptionDiagnostics.Format(ex));
                }
            }
            this._destinations[path] = new(timestamp, entries, retryAfter);
        }

        var result = items
            .OrderByDescending(static item => item.LastUsedTimeUtc)
            .DistinctBy(
                static item => RecentFilePath.GetDeduplicationKey(item.TargetPath),
                StringComparer.Ordinal)
            .ToArray();
        Logger.LogDebug($"Loaded {result.Length} Office Jump List items in {stopwatch.ElapsedMilliseconds}ms");
        return result;
    }

    private static void ReadDestinationFile(
        string path,
        List<ParsedEntry> entries,
        Action<string> reportFailure,
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
                        return shellLink.LoadTargetData(stream, reportFailure);
                    });
                if (OfficeJumpListTarget.TryGetTarget(targetData, out var target))
                {
                    OfficeJumpListTarget.TryGetApplicationPath(targetData, out var applicationPath);
                    entries.Add(new(target, entry.LastUsedTimeUtc, applicationPath));
                }
            }
            catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException or InvalidDataException)
            {
                reportFailure(ExceptionDiagnostics.Format(ex));
            }
        }
    }

    private readonly record struct ParsedEntry(string Target, DateTimeOffset LastUsedTimeUtc, string ApplicationPath);
    private readonly record struct CachedDestination(DateTime Timestamp, List<ParsedEntry> Entries, DateTimeOffset? RetryAfter);

    private static string GetAutomaticDestinationsPath()
    {
        var recentFolder = Environment.GetFolderPath(Environment.SpecialFolder.Recent);
        return string.IsNullOrWhiteSpace(recentFolder)
            ? string.Empty
            : Path.Combine(recentFolder, "AutomaticDestinations");
    }

    public void Dispose() => this._watcher.Dispose();
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