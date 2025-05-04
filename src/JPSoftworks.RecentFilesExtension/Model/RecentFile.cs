// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

using System.IO;

namespace JPSoftworks.RecentFilesExtension.Model;

public class RecentFile(string shellLinkTargetPath)
{
    public string FullPath { get; set; } = shellLinkTargetPath;

    internal bool IsDirectory()
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