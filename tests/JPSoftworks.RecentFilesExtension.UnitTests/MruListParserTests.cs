// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using JPSoftworks.RecentFilesExtension.Sources;

namespace JPSoftworks.RecentFilesExtension.UnitTests;

[TestClass]
public sealed class MruListParserTests
{
    [TestMethod]
    public void ParseMruListExPreservesOrderAndStopsAtTerminator()
    {
        byte[] data =
        [
            2, 0, 0, 0,
            0, 0, 0, 0,
            1, 0, 0, 0,
            255, 255, 255, 255,
            3, 0, 0, 0,
        ];

        int[] expected = [2, 0, 1];
        CollectionAssert.AreEqual(
            expected,
            MruListParser.ParseMruListEx(data).ToArray());
    }

    [TestMethod]
    public void ParseLegacyMruListRemovesDuplicateAndNullEntries()
    {
        string[] expected = ["b", "a", "c"];
        CollectionAssert.AreEqual(
            expected,
            MruListParser.ParseLegacyMruList("ba\0cb").ToArray());
    }

    [TestMethod]
    public void PidlValidationRequiresACompleteTerminatedList()
    {
        Assert.IsTrue(ShellItemIdListPathResolver.IsValidItemIdList(
            new byte[] { 4, 0, 1, 2, 0, 0 }));
        Assert.IsFalse(ShellItemIdListPathResolver.IsValidItemIdList(
            new byte[] { 8, 0, 1, 2, 0, 0 }));
    }
}