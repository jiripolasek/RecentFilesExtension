// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using JPSoftworks.RecentFilesExtension.Helpers;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace JPSoftworks.RecentFilesExtension.UnitTests;

[TestClass]
[DoNotParallelize]
public sealed class SettingsManagerTests
{
    [TestMethod]
    public void HostShellIconSupportUsesThePackageFamilyAndNumericRetailVersion()
    {
        const string retail = "Microsoft.CommandPalette";
        const string dev = "Microsoft.CommandPalette.Dev";
        const string publisher = "8wekyb3d8bbwe";
        Assert.AreEqual(ShellIconSupport.Unsupported, CmdPalHost.GetPackageShellIconSupport(retail, publisher, 0, 12));
        Assert.AreEqual(ShellIconSupport.Supported, CmdPalHost.GetPackageShellIconSupport(retail, publisher, 0, 13));
        Assert.AreEqual(ShellIconSupport.Supported, CmdPalHost.GetPackageShellIconSupport(retail, publisher, 1, 0));
        Assert.AreEqual(ShellIconSupport.Supported, CmdPalHost.GetPackageShellIconSupport(dev, publisher, 0, 0));
        Assert.AreEqual(ShellIconSupport.Unknown, CmdPalHost.GetPackageShellIconSupport(dev, "other", 1, 0));
        Assert.AreEqual(ShellIconSupport.Unknown, CmdPalHost.GetPackageShellIconSupport(retail, "other", 1, 0));
        Assert.AreEqual(ShellIconSupport.Unknown, CmdPalHost.GetPackageShellIconSupport("other", publisher, 1, 0));
    }

    [TestMethod]
    public void NoRpcCallerLeavesTheHostUnknown()
    {
        Assert.AreEqual(ShellIconSupport.Unknown, CmdPalHost.GetCallingShellIconSupport());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void KnownHostsHideThePreviewAndPreserveItsSavedValue(bool preview)
    {
        const string key = "jpsoftworks.recentfiles.Appearance.UseShellIcons";
        var folder = Directory.CreateTempSubdirectory("RecentFilesSettings-");
        try
        {
            var path = Path.Combine(folder.FullName, "settings.json");
            var contents = $$"""{"{{key}}":"{{(preview ? "true" : "false")}}"}""";
            File.WriteAllText(path, contents);
            var settings = new SettingsManager(path);
            var known = settings.GetHostSettings(ShellIconSupport.Supported);
            Assert.AreSame(known, settings.GetHostSettings(ShellIconSupport.Unsupported));
            Assert.AreEqual(contents, File.ReadAllText(path));
            var form = (SettingsForm)known.ToContent().Single();
            Assert.DoesNotContain(key, form.TemplateJson);
            form.SubmitForm("""{"jpsoftworks.recentfiles.Appearance.ViewMode":"gallery"}""", string.Empty);
            Assert.AreEqual(preview, settings.UseShellIcons);
            Assert.AreEqual(preview, new SettingsManager(path).UseShellIcons);
            var unknown = settings.GetHostSettings(ShellIconSupport.Unknown);
            Assert.AreEqual("gallery", unknown.GetSetting<string>("jpsoftworks.recentfiles.Appearance.ViewMode"));
            StringAssert.Contains(((SettingsForm)unknown.ToContent().Single()).TemplateJson, key);
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [TestMethod]
    [DataRow("{invalid json")]
    [DataRow("[]")]
    public void MalformedSettingsAreLoggedAndPreservedBeforeRecovery(string contents)
    {
        using var logs = new TestLogCapture();
        var folder = Directory.CreateTempSubdirectory("RecentFilesSettings-").FullName;
        try
        {
            var path = Path.Combine(folder, "settings.json");
            File.WriteAllText(path, contents);
            var settings = new SettingsManager(path);
            Assert.AreEqual("list", settings.ViewMode);
            logs.AssertLogged("Failed to load settings");

            settings.ViewMode = "gallery";
            logs.AssertLogged("Invalid settings preserved");
            Assert.AreEqual(contents, File.ReadAllText(Directory.GetFiles(folder, "*.corrupt").Single()));
            Assert.AreEqual("gallery", new SettingsManager(path).ViewMode);
            Assert.IsEmpty(Directory.GetFiles(folder, "*.tmp"));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [TestMethod]
    public void FailedReplacementKeepsThePreviousSettingsAndLogsTheError()
    {
        using var logs = new TestLogCapture();
        var folder = Directory.CreateTempSubdirectory("RecentFilesSettings-").FullName;
        var path = Path.Combine(folder, "settings.json");
        const string original = """{"unknown":"preserve me","jpsoftworks.recentfiles.Appearance.ViewMode":"list"}""";
        try
        {
            File.WriteAllText(path, original);
            var settings = new SettingsManager(path);
            File.SetAttributes(path, FileAttributes.ReadOnly);
            settings.ViewMode = "gallery";
            logs.AssertLogged("Failed to save settings");
            Assert.AreEqual(original, File.ReadAllText(path));
            Assert.IsEmpty(Directory.GetFiles(folder, "*.tmp"));

            File.SetAttributes(path, FileAttributes.Normal);
            settings.SaveSettings();
            Assert.AreEqual("gallery", new SettingsManager(path).ViewMode);
            StringAssert.Contains(File.ReadAllText(path), "\"unknown\":\"preserve me\"");
        }
        finally
        {
            File.SetAttributes(path, FileAttributes.Normal);
            Directory.Delete(folder, recursive: true);
        }
    }

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
            Assert.IsFalse(settings.UseShellIcons);
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
                    "jpsoftworks.recentfiles.Appearance.ShowDetails": "true",
                    "jpsoftworks.recentfiles.Appearance.UseShellIcons": "true"
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
            Assert.IsTrue(reloaded.UseShellIcons);
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