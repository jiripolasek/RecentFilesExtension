﻿// ------------------------------------------------------------
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

    internal RecentShortcutFile(string shortcutFilePath, string displayName, string targetPath)
    {
        this.FullPath = shortcutFilePath;
        this.DisplayName = displayName;
        this.TargetPath = targetPath;
    }

    public bool IsDirectory()
    {
        if (!Path.Exists(this.TargetPath))
        {
            return false;
        }

        var attr = File.GetAttributes(this.TargetPath);

        // detect whether it is a directory or file
        return (attr & FileAttributes.Directory) == FileAttributes.Directory;
    }
}