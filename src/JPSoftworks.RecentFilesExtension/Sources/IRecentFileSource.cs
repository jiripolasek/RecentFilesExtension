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

    /// <summary>Reads a snapshot of recent files.</summary>
    /// <remarks>Callers must serialize reads for each source instance; implementations may keep unsynchronized caches.</remarks>
    IReadOnlyList<IRecentFile> GetRecentFiles(CancellationToken cancellationToken);
}

internal sealed record RecentFileSourceRegistration(
    Func<IRecentFileSource> CreateSource,
    Func<bool> IsEnabled)
{
    public IRecentFileSource? Source { get; set; }
    public bool StartFailed { get; set; }
}