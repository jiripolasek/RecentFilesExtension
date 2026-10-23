// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using JPSoftworks.RecentFilesExtension.Model;

namespace JPSoftworks.RecentFilesExtension.UnitTests;

[TestClass]
public sealed class RecentFilePathTests
{
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
