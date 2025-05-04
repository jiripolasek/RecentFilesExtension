// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using Windows.Win32;
using Windows.Win32.Storage.FileSystem;
using Windows.Win32.System.Com;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.Shell.Common;
using static Windows.Win32.PInvoke;

// ReSharper disable InconsistentNaming
// ReSharper disable SuspiciousTypeConversion.Global

namespace JPSoftworks.RecentFilesExtension.Helpers;

/// <summary>
/// Helper for resolving .lnk shortcuts using CsWin32-generated interop.
/// </summary>
public sealed partial class ShellLinkHelper : IShellLinkHelper, IDisposable
{
    private const int MAX_PATH = 260;

    private readonly IPersistFile _persistFile;
    private readonly IShellLinkW _shellLink;

    public string Description { get; private set; } = string.Empty;
    public string Arguments { get; private set; } = string.Empty;
    public bool HasArguments { get; private set; }
    public string TargetPath { get; private set; } = string.Empty;
    public string DisplayName { get; private set; } = string.Empty;
    public string? IconPath { get; private set; }
    public int IconIndex { get; private set; } = -1;

    public ShellLinkHelper()
    {
        this._shellLink = (IShellLinkW)new ShellLink();
        this._persistFile = (IPersistFile)this._shellLink;
    }

    public void Dispose()
    {
        Marshal.ReleaseComObject(this._shellLink);
    }

    public string RetrieveTargetPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            throw new FileNotFoundException("Shortcut file not found.", path);
        }

        // Load the .lnk file
        this._persistFile.Load(path, (int)STGM.STGM_READ);

        // Comment for now, it's too slow; possibly revisit later when the list is updated on background
        //// Resolve without UI
        //try
        //{
        //    this._shellLink.Resolve(HWND.Null, (uint)(SLR_FLAGS.SLR_NO_UI | SLR_FLAGS.SLR_NOUPDATE));
        //}
        //catch (Exception ex)
        //{
        //    Logger.LogError(ex);
        //}

        // Retrieve target via GetPath using Span overload
        var data = new WIN32_FIND_DATAW();
        Span<char> buffer = stackalloc char[MAX_PATH];
        this._shellLink.GetPath(buffer, ref data, (int)SLGP_FLAGS.SLGP_RAWPATH);
        var term = buffer.IndexOf('\0');
        if (term < 0)
        {
            term = buffer.Length;
        }

        this.TargetPath = new string(buffer[..term]);

        // Read description via Span overload
        Span<char> descBuf = stackalloc char[MAX_PATH];
        try
        {
            this._shellLink.GetDescription(descBuf);
            term = descBuf.IndexOf('\0');
            if (term < 0)
            {
                term = descBuf.Length;
            }

            this.Description = new string(descBuf[..term]);
        }
        catch (COMException)
        {
            this.Description = string.Empty;
        }

        // Read arguments via Span overload
        Span<char> argBuf = stackalloc char[MAX_PATH];
        try
        {
            this._shellLink.GetArguments(argBuf);
            term = argBuf.IndexOf('\0');
            if (term < 0)
            {
                term = argBuf.Length;
            }

            this.Arguments = new string(argBuf[..term]);
            this.HasArguments = this.Arguments.Length > 0;
        }
        catch (COMException)
        {
            this.Arguments = string.Empty;
            this.HasArguments = false;
        }

        // get icon
        Span<char> iconLocationBuffer = stackalloc char[MAX_PATH];
        try
        {
            this._shellLink.GetIconLocation(iconLocationBuffer, out var iconIndex);
            term = iconLocationBuffer.IndexOf('\0');
            if (term < 0)
            {
                term = iconLocationBuffer.Length;
            }

            var iconPathString = new string(iconLocationBuffer[..term]);
            this.IconIndex = iconIndex;

            if (string.IsNullOrEmpty(iconPathString) || !File.Exists(iconPathString))
            {
                var r = GetFileIconLocation(path);
                if (!string.IsNullOrWhiteSpace(r.IconPath))
                {
                    this.IconPath = r.IconPath;
                    this.IconIndex = r.IconIndex;
                }
                else

                    // Icon path is invalid, get the default icon for the target file type
                if (!string.IsNullOrEmpty(this.TargetPath) && File.Exists(this.TargetPath))
                {
                    // Get the default icon for the target file
                    var extension = Path.GetExtension(this.TargetPath);
                    if (!string.IsNullOrEmpty(extension))
                    {
                        // Use SHGetFileInfo to get the default icon path
                        var tempExtIcon = GetFileIconLocation(extension);
                        if (!string.IsNullOrEmpty(tempExtIcon.IconPath))
                        {
                            this.IconPath = tempExtIcon.IconPath;
                            this.IconIndex = tempExtIcon.IconIndex;
                        }
                        else
                        {
                            // If extension icon not found, get icon for the file specifically
                            var tempFileIcon = GetFileIconLocation(this.TargetPath);
                            this.IconPath = tempFileIcon.IconPath;
                            this.IconIndex = tempFileIcon.IconIndex;
                        }
                    }
                    else
                    {
                        // Direct file icon lookup if no extension
                        var tempIcon = GetFileIconLocation(this.TargetPath);
                        this.IconPath = tempIcon.IconPath;
                        this.IconIndex = tempIcon.IconIndex;
                    }
                }
                else
                {
                    // Default to shell32.dll if target path is invalid too
                    this.IconPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                        "shell32.dll");
                    this.IconIndex = 0; // Default icon
                }
            }
            else
            {
                // The icon path from the shortcut is valid
                this.IconPath = iconPathString;
            }
        }
        catch (COMException)
        {
            this.IconPath = null;
            this.IconIndex = -1;
        }

        try
        {
            this.DisplayName = GetLinkTargetDisplayName(this._shellLink);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex.ToString());
            this.DisplayName = Path.GetFileNameWithoutExtension(path);
