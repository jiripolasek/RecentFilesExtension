// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using JPSoftworks.RecentFilesExtension.Model;
using JPSoftworks.RecentFilesExtension.Sources;

namespace JPSoftworks.RecentFilesExtension.UnitTests;

[TestClass]
public sealed class CompositeRecentFileSourceTests
{
    [TestMethod]
    public void GetRecentFilesDeduplicatesAcrossSourcesAndKeepsNewestEntry()
    {
        var older = new RecentPathFile(
            @"\\?\C:\Documents\Report.docx",
            new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero));
        var newer = new RecentPathFile(
            @"c:\documents\REPORT.docx",
            new DateTimeOffset(2026, 1, 2, 10, 0, 0, TimeSpan.Zero));
        var middle = new RecentPathFile(
            "file:///C:/Documents/Report.docx",
            new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
        using var firstSource = new StubSource(older);
        using var secondSource = new StubSource(middle, newer);
        using var composite = new CompositeRecentFileSource(
        [
            new(firstSource, static () => true),
            new(secondSource, static () => true),
        ]);

        var files = composite.GetRecentFiles(CancellationToken.None);

        Assert.HasCount(1, files);
        Assert.AreSame(newer, files[0]);
    }

    [TestMethod]
    public void GetRecentFilesOrdersDistinctEntriesByTimestamp()
    {
        var older = new RecentPathFile(
            @"C:\Documents\Older.docx",
            new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero));
        var newer = new RecentPathFile(
            @"C:\Documents\Newer.docx",
            new DateTimeOffset(2026, 1, 2, 10, 0, 0, TimeSpan.Zero));
        using var source = new StubSource(older, newer);
        using var composite = new CompositeRecentFileSource(
        [
            new(source, static () => true),
        ]);

        var files = composite.GetRecentFiles(CancellationToken.None);

        CollectionAssert.AreEqual(
            new[] { newer.TargetPath, older.TargetPath },
            files.Select(static item => item.TargetPath).ToArray());
    }

    [TestMethod]
    public void GetRecentFilesDeduplicatesSharePointWebLinkAndKeepsNewestEntry()
    {
        var older = new RecentPathFile(
            "https://contoso.sharepoint.com/Documents/tontr%C3%A4ger.xlsx",
            new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero));
        var newer = new RecentPathFile(
            "https://contoso.sharepoint.com/Documents/tonträger.xlsx?web=1#sheet",
            new DateTimeOffset(2026, 1, 2, 10, 0, 0, TimeSpan.Zero));
        using var source = new StubSource(older, newer);
        using var composite = new CompositeRecentFileSource(
        [
            new(source, static () => true),
        ]);

        var files = composite.GetRecentFiles(CancellationToken.None);

        Assert.HasCount(1, files);
        Assert.AreSame(newer, files[0]);
    }

    [TestMethod]
    public void DisabledSourceIsExcludedAndDoesNotRaiseChanged()
    {
        var enabled = false;
        using var source = new StubSource(new RecentPathFile(
            @"C:\Documents\Hidden.docx",
            DateTimeOffset.UtcNow));
        using var composite = new CompositeRecentFileSource(
        [
            new(source, () => enabled),
        ]);
        var changeCount = 0;
        composite.Changed += (_, _) => changeCount++;

        source.RaiseChanged();
        Assert.IsEmpty(composite.GetRecentFiles(CancellationToken.None));
        Assert.AreEqual(0, changeCount);

        enabled = true;
        source.RaiseChanged();
        Assert.HasCount(1, composite.GetRecentFiles(CancellationToken.None));
        Assert.AreEqual(1, changeCount);
    }

    [TestMethod]
    public void SettingsChangeReevaluatesEnabledSourcesAndRaisesChanged()
    {
        var settings = new TestRecentFilesSettings { IncludeOfficeRecentFiles = true };
        using var source = new StubSource(new RecentPathFile(
            @"C:\Documents\Office.docx",
            DateTimeOffset.UtcNow));
        using var composite = new CompositeRecentFileSource(
        [
            new(source, () => settings.IncludeOfficeRecentFiles),
        ],
        settings);
        var changeCount = 0;
        composite.Changed += (_, _) => changeCount++;

        Assert.HasCount(1, composite.GetRecentFiles(CancellationToken.None));
        settings.IncludeOfficeRecentFiles = false;
        settings.RaiseSourcesChanged();

        Assert.IsEmpty(composite.GetRecentFiles(CancellationToken.None));
        Assert.AreEqual(1, changeCount);
    }

    private sealed class StubSource(params IRecentFile[] files) : IRecentFileSource
    {
        public event EventHandler? Changed;

        public IReadOnlyList<IRecentFile> GetRecentFiles(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return files;
        }

        public void RaiseChanged()
        {
            this.Changed?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose()
        {
        }
    }
}
