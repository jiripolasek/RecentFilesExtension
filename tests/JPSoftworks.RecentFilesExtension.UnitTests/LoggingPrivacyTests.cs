// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using JPSoftworks.CommandPalette.Extensions.Toolkit.Logging;
using JPSoftworks.CommandPalette.Extensions.Toolkit.Logging.Abstractions;
using JPSoftworks.RecentFilesExtension.Commands;
using JPSoftworks.RecentFilesExtension.Helpers;
using JPSoftworks.RecentFilesExtension.Model;
using JPSoftworks.RecentFilesExtension.Pages;
using JPSoftworks.RecentFilesExtension.Sources;
using Microsoft.CommandPalette.Extensions;

namespace JPSoftworks.RecentFilesExtension.UnitTests;

[TestClass]
[DoNotParallelize]
public sealed class LoggingPrivacyTests
{
    [TestMethod]
    public void ExpectedFailureSummaryIsOneLineAndContainsNoExceptionMessage()
    {
        var failure = System.Runtime.InteropServices.Marshal.GetExceptionForHR(unchecked((int)0x80004005))!;
        var summary = ExceptionDiagnostics.Summarize(failure);
        Assert.AreEqual("(COMException, HRESULT 0x80004005)", summary);
    }

    [TestMethod]
    public void LoggingScopesRestoreThePreviousSinkAndDebugSetting()
    {
        var recording = new RecordingSink();
        using var outer = new TestLogCapture(includeDebug: true, sink: recording);
        using (var inner = new TestLogCapture())
        {
            Logger.LogWarning("inner scope");
            inner.AssertLogged("inner scope");
            Assert.IsEmpty(recording.Messages);
        }
        Logger.LogDebug("restored debug sink");
        Assert.AreEqual("restored debug sink", recording.Messages.Single());
    }

    [TestMethod]
    public void SearchAndDocumentFailuresDoNotPersistUserDataOrExceptionMessages()
    {
        var directory = Directory.CreateTempSubdirectory("RecentFilesLogPrivacy-");
        try
        {
            const string query = "private-customer-search";
            const string target = @"C:\private-customer\confidential-document.docx";
            const string shortcutName = "private-customer-shortcut.lnk";
            using var sink = new ExtensionLogSink(
                Path.Combine(directory.FullName, "log.txt"),
                Path.Combine(directory.FullName, "fallback.txt"),
                CommandPaletteLogSink.Instance);
            using var host = new TestLogCapture(sink: sink);

            var item = new RecentPathFile(target, DateTimeOffset.UtcNow);
            var openWith = new OpenWithCommand(item, _ => throw new IOException($"Cannot open {target}"));
            Assert.AreEqual(CommandResultKind.KeepOpen, openWith.Invoke().Kind);

            var file = new FailingFile($"Cannot search {query} in {target}");
            using (var page = new RecentFilesExtensionPage(new TestRecentFilesSettings(), new StubProvider(file)))
            {
                Assert.HasCount(20, page.GetItems());
                Assert.IsTrue(page.HasMoreItems);
                file.Fail = true;
                page.UpdateSearchText(string.Empty, query);
                Assert.IsEmpty(page.GetItems());
                page.LoadMore();
                Assert.IsEmpty(page.GetItems());
                Assert.IsFalse(page.HasMoreItems);
                file.Fail = false;
                page.UpdateSearchText(query, "file");
                Assert.HasCount(20, page.GetItems());
            }

            File.WriteAllText(Path.Combine(directory.FullName, shortcutName), "not a shell link");
            using (var source = new OfficeRecentFolderSource(directory.FullName))
            {
                Assert.IsEmpty(source.GetRecentFiles(CancellationToken.None));
            }

            Exception failure;
            try
            {
                throw new IOException($"Cannot open {target}");
            }
            catch (IOException ex)
            {
                failure = new AggregateException("private outer exception", ex);
            }
            foreach (var operation in new[] { "Unhandled process exception", "Unobserved background task exception", "Failed to initialize the extension host" })
            {
                Program.LogFailure(sink, operation, failure);
            }
            sink.Write(new(DateTimeOffset.Now, ExtensionLogLevel.Error, "Host", 0, "Unhandled host failure", failure));
            var settingsPath = Path.Combine(directory.FullName, "settings.json");
            File.WriteAllText(settingsPath, "{}");
            var settings = new SettingsManager(settingsPath);
            File.SetAttributes(settingsPath, FileAttributes.ReadOnly);
            try
            {
                settings.SaveSettings();
            }
            finally
            {
                File.SetAttributes(settingsPath, FileAttributes.Normal);
            }

            sink.Dispose();
            var log = File.ReadAllText(Directory.GetFiles(directory.FullName, "log*.txt").Single());
            StringAssert.Contains(log, "Unable to show Open with");
            StringAssert.Contains(log, "Failed to search recent files");
            Assert.IsFalse(log.Contains("Failed to load recent-file results", StringComparison.Ordinal));
            StringAssert.Contains(log, "Failed to save settings.json");
            StringAssert.Contains(log, "Unhandled process exception");
            StringAssert.Contains(log, "Unhandled host failure");
            StringAssert.Contains(log, nameof(AggregateException));
            StringAssert.Contains(log, nameof(SearchAndDocumentFailuresDoNotPersistUserDataOrExceptionMessages));
            StringAssert.Contains(log, "Failed to retrieve a shell link");
            StringAssert.Contains(log, nameof(IOException));
            StringAssert.Contains(log, "HRESULT 0x");
            foreach (var secret in new[] { query, target, shortcutName, directory.FullName, "Cannot open", "Cannot search", "private outer exception", ":line " })
            {
                Assert.IsFalse(log.Contains(secret, StringComparison.Ordinal), "Persistent diagnostics must not contain user data.");
                Assert.IsFalse(host.Messages.Any(entry => entry.Message.Contains(secret, StringComparison.Ordinal)));
            }
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private sealed class FailingFile(string failureMessage) : IRecentFile
    {
        public bool Fail { get; set; }
        public string DisplayName => this.Fail ? throw new IOException(failureMessage) : "file.txt";
        public string TargetPath => "https://example.invalid/file.txt";
        public string FullPath => this.TargetPath;
        public string? IconPath => null;
        public DateTimeOffset LastUsedTimeUtc => DateTimeOffset.MinValue;
        public bool IsFileSystemPath => false;
        public bool IsDirectory() => false;
    }

    private sealed class StubProvider(IRecentFile file) : IRecentFilesProvider
    {
        public event EventHandler<IList<IRecentFile>>? RecentFilesChanged { add { } remove { } }
        public event EventHandler? InitialLoadComplete { add { } remove { } }
        public bool IsInitialLoadComplete => true;
        public IList<IRecentFile> GetRecentFiles() => Enumerable.Repeat(file, 21).ToList();
        public Task<bool> WaitForInitialLoadAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private sealed class RecordingSink : IExtensionLogSink
    {
        public List<string> Messages { get; } = [];
        public void Write(ExtensionLogEntry entry) => this.Messages.Add(entry.Message);
    }
}