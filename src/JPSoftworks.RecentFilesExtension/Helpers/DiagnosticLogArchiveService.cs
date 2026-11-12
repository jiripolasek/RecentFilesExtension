// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.Globalization;
using System.IO.Compression;

namespace JPSoftworks.RecentFilesExtension.Helpers;

internal sealed class DiagnosticLogArchiveService
{
    private const string LogFilePattern = "log*.txt";

    private readonly (string Path, string Prefix)[] _logDirectories;

    public DiagnosticLogArchiveService(string logDirectoryPath, string fallbackLogDirectoryPath)
    {
        ArgumentNullException.ThrowIfNull(logDirectoryPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(fallbackLogDirectoryPath);
        this._logDirectories = [(logDirectoryPath, string.Empty), (fallbackLogDirectoryPath, "fallback/")];
    }

    public Task<DiagnosticLogArchiveResult> CreateArchiveOnDesktopAsync(
        CancellationToken cancellationToken)
    {
        return this.CreateArchiveAsync(GetDesktopDirectoryPath(), cancellationToken);
    }

    internal async Task<DiagnosticLogArchiveResult> CreateArchiveAsync(
        string destinationDirectoryPath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectoryPath);
        cancellationToken.ThrowIfCancellationRequested();

        var destinationDirectory = Path.GetFullPath(destinationDirectoryPath);
        Directory.CreateDirectory(destinationDirectory);
        var logFiles = this._logDirectories
            .Where(static directory => Directory.Exists(directory.Path))
            .SelectMany(static directory => Directory.EnumerateFiles(directory.Path, LogFilePattern, SearchOption.TopDirectoryOnly)
                .Select(path => (Path: path, EntryName: directory.Prefix + Path.GetFileName(path))))
            .OrderBy(static file => file.EntryName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var archiveFileName = string.Format(
            CultureInfo.InvariantCulture,
            "RecentFilesExtension-Logs-{0:yyyyMMdd-HHmmssfff}.zip",
            DateTimeOffset.Now);
        var archivePath = Path.Combine(destinationDirectory, archiveFileName);

        var archiveStream = new FileStream(
            archivePath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        try
        {
            await using var ownedStream = archiveStream;
            using var archive = new ZipArchive(ownedStream, ZipArchiveMode.Create);

            foreach (var logFile in logFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var entry = archive.CreateEntry(
                    logFile.EntryName,
                    CompressionLevel.Optimal);
                await using var entryStream = entry.Open();
                await using var logStream = new FileStream(
                    logFile.Path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete,
                    bufferSize: 81920,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                await logStream.CopyToAsync(entryStream, cancellationToken).ConfigureAwait(false);
            }

            return new DiagnosticLogArchiveResult(archivePath, logFiles.Length);
        }
        catch
        {
            TryDeleteIncompleteArchive(archivePath);
            throw;
        }
    }

    private static string GetDesktopDirectoryPath()
    {
        var desktop = Environment.GetFolderPath(
            Environment.SpecialFolder.DesktopDirectory,
            Environment.SpecialFolderOption.DoNotVerify);
        if (!string.IsNullOrWhiteSpace(desktop))
        {
            return desktop;
        }

        var userProfile = Environment.GetFolderPath(
            Environment.SpecialFolder.UserProfile,
            Environment.SpecialFolderOption.DoNotVerify);
        return !string.IsNullOrWhiteSpace(userProfile)
            ? userProfile
            : throw new InvalidOperationException("The Desktop and user profile directories are unavailable.");
    }

    private static void TryDeleteIncompleteArchive(string archivePath)
    {
        try
        {
            File.Delete(archivePath);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

internal readonly record struct DiagnosticLogArchiveResult(
    string ArchivePath,
    int LogFileCount);