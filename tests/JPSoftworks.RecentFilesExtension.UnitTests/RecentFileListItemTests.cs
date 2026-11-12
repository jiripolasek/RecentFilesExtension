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
    [DataRow("image.png", false)]
    [DataRow("broken.png", false)]
    [DataRow("document.txt", false)]
    [DataRow("folder", false)]
    [DataRow("broken.png", true)]
    public async Task ThumbnailHasEnoughPixelsForGalleryAsync(string name, bool useShellIcons)
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

            var item = new RecentFileListItem(new RecentPathFile(path, DateTimeOffset.UtcNow));
            item.ApplyAppearance(new TestRecentFilesSettings { ViewMode = "gallery", UseShellIcons = useShellIcons });
            var icon = item.Icon;
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
    [DataRow("gallery", false, false)]
    [DataRow("grid", true, false)]
    [DataRow("gallery", false, true)]
    public async Task LargePhotoPreviewsUse256PixelThumbnailsAndListUsesShellIcons(string viewMode, bool usePreview, bool extendedPath)
    {
        var directory = Directory.CreateTempSubdirectory("RecentFileGallery-");
        try
        {
            var path = Path.Combine(directory.FullName, "photo #1 %20 📝.PNG");
            using (var image = new Bitmap(1024, 768))
            {
                using var graphics = Graphics.FromImage(image);
                graphics.Clear(Color.CornflowerBlue);
                image.Save(path, ImageFormat.Png);
            }

            var target = extendedPath ? @"\\?\" + path : path;
            var file = new RecentShortcutFile(new FileInfo(Path.Combine(directory.FullName, "photo.lnk")), "photo", target, false);
            var item = new RecentFileListItem(file);
            var settings = new TestRecentFilesSettings { ViewMode = viewMode, UseShellIcons = usePreview };
            var support = usePreview ? ShellIconSupport.Unknown : ShellIconSupport.Supported;
            item.ApplyAppearance(settings, support);

            using var stream = await item.Icon!.Light.Data.OpenReadAsync();
            var decoder = await BitmapDecoder.CreateAsync(stream);
            Assert.AreEqual(256u, decoder.PixelWidth);
            Assert.AreEqual(192u, decoder.PixelHeight);

            settings.ViewMode = "list";
            item.ApplyAppearance(settings, support);
            Assert.AreEqual($"|ShellItemIcon|v1;{target.Length}:{target}", item.Icon!.Light.Icon);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [TestMethod]
    [DataRow("directory")]
    [DataRow("offline")]
    [DataRow("missing")]
    public void GalleryKeepsShellIconsForImagesThatCannotBeOpenedLocally(string kind)
    {
        var directory = Directory.CreateTempSubdirectory("RecentFileShellIcon-");
        try
        {
            var path = Path.Combine(directory.FullName, "album.jpg");
            if (kind == "directory")
            {
                Directory.CreateDirectory(path);
            }
            else if (kind == "offline")
            {
                File.WriteAllBytes(path, []);
                File.SetAttributes(path, FileAttributes.Offline);
            }

            var item = new RecentFileListItem(new RecentPathFile(path, DateTimeOffset.UnixEpoch));
            item.ApplyAppearance(new TestRecentFilesSettings { ViewMode = "gallery" }, ShellIconSupport.Supported);
            Assert.AreEqual($"|JumboShellItemIcon|v1;{path.Length}:{path}", item.Icon!.Light.Icon);
            Assert.IsNull(item.Icon.Light.Data);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [TestMethod]
    [DataRow("vector.svg", true)]
    [DataRow("vector #1 %20.svg", false)]
    public void GalleryUsesTheHostVectorRendererForSafeSvgPaths(string name, bool usePath)
    {
        var directory = Directory.CreateTempSubdirectory("RecentFileSvg-");
        try
        {
            var path = Path.Combine(directory.FullName, name);
            File.WriteAllText(path, """<svg xmlns="http://www.w3.org/2000/svg" width="50" height="50"/>""");
            var item = new RecentFileListItem(new RecentPathFile(path, DateTimeOffset.UnixEpoch));
            item.ApplyAppearance(new TestRecentFilesSettings { ViewMode = "gallery" }, ShellIconSupport.Supported);
            Assert.AreEqual(usePath, item.Icon!.Light.Data is null);
            if (usePath)
            {
                Assert.AreEqual(path, item.Icon.Light.Icon);
            }
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [TestMethod]
    public void ReapplyingUnchangedAppearanceDoesNotAllocate()
    {
        var file = new RecentPathFile("https://example.invalid/report.pdf", DateTimeOffset.UnixEpoch);
        var row = new RecentFileListItem(file);
        var settings = new TestRecentFilesSettings { ShowFileTypeTags = true, ShowDetails = true };
        row.ApplyAppearance(settings);
        row.ApplyAppearance(settings);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++)
        {
            row.ApplyAppearance(settings);
        }

        Assert.AreEqual(before, GC.GetAllocatedBytesForCurrentThread());
    }

    [TestMethod]
    public async Task CompatibilityTriesTheTargetsShellIconBeforeTheShortcutThumbnail()
    {
        var directory = Directory.CreateTempSubdirectory("RecentFileShortcutIcon-");
        try
        {
            var target = Path.Combine(directory.FullName, "removed.png");
            var link = Path.Combine(directory.FullName, "image.lnk");
            File.WriteAllBytes(target, []);
            ShellLinkHelperTests.CreateShortcut(target, link, useIdList: false);
            File.Delete(target);

            var file = new RecentShortcutFile(new FileInfo(link), "image", target, false);
            var row = new RecentFileListItem(file);
            row.ApplyAppearance(new TestRecentFilesSettings(), ShellIconSupport.Unsupported);
            using var expected = await ThumbnailHelper.GetThumbnail(target, jumbo: true);
            Assert.IsNotNull(expected);
            using var actual = await row.Icon!.Light.Data.OpenReadAsync();
            var targetIcon = await BitmapDecoder.CreateAsync(expected);
            var rowIcon = await BitmapDecoder.CreateAsync(actual);
            Assert.AreEqual(targetIcon.PixelWidth, rowIcon.PixelWidth);
            Assert.AreEqual(targetIcon.PixelHeight, rowIcon.PixelHeight);
            CollectionAssert.AreEqual((await targetIcon.GetPixelDataAsync()).DetachPixelData(),
                (await rowIcon.GetPixelDataAsync()).DetachPixelData());
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CompatibilityReadFallsBackToAShellIcon(bool networkShortcut)
    {
        var directory = Directory.CreateTempSubdirectory("RecentFileFallback-");
        try
        {
            var path = Path.Combine(directory.FullName, "file.png");
            var link = Path.Combine(directory.FullName, "file.lnk");
            IRecentFile file;
            if (networkShortcut)
            {
                var target = @"\\127.0.0.1\RecentFilesMissing$\file.txt";
                ShellLinkHelperTests.CreateShortcut(target, link, useIdList: false);
                file = new RecentShortcutFile(new FileInfo(link), "file", target, false);
            }
            else
            {
                File.WriteAllText(path, "Recent file");
                file = new RecentPathFile(path, DateTimeOffset.UnixEpoch);
                File.Delete(path);
            }

            var item = new RecentFileListItem(file);
            item.ApplyAppearance(new TestRecentFilesSettings(), ShellIconSupport.Unsupported);
            using var stream = await item.Icon!.Light.Data.OpenReadAsync();
            var decoder = await BitmapDecoder.CreateAsync(stream);
            Assert.IsGreaterThan(0u, decoder.PixelWidth);
        }
        finally
        {
            directory.Delete(recursive: true);
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
        var item = new RecentFileListItem(recentFile);
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
        var item = new RecentFileListItem(recentFile);

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
            var item = new RecentFileListItem(recentFile);

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
            var item = new RecentFileListItem(recentFile);
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
        var item = new RecentFileListItem(recentFile);

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
        var item = new RecentFileListItem(recentFile);
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
    [DataRow(false)]
    [DataRow(true)]
    public void UsesFolderIconForRemoteSharePointFolder(bool useShellIcons)
    {
        var recentFolder = new RecentPathFile(
            "https://contoso.sharepoint.com/Documents/Reports/",
            DateTimeOffset.UtcNow);

        var item = new RecentFileListItem(recentFolder);
        item.ApplyAppearance(new TestRecentFilesSettings { UseShellIcons = useShellIcons });

        var icon = Assert.IsInstanceOfType<IconInfo>(item.Icon);
        Assert.AreEqual("\uE838", icon.Light.Icon);
        Assert.IsNull(icon.Light.Data);
    }

    [TestMethod]
    public void MenuAndDetailsAreMaterializedOnFirstHostRead()
    {
        var file = new CountingFile();
        var sharedCommands = new Lazy<Microsoft.CommandPalette.Extensions.IContextItem[]>(() => [new Separator()]);
        var item = new RecentFileListItem(file, sharedCommands);
        var settings = new TestRecentFilesSettings { ShowDetails = true };
        item.ApplyAppearance(settings);
        Assert.AreEqual(0, file.FileSystemReads);
        Assert.AreEqual(0, file.TimestampReads);
        Assert.IsFalse(sharedCommands.IsValueCreated);

        var menu = item.MoreCommands;
        Assert.HasCount(4, menu);
        Assert.AreSame(sharedCommands.Value[0], menu[^1]);
        Assert.AreSame(menu, item.MoreCommands);
        Assert.AreEqual(1, file.FileSystemReads);
        Assert.AreEqual(0, file.TimestampReads);

        var details = item.Details;
        Assert.IsNotNull(details);
        Assert.AreSame(details, item.Details);
        Assert.AreEqual(1, file.TimestampReads);
        settings.ShowDetails = false;
        item.ApplyAppearance(settings);
        Assert.IsNull(item.Details);
    }

    [TestMethod]
    public async Task DefaultThumbnailsOpenOnDemandAndThePreviewReturnsOnlyShellRequests()
    {
        var directory = Directory.CreateTempSubdirectory("RecentFileLazyIcon-");
        try
        {
            var path = Path.Combine(directory.FullName, "draft #23; résumé 📝.txt");
            var item = new RecentFileListItem(new RecentPathFile(path, DateTimeOffset.UnixEpoch));
            var original = item.Icon!.Light.Data;
            Assert.IsNotNull(original);
            File.WriteAllText(path, "Created after the icon was requested.");
            using var firstRead = await original.OpenReadAsync();
            var decoder = await BitmapDecoder.CreateAsync(firstRead);
            Assert.AreEqual(48u, Math.Max(decoder.PixelWidth, decoder.PixelHeight));
            using var secondRead = await original.OpenReadAsync();
            Assert.AreEqual(0ul, secondRead.Position);

            var settings = new TestRecentFilesSettings { UseShellIcons = true };
            item.ApplyAppearance(settings);
            Assert.AreEqual($"|ShellItemIcon|v1;{path.Length}:{path}", item.Icon!.Light.Icon);
            Assert.IsNull(item.Icon.Light.Data);
            settings.ViewMode = "gallery";
            item.ApplyAppearance(settings);
            Assert.AreEqual($"|JumboShellItemIcon|v1;{path.Length}:{path}", item.Icon!.Light.Icon);
            Assert.IsNull(item.Icon.Light.Data);
            settings.UseShellIcons = false;
            item.ApplyAppearance(settings);
            Assert.IsNotNull(item.Icon!.Light.Data);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [TestMethod]
    public void PreviewKeepsNetworkTargetsAndTheirLocalShortcutsOutOfShellRequests()
    {
        var file = new RecentShortcutFile(new FileInfo(@"C:\Recent\report.lnk"), "report", @"\\server\share\report.txt", isDirectory: false);
        var item = new RecentFileListItem(file);
        item.ApplyAppearance(new TestRecentFilesSettings { UseShellIcons = true });
        Assert.AreSame(Icons.Document, item.Icon);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void UsesDirectExecutableIconForRemoteDocument(bool useShellIcons)
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
            item.ApplyAppearance(new TestRecentFilesSettings { UseShellIcons = useShellIcons });

            var icon = Assert.IsInstanceOfType<IconInfo>(item.Icon);
            Assert.AreEqual(Path.ChangeExtension(executablePath, ".exe"), icon.Light.Icon);
            Assert.IsNull(icon.Light.Data);
        }
        finally
        {
            Directory.Delete(folderPath, recursive: true);
        }
    }

    private sealed class CountingFile : IRecentFile
    {
        internal int FileSystemReads { get; private set; }
        internal int TimestampReads { get; private set; }
        public string FullPath => this.TargetPath;
        public string DisplayName => "report.txt";
        public string TargetPath => @"C:\report.txt";
        public string? IconPath => null;

        public bool IsFileSystemPath
        {
            get
            {
                this.FileSystemReads++;
                return true;
            }
        }

        public DateTimeOffset LastUsedTimeUtc
        {
            get
            {
                this.TimestampReads++;
                return DateTimeOffset.UnixEpoch;
            }
        }

        public bool IsDirectory() => false;
    }
}
