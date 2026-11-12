// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.IO.Compression;
using JPSoftworks.RecentFilesExtension.Commands;
using JPSoftworks.RecentFilesExtension.Helpers;
using JPSoftworks.RecentFilesExtension.Pages;
using JPSoftworks.RecentFilesExtension.Resources;
using Microsoft.CommandPalette.Extensions;

namespace JPSoftworks.RecentFilesExtension.UnitTests;

[TestClass]
public sealed class ReportProblemTests
{
    [TestMethod]
    public async Task ArchiveIncludesActiveAndFallbackLogsWithoutOtherFiles()
    {
        var directory = Directory.CreateTempSubdirectory("RecentFilesReport-");
        try
        {
            var primary = Directory.CreateDirectory(Path.Combine(directory.FullName, "primary")).FullName;
            var fallback = Directory.CreateDirectory(Path.Combine(directory.FullName, "fallback")).FullName;
            File.WriteAllText(Path.Combine(primary, "log20261112.txt"), "primary log");
            File.WriteAllText(Path.Combine(fallback, "log20261112.txt"), "fallback log");
            File.WriteAllText(Path.Combine(primary, "settings.json"), "private settings");
            var nested = Directory.CreateDirectory(Path.Combine(primary, "nested")).FullName;
            File.WriteAllText(Path.Combine(nested, "log.txt"), "unrelated log");
            using var activeLog = new FileStream(Path.Combine(primary, "log20261112.txt"), FileMode.Open,
                FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);

            var result = await new DiagnosticLogArchiveService(primary, fallback)
                .CreateArchiveAsync(Path.Combine(directory.FullName, "output"), CancellationToken.None);

            Assert.AreEqual(2, result.LogFileCount);
            StringAssert.StartsWith(Path.GetFileName(result.ArchivePath), "RecentFilesExtension-Logs-");
            using var archive = ZipFile.OpenRead(result.ArchivePath);
            Assert.HasCount(2, archive.Entries);
            Assert.IsNotNull(archive.GetEntry("fallback/log20261112.txt"));
            using var reader = new StreamReader(archive.GetEntry("log20261112.txt")!.Open());
            Assert.AreEqual("primary log", await reader.ReadToEndAsync());
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [TestMethod]
    public async Task ArchiveWorksWhenOnlyFallbackLogsExist()
    {
        var directory = Directory.CreateTempSubdirectory("RecentFilesFallbackReport-");
        try
        {
            File.WriteAllText(Path.Combine(directory.FullName, "log.txt"), "fallback log");
            var result = await new DiagnosticLogArchiveService(string.Empty, directory.FullName)
                .CreateArchiveAsync(Path.Combine(directory.FullName, "output"), CancellationToken.None);

            using var archive = ZipFile.OpenRead(result.ArchivePath);
            Assert.AreEqual("fallback/log.txt", archive.Entries.Single().FullName);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [TestMethod]
    public async Task FailedOrCancelledArchiveLeavesNoPartialZip()
    {
        var directory = Directory.CreateTempSubdirectory("RecentFilesFailedReport-");
        try
        {
            var log = Path.Combine(directory.FullName, "log.txt");
            File.WriteAllText(log, "locked log");
            var output = Directory.CreateDirectory(Path.Combine(directory.FullName, "output")).FullName;
            var service = new DiagnosticLogArchiveService(directory.FullName, Path.Combine(directory.FullName, "missing"));
            using var lockedLog = new FileStream(log, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

            await Assert.ThrowsExactlyAsync<IOException>(() => service.CreateArchiveAsync(output, CancellationToken.None));
            Assert.IsEmpty(Directory.GetFiles(output));
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => service.CreateArchiveAsync(output, cancellation.Token));
            Assert.IsEmpty(Directory.GetFiles(output));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [TestMethod]
    public void ReportPageShowsBothActionsAndSharingInstructions()
    {
        var page = new ReportProblemPage(new DiagnosticLogArchiveService("unused", "unused-fallback"));
        Assert.AreEqual("Report a bug", page.Name);
        Assert.IsTrue(page.ShowDetails);
        var items = page.GetItems();
        Assert.HasCount(2, items);
        Assert.IsInstanceOfType<SaveDiagnosticLogsCommand>(items[0].Command);
        Assert.IsInstanceOfType<OpenGitHubIssuesCommand>(items[1].Command);
        Assert.IsTrue(items.All(item => item.Details.Body == Strings.ReportProblem_Instructions));
        StringAssert.Contains(items[0].Details.Body, "Review the archive before sharing it.");
    }

    [TestMethod]
    [DoNotParallelize]
    [DataRow(true)]
    [DataRow(false)]
    public void GitHubLaunchKeepsPageOpenAndReportsRejection(bool launched)
    {
        using var logs = new TestLogCapture();
        var result = new OpenGitHubIssuesCommand(() => Task.FromResult(launched)).Invoke();
        Assert.AreEqual(launched ? CommandResultKind.KeepOpen : CommandResultKind.ShowToast, result.Kind);
        if (!launched)
        {
            logs.AssertLogged("GitHub Issues launch was rejected");
        }
    }

    [TestMethod]
    [DoNotParallelize]
    public void GitHubLaunchFailureShowsToastAndLogsError()
    {
        using var logs = new TestLogCapture();
        var command = new OpenGitHubIssuesCommand(() => throw new InvalidOperationException("launch failure"));
        Assert.AreEqual(CommandResultKind.ShowToast, command.Invoke().Kind);
        logs.AssertLogged("Unable to open GitHub Issues");
    }
}