// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

using DotNet.Globbing;
using JPSoftworks.RecentFilesExtension.Model;

namespace JPSoftworks.RecentFilesExtension.Pages;

internal sealed partial class RecentFilesLoader : IDisposable
{
    private readonly int _pageSize;
    private readonly RecentFilesProvider _recentFilesProvider;

    private List<IRecentFile> _allFiles;
    private List<IRecentFile> _filteredFiles;
    private int _cursor;
    private string _currentQuery = string.Empty;
    private Guid _currentSearchToken;

    /// <summary>
    /// Raised whenever the underlying recent‐files list changes (e.g. you might clear your UI and restart).
    /// </summary>
    public event EventHandler? SourceChanged;

    public RecentFilesLoader(RecentFilesProvider recentFilesProvider, int pageSize = 20)
    {
        ArgumentNullException.ThrowIfNull(recentFilesProvider);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(pageSize, 0);

        this._pageSize = pageSize;
        this._recentFilesProvider = recentFilesProvider;

        this._allFiles = this._recentFilesProvider.GetRecentFiles().ToList();
        this._filteredFiles = this._allFiles;

        this._recentFilesProvider.RecentFilesChanged += this.OnRecentFilesChanged;
    }

    private void OnRecentFilesChanged(object? sender, IList<IRecentFile> newList)
    {
        this._allFiles = newList.ToList();
        this.ApplyFilter(this._currentQuery);
        this.SourceChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Resets the filter to a new search string (empty = no filter) and rewinds paging.
    /// </summary>
    public void Restart(string? searchQuery, Guid searchToken)
    {
        this._currentSearchToken = searchToken;
        this.ApplyFilter(searchQuery ?? string.Empty);
    }

    private void ApplyFilter(string query)
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
                var glob = Glob.Parse(this._currentQuery, new GlobOptions { Evaluation = { CaseInsensitive = true } });
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

        static bool FilterGlob(IRecentFile arg, Glob glob)
        {
            return glob.IsMatch(arg.DisplayName) || glob.IsMatch(arg.TargetPath);
        }
    }

    /// <summary>
    /// True if there’s more data to page through.
    /// </summary>
    public bool HasMore => this._cursor < this._filteredFiles.Count;

    /// <summary>
    /// Loads the next page of entries, including async thumbnail extraction.
    /// </summary>
    public IList<RecentFileListItem> LoadNextPage(Guid searchToken, CancellationToken ct = default)
    {
        if (this._currentSearchToken != searchToken)
        {
            return [];
        }

        List<IRecentFile> slice = this._filteredFiles.Skip(this._cursor).Take(this._pageSize).ToList();
        this._cursor += slice.Count;

        var entries = new List<RecentFileListItem>(slice.Count);
        foreach (var shortcut in slice)
        {
            ct.ThrowIfCancellationRequested();
            entries.Add(new RecentFileListItem(shortcut));
        }

        return this._currentSearchToken != searchToken ? [] : entries;
    }

    public void Dispose()
    {
        this._recentFilesProvider.RecentFilesChanged -= this.OnRecentFilesChanged;
    }
}