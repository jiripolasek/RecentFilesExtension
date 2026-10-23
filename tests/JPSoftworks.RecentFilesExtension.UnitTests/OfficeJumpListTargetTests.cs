// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using JPSoftworks.RecentFilesExtension.Helpers;
using JPSoftworks.RecentFilesExtension.Sources;

namespace JPSoftworks.RecentFilesExtension.UnitTests;

[TestClass]
public sealed class OfficeJumpListTargetTests
{
    [TestMethod]
    public void TryGetTargetUsesCloudDocumentArgumentInsteadOfOfficeExecutable()
    {
        var data = new ShellLinkTargetData(
            @"C:\Program Files\Microsoft Office\root\Office16\EXCEL.EXE",
            "https://contoso.sharepoint.com/Documents/Quarterly%20report.xlsx",
            "https://contoso.sharepoint.com/Documents/Quarterly report.xlsx");

        var parsed = OfficeJumpListTarget.TryGetTarget(data, out var target);

        Assert.IsTrue(parsed);
        Assert.AreEqual(
            "https://contoso.sharepoint.com/Documents/Quarterly%20report.xlsx",
            target);
    }

    [TestMethod]
    public void TryGetTargetReadsQuotedPathFromCommandArguments()
    {
        var data = new ShellLinkTargetData(
            @"C:\Program Files\Microsoft Office\root\Office16\WINWORD.EXE",
            "/n \"C:\\Documents\\Quarterly report.docx\"",
            string.Empty);

        Assert.IsTrue(OfficeJumpListTarget.TryGetTarget(data, out var target));
        Assert.AreEqual(@"C:\Documents\Quarterly report.docx", target);
    }

    [TestMethod]
    public void TryGetTargetRejectsOfficeExecutableWithoutDocument()
    {
        var data = new ShellLinkTargetData(
            @"C:\Program Files\Microsoft Office\root\Office16\POWERPNT.EXE",
            string.Empty,
            "Presentation application");

        Assert.IsFalse(OfficeJumpListTarget.TryGetTarget(data, out _));
    }

    [TestMethod]
    public void TryGetApplicationPathPreservesOfficeIconSource()
    {
        var data = new ShellLinkTargetData(
            @"C:\Program Files\Microsoft Office\root\Office16\EXCEL.EXE",
            "https://contoso.sharepoint.com/Documents/report.xlsx",
            string.Empty);

        Assert.IsTrue(OfficeJumpListTarget.TryGetApplicationPath(data, out var applicationPath));
        Assert.AreEqual(
            @"C:\Program Files\Microsoft Office\root\Office16\EXCEL.EXE",
            applicationPath);
    }

    [TestMethod]
    public void TryCreateDesktopLaunchUriUsesExcelEditProtocolAndRemovesWebViewFlag()
    {
        var created = OfficeJumpListTarget.TryCreateDesktopLaunchUri(
            @"C:\Program Files\Microsoft Office\root\Office16\EXCEL.EXE",
            "https://contoso.sharepoint.com/Documents/report.xlsx?web=1#sheet",
            out var launchUri);

        Assert.IsTrue(created);
        Assert.AreEqual(
            "ms-excel:ofe|u|https://contoso.sharepoint.com/Documents/report.xlsx",
            launchUri);
    }

    [TestMethod]
    public void CreateRecentFileCarriesOfficeActivationAndIconMetadata()
    {
        const string applicationPath = @"C:\Program Files\Microsoft Office\root\Office16\EXCEL.EXE";
        var recentFile = OfficeApplications.CreateRecentFile(
            "https://contoso.sharepoint.com/Documents/report.xlsx?web=1",
            DateTimeOffset.UtcNow,
            applicationPath);

        Assert.AreEqual(
            "ms-excel:ofe|u|https://contoso.sharepoint.com/Documents/report.xlsx",
            recentFile.FullPath);
        Assert.AreEqual(applicationPath, recentFile.IconPath);
        Assert.AreEqual(
            "https://contoso.sharepoint.com/Documents/report.xlsx?web=1",
            recentFile.TargetPath);
    }
}
