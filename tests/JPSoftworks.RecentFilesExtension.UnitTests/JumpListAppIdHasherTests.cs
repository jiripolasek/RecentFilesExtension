// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using JPSoftworks.RecentFilesExtension.Sources;

namespace JPSoftworks.RecentFilesExtension.UnitTests;

[TestClass]
public sealed class JumpListAppIdHasherTests
{
    [TestMethod]
    [DataRow("Microsoft.Office.EXCEL.EXE.15", "b8ab77100df80ab2")]
    [DataRow("Microsoft.Office.WINWORD.EXE.15", "fb3b0dbfee58fac8")]
    [DataRow("Microsoft.Windows.Explorer", "f01b4d95cf55d32a")]
    public void GetDestinationIdMatchesKnownWindowsIds(string appUserModelId, string expected)
    {
        Assert.AreEqual(expected, JumpListAppIdHasher.GetDestinationId(appUserModelId));
    }

    [TestMethod]
    public void GetDestinationIdIsCaseInsensitive()
    {
        Assert.AreEqual(
            JumpListAppIdHasher.GetDestinationId("Microsoft.Office.EXCEL.EXE.15"),
            JumpListAppIdHasher.GetDestinationId("microsoft.office.excel.exe.15"));
    }
}
