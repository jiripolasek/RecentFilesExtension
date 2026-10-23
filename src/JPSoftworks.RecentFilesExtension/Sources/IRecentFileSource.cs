// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using JPSoftworks.RecentFilesExtension.Model;

namespace JPSoftworks.RecentFilesExtension.Sources;

internal interface IRecentFileSource : IDisposable
{
    event EventHandler? Changed;

    IReadOnlyList<IRecentFile> GetRecentFiles(CancellationToken cancellationToken);
}

internal sealed record RecentFileSourceRegistration(
    IRecentFileSource Source,
    Func<bool> IsEnabled);