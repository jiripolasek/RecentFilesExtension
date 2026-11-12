// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using JPSoftworks.RecentFilesExtension.Helpers;
using JPSoftworks.RecentFilesExtension.Model;
using JPSoftworks.RecentFilesExtension.Resources;

namespace JPSoftworks.RecentFilesExtension.Pages;

internal sealed partial class RecentFilesExtensionPage : DynamicListPage, IDisposable
{
    private static readonly CommandItem NothingFoundEmptyContent;
    private static readonly CommandItem NothingFoundWithQueryEmptyContent;

    private readonly IRecentFilesSettings _settings;
    private readonly ShellIconSupport _shellIconSupport;
    private readonly Lazy<IContextItem[]> _appearanceCommands;
    private readonly IRecentFilesProvider _recentFilesProvider;
    private readonly RecentFilesLoader _loader;
    private readonly RecentFilesFilters _filters;
    private readonly Lock _syncRoot = new();
    private Dictionary<string, RecentFileListItem> _itemsByPath = new(StringComparer.Ordinal);

    private Guid _searchToken = Guid.Empty;
    private List<RecentFileListItem> _loadedItems = [];
    private bool _sourceChanged = true;
    private bool _isDisposed;

    public RecentFilesExtensionPage(IRecentFilesSettings settings, IRecentFilesProvider? recentFilesProvider = null,
        ShellIconSupport shellIconSupport = ShellIconSupport.Unknown)
    {
        ArgumentNullException.ThrowIfNull(settings);

        this.Icon = Icons.MainIcon;
        this.Title = Strings.Page_RecentFiles!;
        this.Name = Strings.Page_RecentFiles!;
        this.Id = "com.jpsoftworks.cmdpal.recentfiles";

        this._settings = settings;
        this._shellIconSupport = shellIconSupport;
        this._appearanceCommands = new(() =>
        [
            new Separator(),
            new CommandContextItem(new AnonymousCommand(() => settings.ViewMode = settings.ViewMode switch
            {
                "list" => "grid",
                "grid" => "gallery",
                _ => "list",
            })
            {
                Name = Strings.Command_ToggleViewMode,
                Icon = Icons.ToggleViewMode,
                Result = CommandResult.KeepOpen(),
            }) { RequestedShortcut = KeyChords.ToggleViewMode },
            new CommandContextItem(new AnonymousCommand(() => settings.ShowDetails = !settings.ShowDetails)
            {
                Name = Strings.Command_ToggleDetails,
                Icon = Icons.ToggleDetails,
                Result = CommandResult.KeepOpen(),
            }) { RequestedShortcut = KeyChords.ToggleDetails },
        ]);
        this._recentFilesProvider = recentFilesProvider ?? new RecentFilesProvider(settings);
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

        this._settings.AppearanceChanged += this.OnAppearanceChanged;
        this.OnAppearanceChanged(this._settings, EventArgs.Empty);
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

    private void OnAppearanceChanged(object? sender, EventArgs e)
    {
        RecentFileListItem[] items;
        lock (this._syncRoot)
        {
            if (this._isDisposed)
            {
                return;
            }

            items = [.. this._loadedItems];
        }

        this.GridProperties = this._settings.ViewMode switch
        {
            "grid" => new MediumGridLayout(),
            "gallery" => new GalleryGridLayout(),
            _ => null,
        };
        this.ShowDetails = this._settings.ShowDetails;
        foreach (var item in items)
        {
            item.ApplyAppearance(this._settings, this._shellIconSupport);
        }
    }

    private void OnSourceChanged(object? sender, IList<IRecentFile> files)
    {
        lock (this._syncRoot)
        {
            if (this._isDisposed)
            {
                return;
            }

            this._sourceChanged = true;
            if (this._itemsByPath.Count == 0)
            {
                return;
            }

            var retained = new Dictionary<string, RecentFileListItem>(StringComparer.Ordinal);
            foreach (var file in files)
            {
                try
                {
                    var path = file.TargetPath;
                    if (this._itemsByPath.TryGetValue(path, out var item) && item.Matches(file))
                    {
                        retained[path] = item;
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogWarning($"Failed to read a refreshed file's path {ExceptionDiagnostics.Summarize(ex)}");
                }
            }
            this._itemsByPath = retained;
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
            var selectedFileFilter = filter as FileFilter;
            var selectedExtensions = selectedFileFilter?.Extensions ?? [];
            var selectedFileNames = selectedFileFilter?.FileNames ?? [];
            var locationFilter = (filter as LocationFilter)?.Location ?? "";
            var onlyFolders = filter is FoldersFilter;

            var searchToken = Guid.NewGuid();
            Exception? restartFailure = null;
            lock (this._syncRoot)
            {
                if (this._isDisposed)
                {
                    return;
                }

                try
                {
                    this._loader.Restart(
                        new SearchQuery(newSearch, selectedExtensions, selectedFileNames, locationFilter, onlyFolders),
                        searchToken);
                }
                catch (Exception ex)
                {
                    restartFailure = ex;
                }

                this._searchToken = searchToken;
                this._sourceChanged = false;
                this._loadedItems = [];
            }

            if (restartFailure is not null)
            {
                Logger.LogError($"Failed to search recent files {ExceptionDiagnostics.Format(restartFailure)}");
                this.HasMoreItems = false;
                this.IsLoading = false;
                this.RaiseItemsChanged(0);
            }
            else
            {
                this.LoadMore(searchToken);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to search recent files {ExceptionDiagnostics.Format(ex)}");
        }
    }

    public override void LoadMore()
    {
        this.LoadMore(null);
    }

    private void LoadMore(Guid? expectedSearchToken)
    {
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

                foreach (var file in this._loader.LoadNextPage(this._searchToken))
                {
                    if (!this._itemsByPath.TryGetValue(file.TargetPath, out var item) || !item.Matches(file))
                    {
                        item = new RecentFileListItem(file, this._appearanceCommands);
                        this._itemsByPath[file.TargetPath] = item;
                    }
                    this._loadedItems.Add(item);
                }
                itemCount = this._loadedItems.Count;
                hasMore = this._loader.HasMore;
            }

            this.HasMoreItems = hasMore;
            this.RaiseItemsChanged(itemCount);
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to load recent-file results {ExceptionDiagnostics.Format(ex)}");

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
            items = [.. this._loadedItems];
        }

        foreach (RecentFileListItem item in items)
        {
            item.ApplyAppearance(this._settings, this._shellIconSupport);
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
        lock (this._syncRoot)
        {
            if (this._isDisposed)
            {
                return;
            }

            this._isDisposed = true;
            this._loadedItems.Clear();
            this._itemsByPath.Clear();
        }

        this._settings.AppearanceChanged -= this.OnAppearanceChanged;
        this._loader.SourceChanged -= this.OnSourceChanged;
        this._loader.InitializationComplete -= this.OnSourceInitialized;
        this._loader.Dispose();
        (this._recentFilesProvider as IDisposable)?.Dispose();
    }
}
