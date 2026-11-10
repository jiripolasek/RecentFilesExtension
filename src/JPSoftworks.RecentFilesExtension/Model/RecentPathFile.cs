// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

namespace JPSoftworks.RecentFilesExtension.Model;

/// <summary>
/// A recent file discovered from a source that supplies a path and timestamp.
/// </summary>
internal sealed class RecentPathFile : IRecentFile
{
    private readonly bool _isDirectory;

    public RecentPathFile(
        string targetPath,
        DateTimeOffset lastUsedTimeUtc,
        string? launchPath = null,
        string? iconPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);

        if (!RecentFilePath.TryNormalize(targetPath, out var normalizedPath))
        {
            throw new ArgumentException("The value must be an absolute file path or supported URI.", nameof(targetPath));
        }

        this.TargetPath = normalizedPath;
        this.DisplayName = RecentFilePath.GetDisplayName(normalizedPath);
        this.LastUsedTimeUtc = lastUsedTimeUtc.ToUniversalTime();
        this.IsFileSystemPath = RecentFilePath.IsFileSystemPath(normalizedPath);
        this._isDirectory = RecentFilePath.IsDirectory(normalizedPath);
        this.FullPath = string.IsNullOrWhiteSpace(launchPath) ? normalizedPath : launchPath;
        this.IconPath = string.IsNullOrWhiteSpace(iconPath)
            ? this.IsFileSystemPath ? normalizedPath : null
            : iconPath;
    }

    public string DisplayName { get; }

    public string TargetPath { get; }

    public string FullPath { get; }

    public string? IconPath { get; }

    public DateTimeOffset LastUsedTimeUtc { get; }

    public bool IsFileSystemPath { get; }

    public bool IsDirectory()
    {
        return this._isDirectory;
    }
}
