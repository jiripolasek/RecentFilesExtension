// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using JPSoftworks.RecentFilesExtension.Helpers;
using JPSoftworks.RecentFilesExtension.Resources;

namespace JPSoftworks.RecentFilesExtension.Pages;

internal sealed partial class RecentFilesExtensionPage : DynamicListPage, IDisposable
{
    private static readonly CommandItem NothingFoundEmptyContent;
    private static readonly CommandItem NothingFoundWithQueryEmptyContent;

    private readonly IRecentFilesProvider _recentFilesProvider;
    private readonly IRecentFilesLoader _loader;
    private readonly RecentFilesFilters _filters;
    private readonly Lock _syncRoot = new();
    private CancellationTokenSource _searchCancellationTokenSource = new();

    private Guid _searchToken = Guid.Empty;
    private List<IListItem> _loadedItems = [];
    private bool _sourceChanged = true;
    private bool _isDisposed;

    public RecentFilesExtensionPage()
    {
        this.Icon = Icons.MainIcon;
        this.Title = Strings.Page_RecentFiles!;
        this.Name = Strings.Page_RecentFiles!;
        this.Id = "com.jpsoftworks.cmdpal.recentfiles";

        this._recentFilesProvider = new RecentFilesProvider();
        this._loader = new RecentFilesLoader(this._recentFilesProvider);
        this._loader.SourceChanged += this.OnSourceChanged;
        this._loader.InitializationComplete += this.OnSourceInitialized;

        this.EmptyContent = new CommandItem
        {
            Title = "Loading...",
            Subtitle = "Loading your recent files",
            Icon = Icons.MainIcon,
        };

        this._filters = new RecentFilesFilters();
        this._filters.PropChanged += (_, _) => { this.TriggerSearch(this.SearchText); };
        this.Filters = this._filters;

        this.IsLoading = true;
        this.TriggerSearch("");
    }



    static RecentFilesExtensionPage()
    {
        NothingFoundEmptyContent = new()
        {
            Title = "No recent files found",
            Subtitle = "It's so empty here",
            Icon = Icons.BigIcon,
        };
        NothingFoundWithQueryEmptyContent = new()
        {
            Title = "No recent files matching the search text",
            Subtitle = "Try changing your search query",
            Icon = Icons.BigIcon,
        };
    }

    private void OnSourceChanged(object? sender, EventArgs e)
    {
        lock (this._syncRoot)
        {
            if (!this._isDisposed)
            {
                this._sourceChanged = true;
            }
        }
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        lock (this._syncRoot)
        {
            if (this._isDisposed)
            {
                return;
            }

            this._sourceChanged = true;
        }

        this.TriggerSearch(this.SearchText);
    }

    public override void UpdateSearchText(string oldSearch, string newSearch)
    {
        bool sourceChanged;
        lock (this._syncRoot)
        {
            if (this._isDisposed)
            {
                return;
            }

            sourceChanged = this._sourceChanged;
        }

        if (oldSearch != newSearch || newSearch == "" && sourceChanged)
        {
            this.TriggerSearch(newSearch);
        }
    }

    private void TriggerSearch(string newSearch)
    {
        if (!this._loader.IsInitialized)
        {
            this.IsLoading = true;
            return;
        }

        try
        {
            var filter = this._filters.GetFilters().OfType<IFilter>().FirstOrDefault(t => t.Id == this._filters.CurrentFilterId);
            var selectedFilter = (filter as FileFilter)?.Extensions ?? [];
            var locationFilter = (filter as LocationFilter)?.Location ?? "";
            var onlyFolders = filter is FoldersFilter;

            var searchToken = Guid.NewGuid();
            var nextCancellationTokenSource = new CancellationTokenSource();
            CancellationTokenSource previousCancellationTokenSource;
            lock (this._syncRoot)
            {
                if (this._isDisposed)
                {
                    nextCancellationTokenSource.Dispose();
                    return;
                }

                try
                {
                    this._loader.Restart(
                        new SearchQuery(newSearch, selectedFilter, locationFilter, onlyFolders),
                        searchToken);
                }
                catch
                {
                    nextCancellationTokenSource.Dispose();
                    throw;
                }

                previousCancellationTokenSource = this._searchCancellationTokenSource;
                this._searchCancellationTokenSource = nextCancellationTokenSource;
                this._searchToken = searchToken;
                this._sourceChanged = false;
                this._loadedItems = [];
            }

            previousCancellationTokenSource.Cancel();
            previousCancellationTokenSource.Dispose();
            this.LoadMore(searchToken);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex);
            throw;
        }
    }

    public override void LoadMore()
    {
        this.LoadMore(null);
    }

    private void LoadMore(Guid? expectedSearchToken)
    {
        CancellationToken cancellationToken = default;

        try
        {
            this.IsLoading = true;

            int itemCount;
            bool hasMore;
            lock (this._syncRoot)
            {
                if (this._isDisposed ||
                    expectedSearchToken.HasValue && expectedSearchToken.Value != this._searchToken)
                {
                    return;
                }

                cancellationToken = this._searchCancellationTokenSource.Token;
                var items = this._loader.LoadNextPage(this._searchToken, cancellationToken);
                this._loadedItems.AddRange(items);
                itemCount = this._loadedItems.Count;
                hasMore = this._loader.HasMore;
            }

            this.HasMoreItems = hasMore;
            this.RaiseItemsChanged(itemCount);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected when a search is superseded or the page is disposed.
        }
        catch (Exception ex)
        {
            Logger.LogError(ex);

            int itemCount;
            lock (this._syncRoot)
            {
                itemCount = this._loadedItems.Count;
            }

            this.RaiseItemsChanged(itemCount);
        }
        finally
        {
            this.IsLoading = !this._loader.IsInitialized;
        }
    }

    public override IListItem[] GetItems()
    {
        bool sourceChanged;
        lock (this._syncRoot)
        {
            if (this._isDisposed)
            {
                return [];
            }

            sourceChanged = this._sourceChanged;
        }

        if (sourceChanged)
        {
            this.TriggerSearch(this.SearchText);
        }

        IListItem[] items;
        lock (this._syncRoot)
        {
            items = this._loadedItems.ToArray();
        }

        var newEmptyContent = string.IsNullOrWhiteSpace(this.SearchText)
            ? NothingFoundEmptyContent
            : NothingFoundWithQueryEmptyContent;

        if (items.Length == 0)
        {
            if (this.EmptyContent != newEmptyContent)
            {
                this.EmptyContent = newEmptyContent;
            }
        }

        return items;
    }

    public void Dispose()
    {
        CancellationTokenSource searchCancellationTokenSource;
        lock (this._syncRoot)
        {
            if (this._isDisposed)
            {
                return;
            }

            this._isDisposed = true;
            searchCancellationTokenSource = this._searchCancellationTokenSource;
        }

        searchCancellationTokenSource.Cancel();
        searchCancellationTokenSource.Dispose();
        this._loader.SourceChanged -= this.OnSourceChanged;
        this._loader.InitializationComplete -= this.OnSourceInitialized;
        this._loader.Dispose();
        (this._recentFilesProvider as IDisposable)?.Dispose();
    }
}
