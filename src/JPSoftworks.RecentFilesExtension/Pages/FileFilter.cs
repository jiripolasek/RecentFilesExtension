// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;

namespace JPSoftworks.RecentFilesExtension.Pages;

internal sealed partial class FileFilter : Filter
{
    public required string[] Extensions { get; init; }

    public string[] FileNames { get; init; } = [];
}

internal sealed partial class FoldersFilter : Filter
{
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
        : this(id, GetKnownFolderPath(folderId))
    {
    }

    private static unsafe string GetKnownFolderPath(Guid folderId)
    {
        PWSTR path = default;

        try
        {
            var result = PInvoke.SHGetKnownFolderPath(
                    folderId,
                    KNOWN_FOLDER_FLAG.KF_FLAG_DEFAULT | KNOWN_FOLDER_FLAG.KF_FLAG_DONT_VERIFY,
                    null,
                    out path);

            if (result.Failed || path == null)
            {
                Logger.LogWarning($"Unable to resolve known folder {folderId}: HRESULT 0x{result.Value:X8}");
                return string.Empty;
            }

            return path.ToString();
        }
        finally
        {
            if (path != null)
            {
                Marshal.FreeCoTaskMem((IntPtr)path.Value);
            }
        }
    }
}