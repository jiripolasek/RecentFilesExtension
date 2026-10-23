// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using JPSoftworks.RecentFilesExtension.Model;
using JPSoftworks.RecentFilesExtension.Pages;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace JPSoftworks.RecentFilesExtension.UnitTests;

[TestClass]
public sealed class RecentFileListItemTests
{
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
