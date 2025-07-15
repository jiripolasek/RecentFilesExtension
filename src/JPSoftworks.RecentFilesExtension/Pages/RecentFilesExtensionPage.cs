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
    private readonly RecentFilesProvider _recentFilesProvider;
    private readonly RecentFilesLoader _loader;
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

        this.TriggerSearch("");
    }

    private void OnSourceChanged(object? sender, EventArgs e)
    {
        this._sourceChanged = true;
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
            this.IsLoading = false;
            this.RaiseItemsChanged(this._loadedItems.Count);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex);
            throw;
        }
    }

    public override IListItem[] GetItems()
    {
        if (this._sourceChanged)
        {
            this.TriggerSearch(this.SearchText);
        }
        return this._loadedItems.ToArray();
    }

    public void Dispose()
    {
        this._loader.SourceChanged -= this.OnSourceChanged;
        this._loader.Dispose();
        this._recentFilesProvider.Dispose();
    }
}