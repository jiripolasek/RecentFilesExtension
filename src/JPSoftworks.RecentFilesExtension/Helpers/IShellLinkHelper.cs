// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

namespace JPSoftworks.RecentFilesExtension.Helpers;

internal interface IShellLinkHelper
{
    string Description { get; }
    string Arguments { get; }
    bool HasArguments { get; }
    string TargetPath { get; }
    string DisplayName { get; }

    string RetrieveTargetPath(string path);
}