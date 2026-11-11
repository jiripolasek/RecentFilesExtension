// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using JPSoftworks.RecentFilesExtension.Helpers;

namespace JPSoftworks.RecentFilesExtension.UnitTests;

internal sealed class TestRecentFilesSettings : IRecentFilesSettings
{
    public event EventHandler? SourcesChanged;

    public event EventHandler? AppearanceChanged;

    public bool IncludeOpenSaveMru { get; set; } = true;

    public bool IncludeOfficeRecentFiles { get; set; } = true;

    public string ViewMode { get; set; } = "list";

    public bool HideFileExtensions { get; set; }

    public bool ShowFileTypeInSubtitle { get; set; }

    public bool ShowFileTypeTags { get; set; }

    public bool ShowDetails { get; set; }

    public void RaiseSourcesChanged()
    {
        this.SourcesChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RaiseAppearanceChanged()
    {
        this.AppearanceChanged?.Invoke(this, EventArgs.Empty);
    }
}