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
    public void GetRecentFilesReadsInternetShortcutTimestampAndUsesAnAvailableApplication()
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
            var expectedLaunchPath = OfficeApplications.TryFindExecutable("EXCEL.EXE", out _)
                ? "ms-excel:ofe|u|https://contoso.sharepoint.com/Documents/Quarterly%20report.xlsx"
                : files[0].TargetPath;
            Assert.AreEqual(expectedLaunchPath, files[0].FullPath);
            Assert.AreEqual(new DateTimeOffset(expectedTimestamp), files[0].LastUsedTimeUtc);
        }
        finally
        {
            Directory.Delete(folderPath, recursive: true);
        }
    }

    [TestMethod]
    [DataRow("files.example.invalid", 2)]
    [DataRow("contoso.sharepoint.com.other.invalid", 2)]
    [DataRow("contoso-my.sharepoint.com", 1)]
    [DataRow("onedrive.live.com", 1)]
    [DataRow("d.docs.live.net", 1)]
    public void FolderAndCompositeIgnoreFragmentsAndUseDocumentHostCaseRules(string host, int expected)
    {
        var directory = Directory.CreateTempSubdirectory("RecentFilesUrlIdentity-");
        try
        {
            var older = $"https://{host}/Résumé/download?id=Ab";
            var withoutFragment = $"https://{host}/Résumé/download?id=ab";
            var newer = withoutFragment + "#page=3";
            var duplicate = Path.Combine(directory.FullName, "without-fragment.url");
            File.WriteAllLines(duplicate, ["[InternetShortcut]", $"URL={withoutFragment}"]);
            File.SetLastWriteTimeUtc(duplicate, DateTime.UtcNow.AddDays(-2));
            var first = Path.Combine(directory.FullName, "older.url");
            File.WriteAllLines(first, ["[InternetShortcut]", $"URL={older}"]);
            File.SetLastWriteTimeUtc(first, DateTime.UtcNow.AddDays(-1));
            File.WriteAllLines(Path.Combine(directory.FullName, "newer.url"), ["[InternetShortcut]", $"URL={newer}"]);
            using var source = new CompositeRecentFileSource([new(() => new OfficeRecentFolderSource(directory.FullName), static () => true)]);

            var items = source.GetRecentFiles(CancellationToken.None);
            Assert.HasCount(expected, items);
            Assert.AreEqual(new Uri(newer).AbsoluteUri, items[0].TargetPath);
            if (expected == 2)
            {
                Assert.AreEqual(new Uri(older).AbsoluteUri, items[1].TargetPath);
            }
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [TestMethod]
    public void InternetShortcutCacheReusesParsesAndInvalidatesOnChangeOrRemoval()
    {
        var directory = Directory.CreateTempSubdirectory("OfficeRecentUrl-");
        var path = Path.Combine(directory.FullName, "report.url");
        try
        {
            var stamp = DateTime.UtcNow.AddDays(-1);
            WriteTarget("https://example.invalid/first.pdf", stamp);
            using var source = new OfficeRecentFolderSource(directory.FullName);
            using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                Assert.IsEmpty(source.GetRecentFiles(CancellationToken.None));
            }
            Assert.AreEqual("https://example.invalid/first.pdf", ReadTarget());
            using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                Assert.AreEqual("https://example.invalid/first.pdf", ReadTarget());
            }

            stamp = stamp.AddMinutes(1);
            WriteTarget("https://example.invalid/second.pdf", stamp);
            Assert.AreEqual("https://example.invalid/second.pdf", ReadTarget());
            File.Delete(path);
            Assert.IsEmpty(source.GetRecentFiles(CancellationToken.None));
            WriteTarget("https://example.invalid/third.pdf", stamp);
            Assert.AreEqual("https://example.invalid/third.pdf", ReadTarget());

            string ReadTarget() => source.GetRecentFiles(CancellationToken.None).Single().TargetPath;
        }
        finally
        {
            directory.Delete(recursive: true);
        }

        void WriteTarget(string target, DateTime timestamp)
        {
            File.WriteAllLines(path, ["[InternetShortcut]", $"URL={target}"]);
            File.SetLastWriteTimeUtc(path, timestamp);
        }
    }

    [TestMethod]
    [DoNotParallelize]
    public void ACorruptShortcutIsLoggedWithoutDiscardingOtherOfficeEntries()
    {
        using var logs = new TestLogCapture();
        var directory = Directory.CreateTempSubdirectory("OfficeRecentCorrupt-");
        try
        {
            const string target = "https://example.invalid/report.pdf";
            File.WriteAllText(Path.Combine(directory.FullName, "broken.lnk"), "not a shell link");
            File.WriteAllLines(Path.Combine(directory.FullName, "valid.url"), ["[InternetShortcut]", $"URL={target}"]);
            using var source = new OfficeRecentFolderSource(directory.FullName);

            Assert.AreEqual(target, source.GetRecentFiles(CancellationToken.None).Single().TargetPath);
            const string message = "Failed to retrieve a shell link";
            logs.AssertLogged(message);
            Assert.IsFalse(logs.Messages.Any(entry => entry.Message.Contains("broken.lnk", StringComparison.Ordinal)));
            source.GetRecentFiles(CancellationToken.None);
            Assert.AreEqual(1, logs.Messages.Count(entry => entry.Message.Contains(message, StringComparison.Ordinal)));
            var shortcutPath = Path.Combine(directory.FullName, "broken.lnk");
            File.SetLastWriteTimeUtc(shortcutPath, File.GetLastWriteTimeUtc(shortcutPath).AddSeconds(1));
            source.GetRecentFiles(CancellationToken.None);
            Assert.IsTrue(SpinWait.SpinUntil(
                () => logs.Messages.Count(entry => entry.Message.Contains(message, StringComparison.Ordinal)) >= 2,
                TimeSpan.FromSeconds(5)));
            Assert.AreEqual(2, logs.Messages.Count(entry => entry.Message.Contains(message, StringComparison.Ordinal)));
        }
        finally
        {
            directory.Delete(recursive: true);
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