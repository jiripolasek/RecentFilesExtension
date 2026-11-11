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
            "https://contoso.sharepoint.com/Documents/Report.xlsx?token=Ab&web=1#sheet",
            out var launchUri);

        Assert.IsTrue(created);
        Assert.AreEqual(
            "ms-excel:ofe|u|https://contoso.sharepoint.com/Documents/Report.xlsx?token=Ab",
            launchUri);
    }

    [TestMethod]
    public void CreateRecentFileCarriesOfficeActivationAndIconMetadata()
    {
        var directory = Directory.CreateTempSubdirectory("OfficeActivation-");
        try
        {
            var applicationPath = Path.Combine(directory.FullName, "EXCEL.EXE");
            File.WriteAllText(applicationPath, string.Empty);
            var recentFile = OfficeApplications.CreateRecentFile(
                "https://contoso.sharepoint.com/Documents/report.xlsx?web=1",
                DateTimeOffset.UtcNow,
                applicationPath,
                _ => throw new InvalidOperationException("The existing application path should be used."));

            Assert.AreEqual(
                "ms-excel:ofe|u|https://contoso.sharepoint.com/Documents/report.xlsx",
                recentFile.FullPath);
            Assert.AreEqual(applicationPath, recentFile.IconPath);
            Assert.AreEqual(
                "https://contoso.sharepoint.com/Documents/report.xlsx?web=1",
                recentFile.TargetPath);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void CloudDocumentsUseDesktopOfficeOnlyWhenAnExecutableIsFound(bool installed)
    {
        const string target = "https://contoso.sharepoint.com/Documents/report.xlsx?web=1";
        var staleApplicationPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "EXCEL.EXE");
        const string resolvedApplicationPath = @"C:\Office\EXCEL.EXE";
        var recentFile = OfficeApplications.CreateRecentFile(
            target, DateTimeOffset.UtcNow, staleApplicationPath, executableName =>
            {
                Assert.AreEqual("EXCEL.EXE", executableName);
                return installed ? resolvedApplicationPath : null;
            });

        Assert.AreEqual(
            installed ? "ms-excel:ofe|u|https://contoso.sharepoint.com/Documents/report.xlsx" : target,
            recentFile.FullPath);
        Assert.AreEqual(installed ? resolvedApplicationPath : null, recentFile.IconPath);
        Assert.AreEqual(target, recentFile.TargetPath);
    }
}