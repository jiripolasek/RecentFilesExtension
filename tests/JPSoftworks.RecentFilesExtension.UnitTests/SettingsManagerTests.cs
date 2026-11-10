// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using JPSoftworks.RecentFilesExtension.Helpers;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace JPSoftworks.RecentFilesExtension.UnitTests;

[TestClass]
public sealed class SettingsManagerTests
{
    [TestMethod]
    public void AppearanceSettingsKeepDefaultsAndPersistWithoutRefreshingSources()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"RecentFilesSettings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "settings.json");
            File.WriteAllText(path, """{"jpsoftworks.recentfiles.Sources.OfficeRecentFiles":"false"}""");
            var settings = new SettingsManager(path);
            Assert.AreEqual("list", settings.ViewMode);
            Assert.IsFalse(settings.HideFileExtensions);
            Assert.IsFalse(settings.ShowFileTypeInSubtitle);
            Assert.IsFalse(settings.ShowFileTypeTags);
            Assert.IsFalse(settings.ShowDetails);
            Assert.IsFalse(settings.IncludeOfficeRecentFiles);
            var sourceChanges = 0;
            var appearanceChanges = 0;
            settings.SourcesChanged += (_, _) => sourceChanges++;
            settings.AppearanceChanged += (_, _) => appearanceChanges++;

            var form = Assert.IsInstanceOfType<SettingsForm>(settings.Settings.ToContent().Single());
            form.SubmitForm("""
                {
                    "jpsoftworks.recentfiles.Appearance.ViewMode": "gallery",
                    "jpsoftworks.recentfiles.Appearance.HideFileExtensions": "true",
                    "jpsoftworks.recentfiles.Appearance.ShowFileTypeInSubtitle": "true",
                    "jpsoftworks.recentfiles.Appearance.ShowFileTypeTags": "true",
                    "jpsoftworks.recentfiles.Appearance.ShowDetails": "true"
                }
                """, string.Empty);

            Assert.AreEqual(0, sourceChanges);
            Assert.AreEqual(1, appearanceChanges);
            var reloaded = new SettingsManager(path);
            Assert.AreEqual("gallery", reloaded.ViewMode);
            Assert.IsTrue(reloaded.HideFileExtensions);
            Assert.IsTrue(reloaded.ShowFileTypeInSubtitle);
            Assert.IsTrue(reloaded.ShowFileTypeTags);
            Assert.IsTrue(reloaded.ShowDetails);
            Assert.IsFalse(reloaded.IncludeOfficeRecentFiles);

            form.SubmitForm("""{"jpsoftworks.recentfiles.Sources.OfficeRecentFiles":"true"}""", string.Empty);
            Assert.AreEqual(1, sourceChanges);
            Assert.IsTrue(new SettingsManager(path).IncludeOfficeRecentFiles);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}