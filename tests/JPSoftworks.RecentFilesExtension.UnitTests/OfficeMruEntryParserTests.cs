// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using JPSoftworks.RecentFilesExtension.Sources;

namespace JPSoftworks.RecentFilesExtension.UnitTests;

[TestClass]
public sealed class OfficeMruEntryParserTests
{
    [TestMethod]
    public void TryParseReadsPathAndFileTime()
    {
        var parsed = OfficeMruEntryParser.TryParse(
            @"[F00000000][T01CFA783D052DEC0][O00000000]*C:\Documents\Quarterly report.xlsx",
            out var entry);

        Assert.IsTrue(parsed);
        Assert.AreEqual(@"C:\Documents\Quarterly report.xlsx", entry.Path);
        Assert.AreEqual(
            new DateTimeOffset(2014, 7, 24, 21, 11, 19, 468, TimeSpan.Zero),
            entry.LastUsedTimeUtc);
    }

    [TestMethod]
    public void TryParseAllowsCloudDocumentUris()
    {
        var parsed = OfficeMruEntryParser.TryParse(
            "[F00000000][T01CFA783D052DEC0]*https://contoso.sharepoint.com/docs/report.docx",
            out var entry);

        Assert.IsTrue(parsed);
        Assert.AreEqual("https://contoso.sharepoint.com/docs/report.docx", entry.Path);
    }

    [TestMethod]
    public void TryParseRejectsRelativePayload()
    {
        Assert.IsFalse(OfficeMruEntryParser.TryParse(
            "[F00000000]*report.docx",
            out _));
    }
}