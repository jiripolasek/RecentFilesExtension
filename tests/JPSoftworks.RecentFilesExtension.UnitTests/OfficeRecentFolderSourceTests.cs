// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using JPSoftworks.RecentFilesExtension.Sources;

namespace JPSoftworks.RecentFilesExtension.UnitTests;

[TestClass]
public sealed class OfficeRecentFolderSourceTests
{
    [TestMethod]
    public void GetRecentFilesReadsInternetShortcutTimestampAndUsesDesktopOfficeProtocol()
    {
        var folderPath = Path.Combine(Path.GetTempPath(), $"OfficeRecent-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folderPath);
        try
        {
            var shortcutPath = Path.Combine(folderPath, "Quarterly report.xlsx.url");
            File.WriteAllLines(
                shortcutPath,
                [
                    "[InternetShortcut]",
                    "URL=https://contoso.sharepoint.com/Documents/Quarterly%20report.xlsx?web=1",
                ]);
            var expectedTimestamp = new DateTime(2026, 8, 16, 7, 2, 18, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(shortcutPath, expectedTimestamp);

            using var source = new OfficeRecentFolderSource(folderPath);
            var files = source.GetRecentFiles(CancellationToken.None);

            Assert.HasCount(1, files);
            Assert.AreEqual(
                "https://contoso.sharepoint.com/Documents/Quarterly%20report.xlsx?web=1",
                files[0].TargetPath);
            Assert.AreEqual(
                "ms-excel:ofe|u|https://contoso.sharepoint.com/Documents/Quarterly%20report.xlsx",
                files[0].FullPath);
            Assert.AreEqual(new DateTimeOffset(expectedTimestamp), files[0].LastUsedTimeUtc);
        }
        finally
        {
            Directory.Delete(folderPath, recursive: true);
        }
    }

    [TestMethod]
    public void GetRecentFilesKeepsSharePointFolderAsBrowserUrl()
    {
        var folderPath = Path.Combine(Path.GetTempPath(), $"OfficeRecent-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folderPath);
        try
        {
            const string target = "https://contoso.sharepoint.com/Documents/Reports/";
            File.WriteAllLines(
                Path.Combine(folderPath, "Reports on contoso.sharepoint.com.url"),
                [
                    "[InternetShortcut]",
                    $"URL={target}",
                ]);

            using var source = new OfficeRecentFolderSource(folderPath);
            var files = source.GetRecentFiles(CancellationToken.None);

            Assert.HasCount(1, files);
            Assert.AreEqual(target, files[0].TargetPath);
            Assert.AreEqual(target, files[0].FullPath);
            Assert.AreEqual("Reports", files[0].DisplayName);
            Assert.IsNull(files[0].IconPath);
            Assert.IsTrue(files[0].IsDirectory());
        }
        finally
        {
            Directory.Delete(folderPath, recursive: true);
        }
    }

    [TestMethod]
    public void TryParseInternetShortcutRequiresInternetShortcutSection()
    {
        var parsed = OfficeRecentFolderSource.TryParseInternetShortcut(
        [
            "[Other]",
            "URL=https://contoso.sharepoint.com/Documents/report.xlsx",
        ],
        out _);

        Assert.IsFalse(parsed);
    }

    [TestMethod]
    public void TryGetDocumentApplicationMapsExcelWorkbook()
    {
        var found = OfficeApplications.TryGetDocumentApplication(
            "https://contoso.sharepoint.com/Documents/report.xlsx",
            out var executableName,
            out var uriScheme);

        Assert.IsTrue(found);
        Assert.AreEqual("EXCEL.EXE", executableName);
        Assert.AreEqual("ms-excel", uriScheme);
    }

}
