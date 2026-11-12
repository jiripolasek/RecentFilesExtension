// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.Globalization;
using System.Text;
using JPSoftworks.RecentFilesExtension.Commands;
using JPSoftworks.RecentFilesExtension.Helpers;
using JPSoftworks.RecentFilesExtension.Model;
using JPSoftworks.RecentFilesExtension.Resources;
using JPCommands = JPSoftworks.RecentFilesExtension.Commands;

namespace JPSoftworks.RecentFilesExtension.Pages;

internal sealed partial class RecentFileListItem : ListItem
{
    private const uint ListThumbnailSize = 48;
    private const uint GalleryThumbnailSize = 256;

    // Image formats recognized by CmdPal 0.13
    private static readonly HashSet<string> HostImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".jfif", ".gif", ".bmp", ".dib", ".avif",
        ".heic", ".heif", ".jxr", ".svg", ".tif", ".tiff", ".ico", ".webp",
    };
    private static readonly CompositeFormat ExtensionFileTypeFormat = CompositeFormat.Parse(Strings.FileType_ExtensionFile);
    private readonly IRecentFile _recentFile;
    private Lazy<IContextItem[]> _moreCommands;
    private Lazy<IDetails>? _details;
    private Lazy<IIconInfo?> _icon;
    private Appearance _appearance;

    internal RecentFileListItem(IRecentFile recentFile, Lazy<IContextItem[]>? appearanceCommands = null)
        : base(new JPCommands.OpenFileCommand(recentFile))
    {
        ArgumentNullException.ThrowIfNull(recentFile);

        this._recentFile = recentFile;
        this.Title = recentFile.DisplayName;
        this.Subtitle = recentFile.TargetPath;
        this._moreCommands = new(() => this.CreateMoreCommands(appearanceCommands?.Value ?? []));
        this._icon = new(() => this.CreateIcon(ListThumbnailSize, false));
    }

    public override IContextItem[] MoreCommands
    {
        get => this._moreCommands.Value;
        set => this.SetProperty(ref this._moreCommands, new Lazy<IContextItem[]>(value));
    }

    public override IDetails? Details
    {
        get => this._details?.Value;
        set => this.SetProperty(ref this._details, value is null ? null : new Lazy<IDetails>(value));
    }

    public override IIconInfo? Icon
    {
        get => this._icon.Value;
        set => this.SetProperty(ref this._icon, new Lazy<IIconInfo?>(value));
    }

    internal bool Matches(IRecentFile file) =>
        ReferenceEquals(this._recentFile, file) || RecentFilesProvider.HasSameMetadata(this._recentFile, file);

    private IContextItem[] CreateMoreCommands(IContextItem[] appearanceCommands)
    {
        var recentFile = this._recentFile;
        List<IContextItem> moreCommands = [];
        if (!string.IsNullOrWhiteSpace(recentFile.TargetPath))
        {
            if (recentFile.IsFileSystemPath)
            {
                moreCommands.Add(new CommandContextItem(new ShowFileInFolderCommand(recentFile.TargetPath) { Name = Strings.Command_ShowInFolder! }) { RequestedShortcut = KeyChords.OpenInExplorer });
                moreCommands.Add(new CommandContextItem(new OpenWithCommand(recentFile)));
            }

            moreCommands.Add(new CommandContextItem(new JPCommands.CopyPathCommand(recentFile)) { RequestedShortcut = KeyChords.CopyPath });
        }
        return [.. moreCommands, .. appearanceCommands];
    }

    internal void ApplyAppearance(IRecentFilesSettings settings, ShellIconSupport shellIconSupport = ShellIconSupport.Unknown)
    {
        var appearance = new Appearance(settings.HideFileExtensions, settings.ShowFileTypeInSubtitle,
            settings.ShowFileTypeTags, settings.ShowDetails, settings.ViewMode is "grid" or "gallery",
            shellIconSupport == ShellIconSupport.Supported || (shellIconSupport == ShellIconSupport.Unknown && settings.UseShellIcons));
        if (appearance != this._appearance)
        {
            this.UpdateAppearance(appearance);
        }
    }

    private void UpdateAppearance(Appearance appearance)
    {
        var previous = this._appearance;
        var recentFile = this._recentFile;
        var isDirectory = recentFile.IsDirectory();
        var fileName = RecentFilePath.GetFileName(recentFile.TargetPath);
        var extension = isDirectory ? string.Empty : Path.GetExtension(fileName);
        if (extension.Length == fileName.Length)
        {
            extension = string.Empty;
        }

        // A property notification can cause the host to request these rows again.
        this._appearance = appearance;
        this.Title = appearance.HideFileExtensions &&
                     extension.Length > 0 &&
                     recentFile.DisplayName.Length > extension.Length &&
                     recentFile.DisplayName.EndsWith(extension, StringComparison.OrdinalIgnoreCase)
            ? recentFile.DisplayName[..^extension.Length]
            : recentFile.DisplayName;

        var type = isDirectory
            ? Strings.FileType_Folder
            : extension.Length == 0
                ? Strings.FileType_File
                : string.Format(CultureInfo.CurrentCulture, ExtensionFileTypeFormat, extension[1..].ToUpperInvariant());

        this.Subtitle = appearance.ShowFileTypeInSubtitle ? $"{type} · {recentFile.TargetPath}" : recentFile.TargetPath;
        if (appearance.ShowFileTypeTags != previous.ShowFileTypeTags)
        {
            this.Tags = appearance.ShowFileTypeTags
                ? [new Tag(extension.Length > 0 ? extension[1..].ToUpperInvariant() : type) { ToolTip = type }]
                : [];
        }

        if (appearance.ShowDetails != (this._details is not null))
        {
            this._details = appearance.ShowDetails ? new(() => this.CreateDetails(type)) : null;
            this.OnPropertyChanged(nameof(this.Details));
        }

        if (appearance.LargeIcons != previous.LargeIcons || appearance.UseShellIcons != previous.UseShellIcons)
        {
            var size = appearance.LargeIcons ? GalleryThumbnailSize : ListThumbnailSize;
            this._icon = new(() => this.CreateIcon(size, appearance.UseShellIcons));
            this.OnPropertyChanged(nameof(this.Icon));
        }
    }

    private Details CreateDetails(string type)
    {
        var recentFile = this._recentFile;
        var lastUsed = recentFile.LastUsedTimeUtc;
        return new Details
        {
            Title = recentFile.DisplayName,
            Metadata =
            [
                new DetailsElement { Key = Strings.Details_Path, Data = new DetailsLink { Text = recentFile.TargetPath } },
                new DetailsElement { Key = Strings.Details_Type, Data = new DetailsLink { Text = type } },
                new DetailsElement
                {
                    Key = Strings.Details_LastUsed,
                    Data = new DetailsLink
                    {
                        Text = lastUsed == DateTimeOffset.MinValue
                            ? Strings.Details_UnknownLastUsed
                            : lastUsed.ToLocalTime().ToString("g", CultureInfo.CurrentCulture),
                    },
                },
            ],
        };
    }

    private IconInfo? CreateIcon(uint size, bool useShellIcons)
    {
        var file = this._recentFile;
        if (!file.IsFileSystemPath && file.IsDirectory())
        {
            return Icons.FolderOpen;
        }

        if (useShellIcons && file.IsFileSystemPath && file.TargetPath.IsNetworkPath())
        {
            return file.IsDirectory() ? Icons.FolderOpen : Icons.Document;
        }

        var path = LocalPath(file.IconPath);
        if (!file.IsFileSystemPath && path is not null &&
            Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(path))
        {
            // Older CmdPal hosts recognize executable icon paths with a lowercase extension.
            return new IconInfo(Path.ChangeExtension(path, ".exe"));
        }

        var fallback = file.IsFileSystemPath ? LocalPath(file.FullPath) : null;
        if (path is null || string.Equals(path, fallback, StringComparison.OrdinalIgnoreCase))
        {
            path ??= fallback;
            fallback = null;
        }

        if (path is null)
        {
            return null;
        }

        if (useShellIcons)
        {
            if (size == GalleryThumbnailSize && HostImageExtensions.Contains(Path.GetExtension(path)) &&
                GetResidentImagePath(path) is { } imagePath)
            {
                if (Path.GetExtension(imagePath).Equals(".svg", StringComparison.OrdinalIgnoreCase) && imagePath.AsSpan().IndexOfAny('#', '%') < 0)
                {
                    // CmdPal uses SvgImageSource for SVG paths and BitmapImage for streams.
                    return new IconInfo(imagePath);
                }

                return new IconInfo(new IconData(new RecentFileThumbnail(imagePath, fallback, size)));
            }

            // The pinned SDK predates ShellItemIconProtocol. Its v1 payload counts UTF-16 code units.
            var protocol = size == GalleryThumbnailSize ? "|JumboShellItemIcon|" : "|ShellItemIcon|";
            return new IconInfo(string.Create(CultureInfo.InvariantCulture, $"{protocol}v1;{path.Length}:{path}"));
        }

        return new IconInfo(new IconData(new RecentFileThumbnail(path, fallback, size)));

        static string? LocalPath(string? value) =>
            !string.IsNullOrWhiteSpace(value) && Path.IsPathFullyQualified(value) && !value.IsNetworkPath() ? value : null;
    }

    private static string? GetResidentImagePath(string path)
    {
        // WinRT accepts a drive path for a local file.
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal))
        {
            path = path[4..];
            if (!Path.IsPathFullyQualified(path))
            {
                return null;
            }
        }

        var image = new FileInfo(path);
        // Skip cloud placeholders and links that could require network access for a preview.
        return image.Exists && (image.Attributes & (FileAttributes.Offline | FileAttributes.ReparsePoint)) == 0 ? path : null;
    }

    private readonly record struct Appearance(bool HideFileExtensions, bool ShowFileTypeInSubtitle, bool ShowFileTypeTags,
        bool ShowDetails, bool LargeIcons, bool UseShellIcons);
}
