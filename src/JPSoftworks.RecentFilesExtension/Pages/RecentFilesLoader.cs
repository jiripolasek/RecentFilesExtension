// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using DotNet.Globbing;
using JPSoftworks.RecentFilesExtension.Model;

namespace JPSoftworks.RecentFilesExtension.Pages;

internal sealed partial class RecentFilesLoader : IRecentFilesLoader
{
    private readonly int _pageSize;
    private readonly IRecentFilesProvider _recentFilesProvider;
    private readonly Lock _syncRoot = new();

    private List<IRecentFile> _allFiles = [];
    private List<IRecentFile> _filteredFiles = [];
    private int _cursor;
    private SearchQuery _currentQuery = new(string.Empty, [], [], string.Empty, false);
    private Guid _currentSearchToken;
    private bool _isInitialized;

    /// <summary>
    /// Raised whenever the underlying recent‐files list changes (e.g. you might clear your UI and restart).
    /// </summary>
    public event EventHandler<IList<IRecentFile>>? SourceChanged;

    /// <summary>
    /// Raised when the loader has completed its initial setup (after provider finishes loading).
    /// </summary>
    public event EventHandler? InitializationComplete;

    public RecentFilesLoader(IRecentFilesProvider recentFilesProvider, int pageSize = 20)
    {
        ArgumentNullException.ThrowIfNull(recentFilesProvider);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(pageSize, 0);

        this._pageSize = pageSize;
        this._recentFilesProvider = recentFilesProvider;
        this._recentFilesProvider.RecentFilesChanged += this.OnRecentFilesChanged;
        this._recentFilesProvider.InitialLoadComplete += this.OnInitialLoadComplete;

        this.InitializeFromProvider();
    }

    /// <summary>
    /// Returns true if the loader has been initialized with data from the provider.
    /// </summary>
    public bool IsInitialized
    {
        get
        {
            lock (this._syncRoot)
            {
                return this._isInitialized;
            }
        }
    }

    private void InitializeFromProvider()
    {
        bool isInitialLoadComplete;

        lock (this._syncRoot)
        {
            this._allFiles = this._recentFilesProvider.GetRecentFiles().ToList();
            this._filteredFiles = this._allFiles;
            isInitialLoadComplete = this._recentFilesProvider.IsInitialLoadComplete;
        }

        if (isInitialLoadComplete)
        {
            this.CompleteInitialization();
        }
    }

    private void OnRecentFilesChanged(object? sender, IList<IRecentFile> newList)
    {
        bool isInitialized;

        lock (this._syncRoot)
        {
            this._allFiles = newList.ToList();
            try
            {
                this.ApplyFilterInternal(this._currentQuery);
            }
            catch
            {
                // A bad item must not turn each provider refresh into another failed notification.
                this._filteredFiles = [];
                this._cursor = 0;
            }
            isInitialized = this._isInitialized;
        }

        if (isInitialized)
        {
            this.SourceChanged?.Invoke(this, newList);
        }
    }

    private void OnInitialLoadComplete(object? sender, EventArgs e)
    {
        this.CompleteInitialization();
    }

