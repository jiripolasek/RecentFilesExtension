// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using Windows.Win32;
using Windows.Win32.UI.Shell;

namespace JPSoftworks.RecentFilesExtension.Pages;

internal sealed partial class FileFilter : Filter
{
    public required string[] Extensions { get; init; }
}

internal sealed partial class LocationFilter : Filter
{
    public required string Location { get; init; }

    [SetsRequiredMembers]
    public LocationFilter(string id, string location)
    {
        this.Location = location;
        this.Name = Path.GetFileName(location.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) + " folder";
        this.Id = id;
    }

    [SetsRequiredMembers]
    public LocationFilter(string id, Environment.SpecialFolder specialFolder)
    {
        this.Location = Environment.GetFolderPath(specialFolder);
        this.Name = Path.GetFileName(this.Location.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) + " folder";
        this.Id = id;
    }

    [SetsRequiredMembers]
    public LocationFilter(string id, Guid folderId)
    {
        var hr = PInvoke.SHGetKnownFolderPath(folderId, KNOWN_FOLDER_FLAG.KF_FLAG_DEFAULT | KNOWN_FOLDER_FLAG.KF_FLAG_DONT_VERIFY, null, out var pPath);
        if (hr != 0 || pPath == null)
        {
            return;
        }

        this.Location = pPath.ToString();
        this.Name = Path.GetFileName(this.Location.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) + " folder";
        this.Id = id;
    }
}