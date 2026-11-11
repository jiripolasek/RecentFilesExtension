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
    [DataRow(@"C:report.docx")]
    [DataRow(@"\report.docx")]
    [DataRow("/report.docx")]
    [DataRow("file:///report.docx")]
    public void NormalizationRejectsTargetsThatDependOnTheProcessDirectoryOrDrive(string target)
    {
        Assert.IsFalse(RecentFilePath.TryNormalize(target, out var path));
        Assert.AreEqual(string.Empty, path);
        Assert.ThrowsExactly<ArgumentException>(() => new RecentPathFile(target, DateTimeOffset.UtcNow));
    }

    [TestMethod]
    [DataRow(@"\\server\share\a%41%2D%5F.txt")]
    [DataRow(@"\\?\UNC\server\share\a%41.txt")]
    [DataRow(@"\\?\C:\Documents\a%41.txt")]
    [DataRow(@"C:\Documents\a%41.txt")]
    public void NormalizationPreservesLiteralPercentSequencesInFileSystemPaths(string path)
    {
        Assert.IsTrue(RecentFilePath.TryNormalize(path, out var normalized));
        Assert.AreEqual(path, normalized);
        Assert.AreEqual(path, new RecentPathFile(path, DateTimeOffset.UtcNow).TargetPath);
    }

    [TestMethod]
    [DataRow("//server/share/a%41.txt", @"\\server\share\a%41.txt")]
    [DataRow("C:/Users/example/Documents/a%41.txt", @"C:\Users\example\Documents\a%41.txt")]
    [DataRow(@"C:/Users/example\Documents/a%41.txt", @"C:\Users\example\Documents\a%41.txt")]
    public void NormalizationConvertsSeparatorsWithoutDecodingPercentSequences(string path, string expected)
    {
        Assert.IsTrue(RecentFilePath.TryNormalize(path, out var normalized));
        Assert.AreEqual(expected, normalized);
        Assert.AreEqual(expected, new RecentPathFile(path, DateTimeOffset.UtcNow).TargetPath);
    }

    [TestMethod]
    public void ExplicitFileUrisAreStillDecoded()
    {
        Assert.IsTrue(RecentFilePath.TryNormalize("file://server/share/a%41%20file.txt", out var path));
        Assert.AreEqual(@"\\server\share\aA file.txt", path);
    }

    [TestMethod]
    [DataRow("https://example.invalid/Report%20one.docx?web=1#page", "Report one.docx")]
    [DataRow("https://example.invalid/Dockerfile?download=1", "Dockerfile")]
    [DataRow(@"\\server\share\a%41.txt", "a%41.txt")]
    public void FileNamesIgnoreUrlQueriesAndPreserveFileSystemNames(string path, string fileName)
    {
        Assert.AreEqual(fileName, RecentFilePath.GetFileName(path));
    }

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
            "https://contoso.sharepoint.com/documents/TONTRÄGER.xlsx?web=1#sheet");

        Assert.AreEqual(first, second);
    }

    [TestMethod]
    public void GetDeduplicationKeyPreservesOtherQueryParameters()
    {
        var key = RecentFilePath.GetDeduplicationKey(
            "https://contoso.sharepoint.com/Documents/report.xlsx?download=1&web=1");

        Assert.IsTrue(key.Contains("download=1", StringComparison.OrdinalIgnoreCase));
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