#if DEBUG
            this.DisplayName = "C: " + this.DisplayName;
#endif
        }

        return this.TargetPath;
    }

    private static unsafe string GetLinkTargetDisplayName(IShellLinkW link)
    {
        ITEMIDLIST* ppidl = null;
        try
        {
            link.GetIDList(out ppidl);
            SHGetNameFromIDList(*ppidl, SIGDN.SIGDN_NORMALDISPLAY, out var pwsz)
                .ThrowOnFailure();
            var result = new string(pwsz.AsSpan());
            return result;
        }
        finally
        {
            if (ppidl != null)
            {
                Marshal.FreeCoTaskMem((IntPtr)ppidl);
            }
        }
    }

    private static unsafe (string IconPath, int IconIndex) GetFileIconLocation(string fileOrExtension)
    {
        var fileInfo = new SHFILEINFOW();
        var fileInfoResult = SHGetFileInfo(
            fileOrExtension,
            0,
            &fileInfo,
            (uint)sizeof(SHFILEINFOW),
            SHGFI_FLAGS.SHGFI_ICONLOCATION);

        if (fileInfoResult == nuint.Zero)
        {
            return (Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "shell32.dll"), 0);
        }

        // Extract the icon path from the szDisplayName field
        var iconPath = new string(fileInfo.szDisplayName.Value);
        if (string.IsNullOrEmpty(iconPath))
        {
            return (Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "shell32.dll"), 0);
        }

        // The icon index is stored in the iIcon field
        return (iconPath, fileInfo.iIcon);
    }

    [Flags]
    internal enum SLR_FLAGS
    {
        /// <summary>
        /// Do not display a dialog box if the link cannot be resolved. When SLR_NO_UI is set,
        /// the high-order word of fFlags can be set to a time-out value that specifies the
        /// maximum amount of time to be spent resolving the link. The function returns if the
        /// link cannot be resolved within the time-out duration. If the high-order word is set
        /// to zero, the time-out duration will be set to the default value of 3,000 milliseconds
        /// (3 seconds). To specify a value, set the high word of fFlags to the desired time-out
        /// duration, in milliseconds.
        /// </summary>
        SLR_NO_UI = 0x01,

        /// <summary>Obsolete and no longer used</summary>
        SLR_ANY_MATCH = 0x02,

        /// <summary>
        /// If the link object has changed, update its path and list of identifiers.
        /// If SLR_UPDATE is set, you do not need to call IPersistFile::IsDirty to determine
        /// whether or not the link object has changed.
        /// </summary>
        SLR_UPDATE = 0x04,

        /// <summary>Do not update the link information</summary>
        SLR_NOUPDATE = 0x08,

        /// <summary>Do not execute the search heuristics</summary>
        SLR_NOSEARCH = 0x10,

        /// <summary>Do not use distributed link tracking</summary>
        SLR_NOTRACK = 0x20,

        /// <summary>
        /// Disable distributed link tracking. By default, distributed link tracking tracks
        /// removable media across multiple devices based on the volume name. It also uses the
        /// Universal Naming Convention (UNC) path to track remote file systems whose drive letter
        /// has changed. Setting SLR_NOLINKINFO disables both types of tracking.
        /// </summary>
        SLR_NOLINKINFO = 0x40,

        /// <summary>Call the Microsoft Windows Installer</summary>
        SLR_INVOKE_MSI = 0x80
    }
}