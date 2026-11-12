// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using JPSoftworks.RecentFilesExtension.Model;

namespace JPSoftworks.RecentFilesExtension.Pages;

internal interface IRecentFilesLoader : IDisposable
{
    /// <summary>
    /// Raised whenever the underlying recent‐files list changes (e.g. you might clear your UI and restart).
    /// </summary>
    event EventHandler<IList<IRecentFile>>? SourceChanged;

    /// <summary>
    /// Raised when the loader has completed its initial setup (after provider finishes loading).
    /// </summary>
    event EventHandler? InitializationComplete;

    /// <summary>
    /// Returns true if the loader has been initialized with data from the provider.
    /// </summary>
    bool IsInitialized { get; }

    /// <summary>
    /// True if there's more data to page through.
    /// </summary>
    bool HasMore { get; }

    /// <summary>
    /// Resets the filter to a new search string (empty = no filter) and rewinds paging.
    /// </summary>
    void Restart(SearchQuery searchQuery, Guid searchToken);

    /// <summary>
    /// Returns the next slice of file metadata for the current search.
    /// </summary>
    IList<IRecentFile> LoadNextPage(Guid searchToken);
}

internal record SearchQuery(
    string Query,
    string[] AllowedExtensions,
    string[] AllowedFileNames,
    string Location,
    bool ShowOnlyFolders);
