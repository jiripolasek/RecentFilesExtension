// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using JPSoftworks.RecentFilesExtension.Model;
using JPSoftworks.RecentFilesExtension.Sources;
using Microsoft.Win32;

namespace JPSoftworks.RecentFilesExtension.UnitTests;

[TestClass]
public sealed class RegistryRecentFileSourceTests
{
    [TestMethod]
    public void OpenSaveMruDatesOnlyNewestEntryAndPreservesKnownRecency()
    {
        var registryPath = $@"Software\RecentFilesExtension.Tests.{Guid.NewGuid():N}";
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(registryPath);
            key.SetValue("a", @"C:\Documents\New.txt");
            key.SetValue("b", @"C:\Documents\Old.txt");
            key.SetValue("c", @"C:\Documents\HistoryOnly.txt");
            key.SetValue("d", @"C:\Documents\OlderHistoryOnly.txt");
            key.SetValue("MRUList", "abcd");
            var keyTime = NativeRegistryMethods.GetLastWriteTimeUtc(key);
            Assert.IsNotNull(keyTime);

            var history = new List<IRecentFile>();
            OpenSaveMruSource.ReadMruKey(key, isPidl: false, history);

            Assert.HasCount(4, history);
            Assert.AreEqual(keyTime, history[0].LastUsedTimeUtc);
            Assert.IsTrue(history.Skip(1).All(static file => file.LastUsedTimeUtc == DateTimeOffset.MinValue));
            Assert.AreEqual(keyTime, NativeRegistryMethods.GetLastWriteTimeUtc(key));

            var now = DateTimeOffset.UtcNow;
            var oldFile = new RecentPathFile(@"C:\Documents\Old.txt", now.AddDays(-14));
            var yesterday = new RecentPathFile(@"C:\Documents\Yesterday.pdf", now.AddDays(-1));
            using var historySource = new StubSource([.. history]);
            using var knownSource = new StubSource(oldFile, yesterday);
            using var composite = new CompositeRecentFileSource(
            [
                new(historySource, static () => true),
                new(knownSource, static () => true),
            ]);

            var files = composite.GetRecentFiles(CancellationToken.None);

            CollectionAssert.AreEqual(
                new[]
                {
                    @"C:\Documents\New.txt",
                    yesterday.TargetPath,
                    oldFile.TargetPath,
                    @"C:\Documents\HistoryOnly.txt",
                    @"C:\Documents\OlderHistoryOnly.txt",
                },
                files.Select(static file => file.TargetPath).ToArray());
            Assert.AreSame(oldFile, files[2]);
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(registryPath, throwOnMissingSubKey: false);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void OfficeMruUsesKeyTimeOnlyForFirstItemWithoutTimestamp(bool firstItemHasTimestamp)
    {
        var registryPath = $@"Software\RecentFilesExtension.Tests.{Guid.NewGuid():N}";
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(registryPath);
            var firstItemTime = DateTimeOffset.UtcNow.AddDays(-1);
            var olderItemTime = firstItemTime.AddDays(-14);
            var firstMetadata = firstItemHasTimestamp
                ? $"[T{firstItemTime.UtcDateTime.ToFileTimeUtc():X16}]"
                : string.Empty;
            key.SetValue("Item 1", $@"[F00000000]{firstMetadata}*C:\Documents\First.docx");
            key.SetValue("Item 2", @"[F00000000]*C:\Documents\Unknown.docx");
            key.SetValue("Item 3", $@"[F00000000][T{olderItemTime.UtcDateTime.ToFileTimeUtc():X16}]*C:\Documents\Known.docx");
            var keyTime = NativeRegistryMethods.GetLastWriteTimeUtc(key);
            Assert.IsNotNull(keyTime);

            var files = new List<IRecentFile>();
            OfficeRecentFilesSource.ReadFileMru(key, files);

            Assert.HasCount(3, files);
            Assert.AreEqual(firstItemHasTimestamp ? firstItemTime : keyTime, files[0].LastUsedTimeUtc);
            Assert.AreEqual(DateTimeOffset.MinValue, files[1].LastUsedTimeUtc);
            Assert.AreEqual(olderItemTime, files[2].LastUsedTimeUtc);
            Assert.AreEqual(keyTime, NativeRegistryMethods.GetLastWriteTimeUtc(key));
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(registryPath, throwOnMissingSubKey: false);
        }
    }

    private sealed class StubSource(params IRecentFile[] files) : IRecentFileSource
    {
        public event EventHandler? Changed
        {
            add { }
            remove { }
        }

        public IReadOnlyList<IRecentFile> GetRecentFiles(CancellationToken cancellationToken)
        {
            return files;
        }

        public void Dispose()
        {
        }
    }
}