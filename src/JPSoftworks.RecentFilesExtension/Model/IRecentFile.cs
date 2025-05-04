// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

namespace JPSoftworks.RecentFilesExtension.Model;

public interface IRecentFile
{
    public string DisplayName { get; }

    public string TargetPath { get; }

    public string FullPath { get; }

    public bool IsDirectory();
}