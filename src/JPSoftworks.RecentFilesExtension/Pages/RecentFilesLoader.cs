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
    private string _currentQuery = string.Empty;
    private Guid _currentSearchToken;
    private bool _isInitialized;

    /// <summary>
    /// Raised whenever the underlying recent‐files list changes (e.g. you might clear your UI and restart).
    /// </summary>
    public event EventHandler? SourceChanged;

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
        var files = this._recentFilesProvider.GetRecentFiles().ToList();
        
        lock (this._syncRoot)
        {
            this._allFiles = files;
            this._filteredFiles = this._allFiles;
            this._isInitialized = this._recentFilesProvider.IsInitialLoadComplete;
        }

        if (this._isInitialized)
        {
            this.InitializationComplete?.Invoke(this, EventArgs.Empty);
            this.SourceChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnRecentFilesChanged(object? sender, IList<IRecentFile> newList)
    {
        bool wasInitialized;
        lock (this._syncRoot)
        {
            this._allFiles = newList.ToList();
            this.ApplyFilterInternal(this._currentQuery);
            wasInitialized = this._isInitialized;
            this._isInitialized = true;
        }
        
        if (!wasInitialized)
        {
            this.InitializationComplete?.Invoke(this, EventArgs.Empty);
        }
        
        this.SourceChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Resets the filter to a new search string (empty = no filter) and rewinds paging.
    /// </summary>
    public void Restart(string? searchQuery, Guid searchToken)
    {
        lock (this._syncRoot)
        {
            this._currentSearchToken = searchToken;
            this.ApplyFilterInternal(searchQuery ?? string.Empty);
        }
    }
    
    private void ApplyFilterInternal(string query)
    {
        this._currentQuery = query;
        this._cursor = 0;

        if (string.IsNullOrWhiteSpace(query))
        {
            this._filteredFiles = this._allFiles;
        }
        else
        {
            var useGlob = query.Contains('?') || query.Contains('*');
            if (useGlob)
            {
                var glob = Glob.Parse(this._currentQuery, new() { Evaluation = { CaseInsensitive = true } });
                this._filteredFiles = [.. this._allFiles.Where(file => FilterGlob(file, glob!))];
            }
            else
            {
                this._filteredFiles = [.. this._allFiles.Where(file => FilterPlain(file, query))];
            }
        }

        return;

        static bool FilterPlain(IRecentFile recentFile, string q)
        {
            return recentFile.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                   recentFile.TargetPath.Contains(q, StringComparison.OrdinalIgnoreCase);
        }

        static bool FilterGlob(IRecentFile recentFile, Glob glob)
        {
            return glob.IsMatch(recentFile.DisplayName) ||
                   glob.IsMatch(recentFile.TargetPath);
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
    /// Synchronous version of LoadNextPage for backward compatibility.
    /// </summary>
    public IList<RecentFileListItem> LoadNextPage(Guid searchToken, CancellationToken ct = default)
    {
        List<IRecentFile> slice;
        
        lock (this._syncRoot)
        {
            if (this._currentSearchToken != searchToken)
            {
                return [];
            }

            slice = this._filteredFiles.Skip(this._cursor).Take(this._pageSize).ToList();
            this._cursor += slice.Count;
        }

        var entries = new List<RecentFileListItem>(slice.Count);
        foreach (var shortcut in slice)
        {
            ct.ThrowIfCancellationRequested();
            entries.Add(new(shortcut));
        }

        lock (this._syncRoot)
        {
            return this._currentSearchToken != searchToken ? [] : entries;
        }
    }

    public void Dispose()
    {
        this._recentFilesProvider.RecentFilesChanged -= this.OnRecentFilesChanged;
    }
}