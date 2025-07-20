// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

using JPSoftworks.RecentFilesExtension.Model;

namespace JPSoftworks.RecentFilesExtension.Pages;

internal interface IRecentFilesProvider
{
    /// <summary>
    /// Fired whenever the list of recent files actually changes.
    /// </summary>
    event EventHandler<IList<IRecentFile>>? RecentFilesChanged;

    /// <summary>
    /// Fired when the initial cache loading is complete.
    /// </summary>
    event EventHandler? InitialLoadComplete;

    /// <summary>
    /// Returns true if the initial cache load has completed.
    /// </summary>
    bool IsInitialLoadComplete { get; }

    /// <summary>
    /// Returns the *cached* list of recent files.  
    /// Callers should subscribe to <see cref="RecentFilesProvider.RecentFilesChanged"/> for updates.
    /// </summary>
    IList<IRecentFile> GetRecentFiles();

    /// <summary>
    /// Asynchronously waits for the initial cache load to complete.
    /// </summary>
    /// <param name="timeout">Maximum time to wait. If null, waits indefinitely.</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if load completed within timeout, false if timed out</returns>
    Task<bool> WaitForInitialLoadAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default);
}