// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

namespace JPSoftworks.RecentFilesExtension.Model;

internal interface IRecentFile
{
    public string DisplayName { get; }

    public string TargetPath { get; }

    public string FullPath { get; }

    public string? IconPath { get; }

    public DateTimeOffset LastUsedTimeUtc { get; }

    public bool IsFileSystemPath { get; }

    public bool IsDirectory();
}