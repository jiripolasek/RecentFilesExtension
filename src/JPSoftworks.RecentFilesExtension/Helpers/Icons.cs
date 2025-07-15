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
}