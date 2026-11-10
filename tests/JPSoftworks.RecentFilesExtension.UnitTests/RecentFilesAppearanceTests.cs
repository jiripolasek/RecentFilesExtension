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

    private sealed class StubProvider(params IRecentFile[] files) : IRecentFilesProvider
    {
        public event EventHandler<IList<IRecentFile>>? RecentFilesChanged { add { } remove { } }

        public event EventHandler? InitialLoadComplete { add { } remove { } }

        public bool IsInitialLoadComplete => true;

        public IList<IRecentFile> GetRecentFiles() => [.. files];

        public Task<bool> WaitForInitialLoadAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
            => Task.FromResult(true);
    }
}
