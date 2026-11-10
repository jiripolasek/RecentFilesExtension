// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using JPSoftworks.RecentFilesExtension.Helpers;
using JPSoftworks.RecentFilesExtension.Model;

namespace JPSoftworks.RecentFilesExtension.UnitTests;

[TestClass]
public sealed class RecentFilePathTests
{
    [TestMethod]
    [DataRow(@"\\server\share\file.txt")]
    [DataRow(@"\\?\UNC\server\share\file.txt")]
    [DataRow("//server/share/file.txt")]
    public void NetworkPathDetectionRecognizesUncPathsWithoutAccessingThem(string path)
    {
        Assert.IsTrue(path.IsNetworkPath());
    }

    [TestMethod]
    public void ExtendedLocalPathsUseTheLocalDriveType()
    {
        var path = Path.GetTempPath();
        var expected = new DriveInfo(Path.GetPathRoot(path)!).DriveType == DriveType.Network;

        Assert.AreEqual(expected, path.IsNetworkPath());
        Assert.AreEqual(expected, $@"\\?\{path}".IsNetworkPath());
        Assert.IsFalse("relative.txt".IsNetworkPath());
    }

    [TestMethod]
    public void GetDeduplicationKeyCanonicalizesSharePointEncodingAndWebFlag()
    {
        var first = RecentFilePath.GetDeduplicationKey(
            "https://contoso.sharepoint.com/Documents/tontr%C3%A4ger.xlsx");
        var second = RecentFilePath.GetDeduplicationKey(
            "https://contoso.sharepoint.com/Documents/tonträger.xlsx?web=1#sheet");

        Assert.AreEqual(first, second, ignoreCase: true);
    }

    [TestMethod]
    public void GetDeduplicationKeyPreservesOtherQueryParameters()
    {
        var key = RecentFilePath.GetDeduplicationKey(
            "https://contoso.sharepoint.com/Documents/report.xlsx?download=1&web=1");

        StringAssert.Contains(key, "download=1");
        Assert.IsFalse(key.Contains("web=1", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void IsSharePointFolderRecognizesTrailingSlashFolderUrl()
    {
        Assert.IsTrue(RecentFilePath.IsSharePointFolder(
            "https://contoso.sharepoint.com/Documents/Reports/"));
        Assert.IsFalse(RecentFilePath.IsSharePointFolder(
            "https://contoso.sharepoint.com/Documents/Reports/report.xlsx"));
        Assert.IsFalse(RecentFilePath.IsSharePointFolder(
            "https://contoso.example/Documents/Reports/"));
    }
}
