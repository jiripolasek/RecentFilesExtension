// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

namespace JPSoftworks.RecentFilesExtension.Model;

internal sealed class RecentShortcutFile : IRecentFile
{
    public string FullPath { get; init; }

    public string DisplayName { get; init; }

    public string TargetPath { get; init; }

    public FileInfo FileInfo { get; init; }

    internal RecentShortcutFile(FileInfo fileInfo, string displayName, string targetPath)
    {
        this.FileInfo = fileInfo;
        this.FullPath = fileInfo.FullName;
        this.DisplayName = displayName;
        this.TargetPath = targetPath;
    }

    public bool IsDirectory()
    {
        return Directory.Exists(this.TargetPath);
    }
}
