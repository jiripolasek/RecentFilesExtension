// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

namespace JPSoftworks.RecentFilesExtension.Helpers;

internal interface IRecentFilesSettings
{
    event EventHandler? SourcesChanged;

    event EventHandler? AppearanceChanged;

    bool IncludeOpenSaveMru { get; }

    bool IncludeOfficeRecentFiles { get; }

    string ViewMode { get; set; }

    bool HideFileExtensions { get; }

    bool ShowFileTypeInSubtitle { get; }

    bool ShowFileTypeTags { get; }

    bool UseShellIcons { get; }

    bool ShowDetails { get; set; }
}