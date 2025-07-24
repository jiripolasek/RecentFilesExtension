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
    private Guid _searchToken = Guid.Empty;

    private List<IListItem> _loadedItems = [];
    private bool _sourceChanged = true;

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
        this._sourceChanged = true;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        this._sourceChanged = true;
        this.TriggerSearch(this.SearchText);
    }

    public override void UpdateSearchText(string oldSearch, string newSearch)
    {
        if (oldSearch != newSearch || newSearch == "" && this._sourceChanged)
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
            this._searchToken = Guid.NewGuid();
            this._sourceChanged = false;
            this._loader.Restart(newSearch, this._searchToken);
            this._loadedItems = [];
            this.LoadMore();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex);
            throw;
        }
    }

    public override void LoadMore()
    {
        try
        {
            this.IsLoading = true;
            var items = this._loader.LoadNextPage(this._searchToken, CancellationToken.None);
            this._loadedItems.AddRange(items);
            this.HasMoreItems = this._loader.HasMore;
            this.IsLoading = !this._loader.IsInitialized;

            this.RaiseItemsChanged(this._loadedItems.Count);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex);
            this.IsLoading = !this._loader.IsInitialized;
            this.RaiseItemsChanged(this._loadedItems.Count);
        }
    }

    public override IListItem[] GetItems()
    {
        if (this._sourceChanged)
        {
            this.TriggerSearch(this.SearchText);
        }

        var newEmptyContent = string.IsNullOrWhiteSpace(this.SearchText)
            ? NothingFoundEmptyContent
            : NothingFoundWithQueryEmptyContent;

        if (this._loadedItems.Count == 0)
        {
            if (this.EmptyContent != newEmptyContent)
            {
                this.EmptyContent = newEmptyContent;
            }
        }

        return this._loadedItems.ToArray();
    }

    public void Dispose()
    {
        this._loader.SourceChanged -= this.OnSourceChanged;
        this._loader.InitializationComplete -= this.OnSourceInitialized;
        this._loader.Dispose();
        (this._recentFilesProvider as IDisposable)?.Dispose();
    }
}