// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

namespace JPSoftworks.RecentFilesExtension.Helpers;

internal static class Icons
{
    internal static IconInfo FileExplorerSegoe { get; } = new("\uEC50");

    internal static IconInfo OpenFile { get; } = new("\uE8E5"); // OpenFile

    internal static IconInfo Document { get; } = new("\uE8A5"); // Document

    internal static IconInfo FolderOpen { get; } = new("\uE838"); // FolderOpen

    internal static IconInfo CopyPath { get; } = new("\uE8C8"); // CopyPath

    internal static IconInfo OpenWith { get; } = new("\uE7AC"); // OpenWith

    internal static IconInfo MainIcon { get; } = IconHelpers.FromRelativePath("Assets\\Square20x20Logo.scale-100.png");

    internal static IconInfo BigIcon { get; } = IconHelpers.FromRelativePath("Assets\\Square44x44Logo.scale-200.png");

    internal static class Filters
    {
        internal static IconInfo AllFiles { get; } = new("\uF571"); // Document

        internal static IconInfo Documents { get; } = new("\uF000"); // Document

        internal static IconInfo Images { get; } = new("\uEB9F"); // Picture

        internal static IconInfo Videos { get; } = new("\uE714"); // Video

        internal static IconInfo Music { get; } = new("\uEC4F"); // Note

        internal static IconInfo Archives { get; } = new("\uF012"); // ZipFolder

        internal static IconInfo Folders { get; } = new("\uE838"); // FolderOpen

        internal static IconInfo Code { get; } = new("\uE943"); // Code

        internal static IconInfo Scripts { get; } = new("\uE756"); // Prompt

        internal static IconInfo Projects { get; } = new("\uE7B8"); // ProjectDocument

        internal static IconInfo Fonts { get; } = new("\uE8D2"); // Font

        internal static IconInfo Database { get; } = new("\uE96A"); // Database

        internal static IconInfo DownloadsFolder { get; } = new("\uE896");

        internal static IconInfo DesktopFolder { get; } = new("\uE90E"); // Dock bottom

        internal static IconInfo UserProfileFolder { get; } = new("\uEC25");

    }
}