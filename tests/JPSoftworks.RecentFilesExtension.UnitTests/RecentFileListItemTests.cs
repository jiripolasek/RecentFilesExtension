// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;
using JPSoftworks.RecentFilesExtension.Helpers;
using JPSoftworks.RecentFilesExtension.Model;
using JPSoftworks.RecentFilesExtension.Pages;
using JPSoftworks.RecentFilesExtension.Resources;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Windows.Graphics.Imaging;

namespace JPSoftworks.RecentFilesExtension.UnitTests;

[TestClass]
public sealed class RecentFileListItemTests
{
    [TestMethod]
    [DataRow("image.png")]
    [DataRow("broken.png")]
    [DataRow("document.txt")]
    [DataRow("folder")]
    public async Task ThumbnailHasEnoughPixelsForGalleryAsync(string name)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"RecentFileThumbnail-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, name);
            if (name == "image.png")
            {
                using var bitmap = new Bitmap(1024, 768);
                using var graphics = Graphics.FromImage(bitmap);
                graphics.Clear(Color.CornflowerBlue);
                bitmap.Save(path, ImageFormat.Png);
            }
            else if (name == "folder")
            {
                Directory.CreateDirectory(path);
            }
            else
            {
                File.WriteAllText(path, "Thumbnail resolution test");
            }

