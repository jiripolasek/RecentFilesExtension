// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

namespace JPSoftworks.RecentFilesExtension.Helpers;

internal interface IRecentFilesSettings
{
    event EventHandler? SourcesChanged;

    bool IncludeOpenSaveMru { get; }

    bool IncludeOfficeRecentFiles { get; }
}