// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using JPSoftworks.RecentFilesExtension.Helpers;
using JPSoftworks.RecentFilesExtension.Model;
using JPSoftworks.RecentFilesExtension.Pages;
using JPSoftworks.RecentFilesExtension.Resources;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace JPSoftworks.RecentFilesExtension.UnitTests;

[TestClass]
public sealed class RecentFilesAppearanceTests
{
    [TestMethod]
    public void LocationFiltersMatchNormalizedForwardSlashPaths()
    {
        var file = new RecentPathFile("C:/Users/example/Documents/report%41.docx", DateTimeOffset.UtcNow);
        using var loader = new RecentFilesLoader(new StubProvider(file));
        var token = Guid.NewGuid();
        loader.Restart(new(string.Empty, [], [], @"C:\Users\example\Documents", false), token);

        Assert.AreEqual("report%41.docx", loader.LoadNextPage(token).Single().DisplayName);
        Assert.AreEqual(@"C:\Users\example\Documents\report%41.docx", file.TargetPath);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void InvalidWildcardUpdatesResultsAndFinishesLoading(bool initiallyLoaded)
    {
        var files = Enumerable.Range(1, 25)
            .Select(index => new RecentPathFile($"https://example.invalid/report{index}.txt", DateTimeOffset.UtcNow))
            .ToArray();
        var provider = new StubProvider(files) { IsInitialLoadComplete = initiallyLoaded };
        using var page = new RecentFilesExtensionPage(new TestRecentFilesSettings(), provider);
        if (initiallyLoaded)
        {
            Assert.HasCount(20, page.GetItems());
            Assert.IsTrue(page.HasMoreItems);
        }
        else
        {
            Assert.IsTrue(page.IsLoading);
        }

        var notifications = 0;
        page.ItemsChanged += (_, _) => notifications++;
        page.SearchText = "*[a-]";
        page.UpdateSearchText(string.Empty, page.SearchText);
        if (!initiallyLoaded)
        {
            provider.CompleteInitialization();
        }

        Assert.IsEmpty(page.GetItems());
        Assert.IsFalse(page.HasMoreItems);
        Assert.IsFalse(page.IsLoading);
        Assert.IsTrue(notifications > 0);
        page.LoadMore();
        Assert.IsEmpty(page.GetItems());
        provider.RaiseChanged();
        Assert.IsEmpty(page.GetItems());

        page.SearchText = "report21.txt";
        page.UpdateSearchText("*[a-]", page.SearchText);
        Assert.AreEqual("report21.txt", page.GetItems().Single().Title);
        Assert.IsFalse(page.IsLoading);
    }

    [TestMethod]
    [DoNotParallelize]
    public void UnavailableKnownFoldersAreLoggedWithoutBreakingFilters()
    {
        using var logs = new TestLogCapture();
        var missing = new LocationFilter("missing", Guid.Empty);
        Assert.AreEqual(string.Empty, missing.Location);
        logs.AssertLogged("Unable to resolve known folder");

        var filterProvider = new RecentFilesFilters();
        var filters = filterProvider.GetFilters();
        Assert.AreSame(filters, filterProvider.GetFilters());
        Assert.IsTrue(filters.OfType<LocationFilter>().All(static filter => !string.IsNullOrWhiteSpace(filter.Location)));
        Assert.IsTrue(filters.OfType<FileFilter>().Any(static filter => filter.Id == "Everything"));
    }

    [TestMethod]
    [DataRow("Report%20one.docx", "documents", "DOCX")]
    [DataRow("Sheet.xlsx", "documents", "XLSX")]
    [DataRow("Makefile", "projects", null)]
    public void TypeFiltersMatchCloudFilesWithQueryStrings(string fileName, string filterId, string? expectedTag)
    {
        var settings = new TestRecentFilesSettings { ShowFileTypeTags = true };
        var file = new RecentPathFile($"https://example.invalid/{fileName}?web=1#section", DateTimeOffset.UtcNow);
        using var page = new RecentFilesExtensionPage(settings, new StubProvider(file));
        var filters = Assert.IsInstanceOfType<RecentFilesFilters>(page.Filters);
        filters.CurrentFilterId = filterId;
        var item = page.GetItems().Single();
        Assert.AreEqual(file.DisplayName, item.Title);
        if (expectedTag is not null)
        {
            Assert.AreEqual(expectedTag, item.Tags.Single().Text);
        }
    }

    [TestMethod]
    [DataRow(@"\\recent-files-unavailable.invalid\share")]
    [DataRow(@"\\?\UNC\recent-files-unavailable.invalid\share")]
    public async Task UnavailableNetworkTargetsDoNotBlockLoadingOrAppearanceChangesAsync(string share)
    {
        await Task.Run(() =>
        {
            var settings = new TestRecentFilesSettings();
            var folder = new RecentShortcutFile(
                new FileInfo(Path.Combine(Path.GetTempPath(), "UnavailableRecentFolder.lnk")),
                "Reports.2026",
                $@"{share}\Reports.2026",
                isDirectory: true);
            IRecentFile[] files =
            [
                folder,
                .. Enumerable.Range(1, 20).Select(index => new RecentPathFile($@"{share}\report{index}.pdf", DateTimeOffset.UtcNow)),
            ];
            using var page = new RecentFilesExtensionPage(settings, new StubProvider(files));
            Assert.HasCount(20, page.GetItems());

            settings.ViewMode = "gallery";
            settings.HideFileExtensions = true;
            settings.ShowFileTypeInSubtitle = true;
            settings.ShowFileTypeTags = true;
            settings.ShowDetails = true;
            settings.RaiseAppearanceChanged();
            page.LoadMore();

            Assert.HasCount(21, page.GetItems());
            Assert.AreEqual("Reports.2026", page.GetItems()[0].Title);
            Assert.AreEqual(Strings.FileType_Folder, page.GetItems()[0].Tags.Single().Text);
            Assert.AreEqual(Strings.Command_OpenFolder, page.GetItems()[0].Command.Name);
            Assert.AreEqual("report20", page.GetItems()[^1].Title);
            Assert.IsNotNull(page.GetItems()[^1].Details);

            var filters = Assert.IsInstanceOfType<RecentFilesFilters>(page.Filters);
            filters.CurrentFilterId = "folders";
            Assert.HasCount(1, page.GetItems());
            Assert.AreEqual("Reports.2026", page.GetItems()[0].Title);
        }).WaitAsync(TimeSpan.FromSeconds(5));
    }

    [TestMethod]
    public void SideBySideHostsKeepTheirIconPolicyWhenThePreviewChanges()
    {
        var path = Path.Combine(Path.GetTempPath(), "report.pdf");
        var file = new RecentPathFile(path, DateTimeOffset.UnixEpoch);
        var settings = new TestRecentFilesSettings();
        using var modern = new RecentFilesExtensionPage(settings, new StubProvider(file), ShellIconSupport.Supported);
        using var legacy = new RecentFilesExtensionPage(settings, new StubProvider(file), ShellIconSupport.Unsupported);
        using var unknown = new RecentFilesExtensionPage(settings, new StubProvider(file));
        var row = unknown.GetItems().Single();
        var shellIcon = $"|ShellItemIcon|v1;{path.Length}:{path}";
        Assert.AreEqual(shellIcon, modern.GetItems().Single().Icon.Light.Icon);
        Assert.IsNotNull(legacy.GetItems().Single().Icon.Light.Data);
        Assert.IsNotNull(row.Icon.Light.Data);

        settings.UseShellIcons = true;
        settings.RaiseAppearanceChanged();
        Assert.AreSame(row, unknown.GetItems().Single());
        Assert.AreEqual(shellIcon, row.Icon.Light.Icon);
        Assert.AreEqual(shellIcon, modern.GetItems().Single().Icon.Light.Icon);
        Assert.IsNotNull(legacy.GetItems().Single().Icon.Light.Data);
    }

    [TestMethod]
    public void ChangingAppearancePreservesLoadedItemsAndAppliesToLaterPages()
    {
        var settings = new TestRecentFilesSettings();
        var files = Enumerable.Range(1, 21)
            .Select(index => new RecentPathFile($"https://contoso.sharepoint.com/Documents/report{index}.pdf", DateTimeOffset.UtcNow))
            .ToArray();
        using var page = new RecentFilesExtensionPage(settings, new StubProvider(files));
        var firstItem = page.GetItems()[0];
        Assert.IsNull(page.GridProperties);
        Assert.IsFalse(page.ShowDetails);
        Assert.HasCount(20, page.GetItems());
        Assert.IsTrue(page.HasMoreItems);

        settings.ViewMode = "grid";
        settings.HideFileExtensions = true;
        settings.ShowFileTypeInSubtitle = true;
        settings.ShowFileTypeTags = true;
        settings.ShowDetails = true;
        settings.RaiseAppearanceChanged();

        Assert.IsInstanceOfType<MediumGridLayout>(page.GridProperties);
        Assert.IsTrue(page.ShowDetails);
        Assert.AreSame(firstItem, page.GetItems()[0]);
        Assert.AreEqual("report1", firstItem.Title);
        Assert.AreEqual("PDF", firstItem.Tags.Single().Text);
        Assert.IsNotNull(firstItem.Details);

        settings.ViewMode = "gallery";
        settings.RaiseAppearanceChanged();
        var gallery = Assert.IsInstanceOfType<GalleryGridLayout>(page.GridProperties);
        Assert.IsTrue(gallery.ShowTitle);
        Assert.IsTrue(gallery.ShowSubtitle);
        page.LoadMore();
        Assert.HasCount(21, page.GetItems());
        Assert.IsFalse(page.HasMoreItems);
        var lastItem = page.GetItems()[^1];
        Assert.AreEqual("report21", lastItem.Title);
        Assert.AreEqual($"PDF file · {files[^1].TargetPath}", lastItem.Subtitle);
        Assert.AreEqual("PDF", lastItem.Tags.Single().Text);
        Assert.IsNotNull(lastItem.Details);

        settings.ViewMode = "unknown";
        settings.ShowDetails = false;
        settings.RaiseAppearanceChanged();
        Assert.IsNull(page.GridProperties);
        Assert.IsFalse(page.ShowDetails);
        Assert.IsNull(firstItem.Details);

        page.SearchText = "report21.pdf";
        page.UpdateSearchText(string.Empty, page.SearchText);
        Assert.HasCount(1, page.GetItems());
        Assert.AreEqual("report21", page.GetItems()[0].Title);

        page.Dispose();
        settings.ShowDetails = true;
        settings.RaiseAppearanceChanged();
        Assert.IsFalse(page.ShowDetails);
        Assert.IsEmpty(page.GetItems());
    }

    [TestMethod]
    public void PageAppliesSavedAppearanceOnInitialLoad()
    {
        var settings = new TestRecentFilesSettings { ViewMode = "gallery", HideFileExtensions = true, ShowDetails = true };
        var recentFile = new RecentPathFile("https://contoso.sharepoint.com/Documents/report.docx", DateTimeOffset.UtcNow);
        using var page = new RecentFilesExtensionPage(settings, new StubProvider(recentFile));

        Assert.IsInstanceOfType<GalleryGridLayout>(page.GridProperties);
        Assert.IsTrue(page.ShowDetails);
        Assert.AreEqual("report", page.GetItems().Single().Title);
        Assert.IsNotNull(page.GetItems().Single().Details);
    }

    [TestMethod]
    public void ItemMenusToggleAndPersistGlobalAppearanceWithoutReloadingItems()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"RecentFilesMenus-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "settings.json");
            var settings = new SettingsManager(path);
            var sourceChanges = 0;
            var appearanceChanges = 0;
            settings.SourcesChanged += (_, _) => sourceChanges++;
            settings.AppearanceChanged += (_, _) => appearanceChanges++;
            IRecentFile[] files =
            [
                new RecentPathFile(@"C:\report.pdf", DateTimeOffset.UtcNow),
                .. Enumerable.Range(1, 20).Select(index => new RecentPathFile($"https://contoso.sharepoint.com/Documents/report{index}.pdf", DateTimeOffset.UtcNow)),
            ];
            using var page = new RecentFilesExtensionPage(settings, new StubProvider(files));
            var firstItem = page.GetItems()[0];
            var viewItem = Assert.IsInstanceOfType<CommandContextItem>(firstItem.MoreCommands[^2]);
            var detailsItem = Assert.IsInstanceOfType<CommandContextItem>(firstItem.MoreCommands[^1]);
            var toggleView = Assert.IsInstanceOfType<IInvokableCommand>(viewItem.Command);
            var toggleDetails = Assert.IsInstanceOfType<IInvokableCommand>(detailsItem.Command);
            Assert.AreEqual(Strings.Command_ToggleViewMode, toggleView.Name);
            Assert.AreEqual(Strings.Command_ToggleDetails, toggleDetails.Name);

            Assert.AreEqual(CommandResultKind.KeepOpen, toggleView.Invoke(firstItem).Kind);
            Assert.IsInstanceOfType<MediumGridLayout>(page.GridProperties);
            Assert.AreEqual("grid", new SettingsManager(path).ViewMode);
            Assert.AreEqual(CommandResultKind.KeepOpen, toggleView.Invoke(firstItem).Kind);
            Assert.IsInstanceOfType<GalleryGridLayout>(page.GridProperties);
            Assert.AreEqual("gallery", new SettingsManager(path).ViewMode);

            Assert.AreEqual(CommandResultKind.KeepOpen, toggleDetails.Invoke(firstItem).Kind);
            Assert.IsTrue(page.ShowDetails);
            Assert.IsTrue(new SettingsManager(path).ShowDetails);
            Assert.IsNotNull(firstItem.Details);
            Assert.AreSame(firstItem, page.GetItems()[0]);
            page.LoadMore();
            Assert.HasCount(21, page.GetItems());
            foreach (var item in page.GetItems())
            {
                Assert.IsNotNull(item.Details);
                Assert.IsInstanceOfType<Separator>(item.MoreCommands[^3]);
                Assert.AreSame(viewItem, item.MoreCommands[^2]);
                Assert.AreSame(detailsItem, item.MoreCommands[^1]);
                Assert.HasCount(1, item.MoreCommands.OfType<Separator>());
            }

            Assert.AreEqual(CommandResultKind.KeepOpen, toggleView.Invoke(firstItem).Kind);
            Assert.IsNull(page.GridProperties);
            Assert.AreEqual("list", new SettingsManager(path).ViewMode);
            Assert.AreEqual(CommandResultKind.KeepOpen, toggleDetails.Invoke(firstItem).Kind);
            Assert.IsFalse(page.ShowDetails);
            Assert.IsFalse(new SettingsManager(path).ShowDetails);
            Assert.IsTrue(page.GetItems().All(item => item.Details is null));
            Assert.AreSame(firstItem, page.GetItems()[0]);
            Assert.AreEqual(0, sourceChanges);
            Assert.AreEqual(5, appearanceChanges);

            page.SearchText = "report20.pdf";
            page.UpdateSearchText(string.Empty, page.SearchText);
            var searchItem = page.GetItems().Single();
            Assert.AreSame(viewItem, searchItem.MoreCommands[^2]);
            Assert.AreSame(detailsItem, searchItem.MoreCommands[^1]);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [TestMethod]
    public void AppearanceChangesUpdateDisplayedRowsEvenAfterARefreshReplacesThem()
    {
        var path = Path.Combine(Path.GetTempPath(), "displayed-report.pdf");
        var time = DateTimeOffset.UnixEpoch;
        var provider = new StubProvider(new RecentPathFile(path, time));
        var settings = new TestRecentFilesSettings();
        using var page = new RecentFilesExtensionPage(settings, provider, ShellIconSupport.Supported);
        var displayed = page.GetItems().Single();

        provider.Replace(new RecentPathFile(path, time.AddHours(1)));
        settings.ViewMode = "gallery";
        settings.HideFileExtensions = true;
        settings.ShowDetails = true;
        settings.RaiseAppearanceChanged();

        Assert.AreEqual("displayed-report", displayed.Title);
        Assert.AreEqual($"|JumboShellItemIcon|v1;{path.Length}:{path}", displayed.Icon.Light.Icon);
        Assert.IsNotNull(displayed.Details);
        Assert.AreNotSame(displayed, page.GetItems().Single());
    }

    [TestMethod]
    public void CachedRowsApplyAppearanceWhenShownAndReuseTheirTagsAcrossSearches()
    {
        var alpha = new RecentPathFile("https://example.invalid/alpha.pdf", DateTimeOffset.UnixEpoch);
        var beta = new RecentPathFile("https://example.invalid/beta.pdf", DateTimeOffset.UnixEpoch);
        var settings = new TestRecentFilesSettings { ShowFileTypeTags = true };
        using var page = new RecentFilesExtensionPage(settings, new StubProvider(alpha, beta));
        var row = page.GetItems().Single(item => item.Title == "alpha.pdf");
        var tag = row.Tags.Single();

        page.SearchText = "beta";
        settings.HideFileExtensions = true;
        settings.RaiseAppearanceChanged();
        Assert.AreEqual("alpha.pdf", row.Title);

        page.SearchText = "alpha";
        Assert.AreSame(row, page.GetItems().Single());
        Assert.AreEqual("alpha", row.Title);
        Assert.AreSame(tag, row.Tags.Single());

        page.SearchText = "beta";
        page.SearchText = "alpha";
        Assert.AreSame(tag, page.GetItems().Single().Tags.Single());
    }

    [TestMethod]
    public void SearchesReuseRowsAndRefreshReplacesOnlyChangedOrRemovedFiles()
    {
        var time = DateTimeOffset.UnixEpoch;
        var alpha = new RecentPathFile("https://example.invalid/alpha.pdf", time);
        var beta = new RecentPathFile("https://example.invalid/beta.pdf", time);
        var provider = new StubProvider(alpha, beta);
        var settings = new TestRecentFilesSettings { ShowDetails = true };
        using var page = new RecentFilesExtensionPage(settings, provider);
        var original = page.GetItems();
        var menu = original[0].MoreCommands;
        var details = original[0].Details;

        page.SearchText = "beta";
        Assert.AreSame(original[1], page.GetItems().Single());
        provider.Replace(new RecentPathFile(alpha.TargetPath, time), new RecentPathFile(beta.TargetPath, time.AddHours(1)));
        Assert.AreNotSame(original[1], page.GetItems().Single());
        settings.HideFileExtensions = true;
        settings.RaiseAppearanceChanged();
        page.SearchText = "alpha";
        var result = page.GetItems().Single();
        Assert.AreSame(original[0], result);
        Assert.AreEqual("alpha", result.Title);
        Assert.AreSame(menu, result.MoreCommands);
        Assert.AreSame(details, result.Details);

        provider.Replace(beta);
        Assert.IsEmpty(page.GetItems());
        provider.Replace(alpha, beta);
        Assert.AreNotSame(original[0], page.GetItems().Single());
    }

    private sealed class StubProvider(params IRecentFile[] files) : IRecentFilesProvider
    {
        public event EventHandler<IList<IRecentFile>>? RecentFilesChanged;

        public event EventHandler? InitialLoadComplete;

        public bool IsInitialLoadComplete { get; set; } = true;

        public IList<IRecentFile> GetRecentFiles() => [.. files];

        public Task<bool> WaitForInitialLoadAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
            => Task.FromResult(this.IsInitialLoadComplete);

        public void CompleteInitialization()
        {
            this.IsInitialLoadComplete = true;
            this.InitialLoadComplete?.Invoke(this, EventArgs.Empty);
        }

        public void RaiseChanged() => this.RecentFilesChanged?.Invoke(this, files);

        public void Replace(params IRecentFile[] updated)
        {
            files = updated;
            this.RaiseChanged();
        }
    }
}
