// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

using System.IO;

namespace JPSoftworks.RecentFilesExtension.Model;

internal class RecentShortcutFile : IRecentFile
{
    public string FullPath { get; init; }

    public string DisplayName { get; init; }

    public string TargetPath { get; init; }

    public RecentShortcutFile(string shortcutFilePath, string displayName, string targetPath)
    {
        this.FullPath = shortcutFilePath;
        this.DisplayName = displayName;
        this.TargetPath = targetPath;
    }

    public bool IsDirectory()
    {
        if (!Path.Exists(this.FullPath))
        {
            return false;
        }

        var attr = File.GetAttributes(this.FullPath);

        // detect whether it is a directory or file
        return (attr & FileAttributes.Directory) == FileAttributes.Directory;
    }
}