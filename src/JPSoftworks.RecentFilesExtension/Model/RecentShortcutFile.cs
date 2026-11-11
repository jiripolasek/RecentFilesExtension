// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

namespace JPSoftworks.RecentFilesExtension.Model;

internal sealed class RecentShortcutFile : IRecentFile
{
    private readonly bool _isDirectory;

    public string FullPath { get; init; }

    public string DisplayName { get; init; }

    public string TargetPath { get; init; }

    public string? IconPath => this.TargetPath;

    public FileInfo FileInfo { get; init; }

    public DateTimeOffset LastUsedTimeUtc => new(this.FileInfo.LastWriteTimeUtc, TimeSpan.Zero);

    public bool IsFileSystemPath => true;

    internal RecentShortcutFile(FileInfo fileInfo, string displayName, string targetPath, bool isDirectory)
    {
        this.FileInfo = fileInfo;
        this.FullPath = fileInfo.FullName;
        this.DisplayName = displayName;
        this.TargetPath = targetPath;
        this._isDirectory = isDirectory;
    }

    public bool IsDirectory()
    {
        return this._isDirectory;
    }
}