    private void CompleteInitialization()
    {
        bool initializationCompleted;

        lock (this._syncRoot)
        {
            initializationCompleted = !this._isInitialized;
            this._isInitialized = true;
        }

        if (initializationCompleted)
        {
            this.InitializationComplete?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Resets the filter to a new search string (empty = no filter) and rewinds paging.
    /// </summary>
    public void Restart(SearchQuery searchQuery, Guid searchToken)
    {
        lock (this._syncRoot)
        {
            this._currentSearchToken = searchToken;
            this._filteredFiles = [];
            this._cursor = 0;
            try
            {
                this.ApplyFilterInternal(searchQuery);
            }
            catch
            {
                this._currentQuery = searchQuery;
                throw;
            }
        }
    }

    private void ApplyFilterInternal(SearchQuery searchQuery)
    {
        var query = searchQuery.Query;
        var extensionFilter = searchQuery.AllowedExtensions;
        var fileNameFilter = searchQuery.AllowedFileNames;
        var location = searchQuery.Location;

        var temp = extensionFilter is [] && fileNameFilter is []
            ? this._allFiles
            : this._allFiles.Where(file => MatchesFileFilter(file, extensionFilter, fileNameFilter));

        if (searchQuery.ShowOnlyFolders)
        {
            temp = temp.Where(static f => f.IsDirectory());
        }

        if (!string.IsNullOrWhiteSpace(location))
        {
            var normalizedLocation = Path.TrimEndingDirectorySeparator(location);
            temp = temp.Where(f => IsPathInLocation(f.TargetPath, normalizedLocation));
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            this._filteredFiles = [.. temp];
        }
        else
        {
            Glob? glob = null;
            if (query.Contains('?') || query.Contains('*'))
            {
                try
                {
                    glob = Glob.Parse(query, new() { Evaluation = { CaseInsensitive = true } });
                }
                catch (Exception ex) when (ex is ArgumentException or FormatException or IndexOutOfRangeException)
                {
                    if (!string.Equals(query, this._currentQuery.Query, StringComparison.Ordinal))
                    {
                        Logger.LogDebug("Invalid wildcard; using a literal search");
                    }
                }
            }

            this._filteredFiles = glob is null
                ? [.. temp.Where(file => FilterPlain(file, query))]
                : [.. temp.Where(file => FilterGlob(file, glob))];
        }

        this._currentQuery = searchQuery;
        this._cursor = 0;

        static bool FilterPlain(IRecentFile recentFile, string q)
        {
            return recentFile.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                   recentFile.TargetPath.Contains(q, StringComparison.OrdinalIgnoreCase);
        }

        static bool MatchesFileFilter(
            IRecentFile recentFile,
            string[] allowedExtensions,
            string[] allowedFileNames)
        {
            var fileName = RecentFilePath.GetFileName(recentFile.TargetPath);
            return allowedExtensions is not [] &&
                       allowedExtensions.Contains(
                           Path.GetExtension(fileName),
                           StringComparer.OrdinalIgnoreCase) ||
                   allowedFileNames is not [] &&
                       allowedFileNames.Contains(
                           fileName,
                           StringComparer.OrdinalIgnoreCase);
        }

        static bool FilterGlob(IRecentFile recentFile, Glob glob)
        {
            return glob.IsMatch(recentFile.DisplayName) ||
                   glob.IsMatch(recentFile.TargetPath);
        }

        static bool IsPathInLocation(string path, string normalizedLocation)
        {
            if (!path.StartsWith(normalizedLocation, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return path.Length == normalizedLocation.Length ||
                   Path.EndsInDirectorySeparator(normalizedLocation) ||
                   IsDirectorySeparator(path[normalizedLocation.Length]);
        }

        static bool IsDirectorySeparator(char value)
        {
            return value == Path.DirectorySeparatorChar || value == Path.AltDirectorySeparatorChar;
        }
    }

    /// <summary>
    /// True if there's more data to page through.
    /// </summary>
    public bool HasMore
    {
        get
        {
            lock (this._syncRoot)
            {
                return this._cursor < this._filteredFiles.Count;
            }
        }
    }

    /// <summary>
    /// Returns the next slice of file metadata for the current search.
    /// </summary>
    public IList<IRecentFile> LoadNextPage(Guid searchToken)
    {
        lock (this._syncRoot)
        {
            if (this._currentSearchToken != searchToken)
            {
                return [];
            }

            var slice = this._filteredFiles.GetRange(this._cursor, Math.Min(this._pageSize, this._filteredFiles.Count - this._cursor));
            this._cursor += slice.Count;
            return slice;
        }
    }

    public void Dispose()
    {
        this._recentFilesProvider.RecentFilesChanged -= this.OnRecentFilesChanged;
        this._recentFilesProvider.InitialLoadComplete -= this.OnInitialLoadComplete;
    }
}