            var icon = await RecentFileListItem.TryGetThumbnailIconAsync(path);
            Assert.IsNotNull(icon);
            Assert.IsNotNull(icon.Light.Data);
            using var stream = await icon.Light.Data.OpenReadAsync();
            var decoder = await BitmapDecoder.CreateAsync(stream);
            Assert.IsGreaterThanOrEqualTo(256u, Math.Max(decoder.PixelWidth, decoder.PixelHeight),
                $"{name} returned only {decoder.PixelWidth} x {decoder.PixelHeight} pixels.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    [DataRow(@"C:\Documents\report.PDF", "report", "PDF")]
    [DataRow(@"C:\Documents\archive.tar.gz", "archive.tar", "GZ")]
    [DataRow(@"C:\Documents\README", "README", "File")]
    [DataRow(@"C:\Documents\.gitignore", ".gitignore", "File")]
    [DataRow("https://contoso.sharepoint.com/Documents/quarter%202.report.xlsx?web=1&key=a.b", "quarter 2.report", "XLSX")]
    [DataRow("https://example.com/", "example.com", "File")]
    [DataRow("https://contoso.sharepoint.com/Documents/Reports.2026/", "Reports.2026", "Folder")]
    public void AppearanceHidesOnlyTheFileExtension(string path, string expectedTitle, string expectedTag)
    {
        var recentFile = new RecentPathFile(path, DateTimeOffset.UtcNow);
        var item = new RecentFileListItem(recentFile, new CancellationToken(canceled: true));
        var command = item.Command;

        item.ApplyAppearance(new TestRecentFilesSettings { HideFileExtensions = true, ShowFileTypeTags = true });

        Assert.AreEqual(expectedTitle, item.Title);
        Assert.AreEqual(expectedTag, item.Tags.Single().Text);
        Assert.AreEqual(path, item.Subtitle);
        Assert.AreEqual(path, recentFile.TargetPath);
        Assert.AreEqual(path, recentFile.FullPath);
        Assert.AreSame(command, item.Command);
    }

    [TestMethod]
    public void AppearancePreservesShortcutDisplayNamesThatDoNotEndWithTheTargetExtension()
    {
        var recentFile = new RecentShortcutFile(new FileInfo(@"C:\Recent\annual.lnk"), "Annual.plan", @"C:\Documents\report.docx", isDirectory: false);
        var item = new RecentFileListItem(recentFile, new CancellationToken(canceled: true));

        item.ApplyAppearance(new TestRecentFilesSettings { HideFileExtensions = true });

        Assert.AreEqual("Annual.plan", item.Title);
        Assert.AreEqual(@"C:\Documents\report.docx", item.Subtitle);
    }

    [TestMethod]
    public void AppearanceUsesCachedDirectoryMetadataAfterTheFolderDisappears()
    {
        var folderPath = Path.Combine(Path.GetTempPath(), $"RecentFileAppearance-{Guid.NewGuid():N}.2026");
        Directory.CreateDirectory(folderPath);
        try
        {
            var recentFile = new RecentPathFile(folderPath, DateTimeOffset.UtcNow);
            Directory.Delete(folderPath);
            var item = new RecentFileListItem(recentFile, new CancellationToken(canceled: true));

            item.ApplyAppearance(new TestRecentFilesSettings { HideFileExtensions = true, ShowFileTypeTags = true });

            Assert.AreEqual(Path.GetFileName(folderPath), item.Title);
            Assert.AreEqual(Strings.FileType_Folder, item.Tags.Single().Text);
            Assert.IsNotNull(item.Command);
            Assert.AreEqual(Strings.Command_OpenFolder, item.Command.Name);
        }
        finally
        {
            if (Directory.Exists(folderPath))
            {
                Directory.Delete(folderPath);
            }
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public unsafe void ShortcutReadsSavedDirectoryMetadataWithoutTheTarget(bool isDirectory)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"RecentFileMetadata-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var targetPath = Path.Combine(directory, "Reports.2026");
            if (isDirectory)
            {
                Directory.CreateDirectory(targetPath);
            }
            else
            {
                File.WriteAllText(targetPath, string.Empty);
            }

            var shortcutPath = Path.Combine(directory, "Reports.lnk");
            using (var link = ShellLinkComObject.Create())
            {
                fixed (char* target = targetPath)
                fixed (char* shortcut = shortcutPath)
                {
                    Marshal.ThrowExceptionForHR(link.ShellLink.SetPath(target));
                    Marshal.ThrowExceptionForHR(link.PersistFile.Save(shortcut, 1));
                }
            }

            if (isDirectory)
            {
                Directory.Delete(targetPath);
            }
            else
            {
                File.Delete(targetPath);
            }

            using var helper = new ShellLinkHelper();
            Assert.AreEqual(targetPath, helper.LoadTargetPath(shortcutPath));
            Assert.AreEqual(isDirectory, helper.IsDirectory);
            var recentFile = new RecentShortcutFile(new FileInfo(shortcutPath), "Reports.2026", targetPath, helper.IsDirectory);
            var item = new RecentFileListItem(recentFile, new CancellationToken(canceled: true));
            item.ApplyAppearance(new TestRecentFilesSettings { HideFileExtensions = true, ShowFileTypeTags = true });

            Assert.AreEqual(isDirectory ? "Reports.2026" : "Reports", item.Title);
            Assert.AreEqual(isDirectory ? Strings.FileType_Folder : "2026", item.Tags.Single().Text);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public void SubtitleAndTagsAreIndependent(bool showType, bool showTags)
    {
        var recentFile = new RecentPathFile(@"C:\Documents\report.pdf", DateTimeOffset.UtcNow);
        var item = new RecentFileListItem(recentFile, new CancellationToken(canceled: true));

        item.ApplyAppearance(new TestRecentFilesSettings { ShowFileTypeInSubtitle = showType, ShowFileTypeTags = showTags });

        Assert.AreEqual("report.pdf", item.Title);
        Assert.AreEqual(showType ? $"PDF file · {recentFile.TargetPath}" : recentFile.TargetPath, item.Subtitle);
        Assert.AreEqual(showTags ? 1 : 0, item.Tags.Length);
        Assert.IsNull(item.Details);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void DetailsKeepTheFullNameAndCanBeDisabledAlongWithOtherAppearanceOptions(bool unknownLastUsed)
    {
        var lastUsed = unknownLastUsed ? DateTimeOffset.MinValue : new DateTimeOffset(2026, 10, 24, 12, 30, 0, TimeSpan.Zero);
        var recentFile = new RecentPathFile(@"C:\Documents\report.pdf", lastUsed);
        var item = new RecentFileListItem(recentFile, new CancellationToken(canceled: true));
        item.ApplyAppearance(new TestRecentFilesSettings
        {
            HideFileExtensions = true,
            ShowFileTypeInSubtitle = true,
            ShowFileTypeTags = true,
            ShowDetails = true,
        });

        var details = Assert.IsInstanceOfType<Details>(item.Details);
        Assert.AreEqual("report", item.Title);
        Assert.AreEqual("report.pdf", details.Title);
        CollectionAssert.AreEqual(
            new[] { Strings.Details_Path, Strings.Details_Type, Strings.Details_LastUsed },
            details.Metadata.Select(element => element.Key).ToArray());
        CollectionAssert.AreEqual(
            new[] { recentFile.TargetPath, "PDF file", unknownLastUsed ? Strings.Details_UnknownLastUsed : lastUsed.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) },
            details.Metadata.Select(element => Assert.IsInstanceOfType<DetailsLink>(element.Data).Text).ToArray());

        item.ApplyAppearance(new TestRecentFilesSettings());

        Assert.AreEqual("report.pdf", item.Title);
        Assert.AreEqual(recentFile.TargetPath, item.Subtitle);
        Assert.IsEmpty(item.Tags);
        Assert.IsNull(item.Details);
    }

    [TestMethod]
    public void ConstructorUsesFolderIconForRemoteSharePointFolder()
    {
        var recentFolder = new RecentPathFile(
            "https://contoso.sharepoint.com/Documents/Reports/",
            DateTimeOffset.UtcNow);

        var item = new RecentFileListItem(recentFolder);

        var icon = Assert.IsInstanceOfType<IconInfo>(item.Icon);
        Assert.AreEqual("\uE838", icon.Light.Icon);
        Assert.IsNull(icon.Light.Data);
    }

    [TestMethod]
    public void ConstructorUsesDirectExecutableIconForRemoteDocument()
    {
        var folderPath = Path.Combine(Path.GetTempPath(), $"RecentFileIcon-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folderPath);
        try
        {
            var executablePath = Path.Combine(folderPath, "EXCEL.EXE");
            File.WriteAllBytes(executablePath, []);
            var recentFile = new RecentPathFile(
                "https://contoso.sharepoint.com/Documents/report.xlsx",
                DateTimeOffset.UtcNow,
                "ms-excel:ofe|u|https://contoso.sharepoint.com/Documents/report.xlsx",
                executablePath);

            var item = new RecentFileListItem(recentFile);

            var icon = Assert.IsInstanceOfType<IconInfo>(item.Icon);
            Assert.AreEqual(Path.ChangeExtension(executablePath, ".exe"), icon.Light.Icon);
            Assert.IsNull(icon.Light.Data);
        }
        finally
        {
            Directory.Delete(folderPath, recursive: true);
        }
    }
}
