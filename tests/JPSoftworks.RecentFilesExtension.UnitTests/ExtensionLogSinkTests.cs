// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using JPSoftworks.CommandPalette.Extensions.Toolkit.Logging;
using JPSoftworks.CommandPalette.Extensions.Toolkit.Logging.Abstractions;
using JPSoftworks.RecentFilesExtension.Helpers;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace JPSoftworks.RecentFilesExtension.UnitTests;

[TestClass]
public sealed class ExtensionLogSinkTests
{
    [TestMethod]
    public void StartupPreambleReachesTheFileWithoutAConnectedHost()
    {
        var directory = Directory.CreateTempSubdirectory("RecentFilesStartupInfo-");
        try
        {
            var diagnostics = new RecordingSink();
            using (var sink = new ExtensionLogSink(Path.Combine(directory.FullName, "log.txt"),
                       Path.Combine(directory.FullName, "fallback.txt"), diagnostics))
            {
                Program.LogStartup(sink);
            }

            var entry = diagnostics.Entries.Single();
            Assert.AreEqual(ExtensionHostLogLevel.Information, entry.Level);
            var log = File.ReadAllText(Directory.GetFiles(directory.FullName, "log*.txt").Single());
            StringAssert.Contains(log, "Starting RecentFilesExtension");
            StringAssert.Contains(log, $"Version: {typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion}");
            StringAssert.Contains(log, $"OS: {RuntimeInformation.OSDescription}");
            StringAssert.Contains(log, $"OS architecture: {RuntimeInformation.OSArchitecture}");
            StringAssert.Contains(log, $"Process architecture: {RuntimeInformation.ProcessArchitecture}");
            StringAssert.Contains(log, $"Runtime: {RuntimeInformation.FrameworkDescription}");
            StringAssert.Contains(log, $"Culture: {CultureInfo.CurrentCulture.Name}");
            StringAssert.Contains(log, $"UI culture: {CultureInfo.CurrentUICulture.Name}");
            StringAssert.Contains(log, $"Time zone: {TimeZoneInfo.Local.Id}");
            StringAssert.Contains(log, $"Process ID: {Environment.ProcessId}");
            Assert.IsFalse(log.Contains(directory.FullName, StringComparison.Ordinal));
            Assert.IsFalse(entry.Message.Contains('\n'));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [TestMethod]
    public async Task SlowDiagnosticsDoNotBlockOtherFileWritesOrDisposalAsync()
    {
        var directory = Directory.CreateTempSubdirectory("RecentFilesSlowHost-");
        using var gate = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var sink = new ExtensionLogSink(Path.Combine(directory.FullName, "log.txt"),
            Path.Combine(directory.FullName, "fallback.txt"), new BlockingSink(gate, entered));
        var first = Task.Run(() => sink.Write(Entry("first")));
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.Run(() => sink.Write(Entry("second"))).WaitAsync(TimeSpan.FromSeconds(2));
            await Task.Run(sink.Dispose).WaitAsync(TimeSpan.FromSeconds(2));
            var log = File.ReadAllText(Directory.GetFiles(directory.FullName, "log*.txt").Single());
            StringAssert.Contains(log, "first");
            StringAssert.Contains(log, "second");
        }
        finally
        {
            gate.Set();
            await first.WaitAsync(TimeSpan.FromSeconds(5));
            sink.Dispose();
            directory.Delete(recursive: true);
        }
    }

    private sealed class BlockingSink(ManualResetEventSlim gate, TaskCompletionSource entered) : IExtensionHostLogSink
    {
        private int _calls;
        public void Write(ExtensionHostLogEntry entry)
        {
            if (Interlocked.Increment(ref this._calls) == 1)
            {
                entered.TrySetResult();
                Assert.IsTrue(gate.Wait(TimeSpan.FromSeconds(10)));
            }
        }
    }

    [TestMethod]
    public void InstalledCompositeSinkIsolatesThrowingDiagnostics()
    {
        var recording = new RecordingSink();
        var composite = new CompositeExtensionHostLogSink(new IExtensionHostLogSink[] { new ThrowingSink(), recording });
        composite.Write(Entry("original failure"));
        Assert.AreEqual("original failure", recording.Entries.Single().Message);
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void ThrowingDiagnosticsDoNotInterruptFileLoggingOrDisposal(bool primaryFails, bool fallbackFails)
    {
        var directory = Directory.CreateTempSubdirectory("RecentFilesThrowingLog-").FullName;
        try
        {
            var blocked = Path.Combine(directory, "blocked");
            File.WriteAllText(blocked, "block directory creation");
            var primary = Path.Combine(primaryFails ? blocked : directory, "primary.txt");
            var fallback = Path.Combine(fallbackFails ? blocked : directory, "fallback.txt");
            var diagnostics = new ThrowingSink();
            using (var sink = new ExtensionLogSink(primary, fallback, diagnostics))
            {
                sink.Write(Entry("original failure"));
                sink.Write(Entry("another failure"));
            }

            Assert.IsTrue(diagnostics.WriteCount > 0);
            if (!primaryFails || !fallbackFails)
            {
                var pattern = primaryFails ? "fallback*.txt" : "primary*.txt";
                var log = File.ReadAllText(Directory.GetFiles(directory, pattern).Single());
                StringAssert.Contains(log, "original failure");
                StringAssert.Contains(log, "another failure");
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    [DoNotParallelize]
    public void SettingsFailureReachesTheFileBeforeTheHostConnects()
    {
        var directory = Directory.CreateTempSubdirectory("RecentFilesStartupLog-").FullName;
        var previousHost = ExtensionHost.Host;
        try
        {
            ExtensionHost.Initialize(null!);
            using var sink = new ExtensionLogSink(
                Path.Combine(directory, "log.txt"), Path.Combine(directory, "fallback.txt"), new RecordingSink());
            using var capture = new TestLogCapture(sink: sink);
            ExtensionHost.Initialize(null!);
            var settingsPath = Path.Combine(directory, "settings.json");
            File.WriteAllText(settingsPath, "{invalid json");
            _ = new SettingsManager(settingsPath);

            using var stream = new FileStream(Directory.GetFiles(directory, "log*.txt").Single(),
                FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            var log = reader.ReadToEnd();
            StringAssert.Contains(log, "Failed to load settings");
            StringAssert.Contains(log, "settings.json");
            Assert.IsFalse(log.Contains(directory, StringComparison.Ordinal));
        }
        finally
        {
            ExtensionHost.Initialize(previousHost!);
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void FailedFileLoggingReportsTheFailureUsesFallbackAndRecovers()
    {
        var directory = Directory.CreateTempSubdirectory("RecentFilesLogTest-").FullName;
        try
        {
            var blocked = Path.Combine(directory, "blocked");
            File.WriteAllText(blocked, "block directory creation");
            var diagnostics = new RecordingSink();
            using (var sink = new ExtensionLogSink(
                       Path.Combine(blocked, "log.txt"), Path.Combine(directory, "fallback.txt"), diagnostics))
            {
                sink.Write(Entry("first"));
                sink.Write(Entry("second"));
                File.Delete(blocked);
                Directory.CreateDirectory(blocked);
                sink.Write(Entry("recovered"));
            }

            var fallback = File.ReadAllText(Directory.GetFiles(directory, "fallback*.txt").Single());
            StringAssert.Contains(fallback, "Cannot write log");
            StringAssert.Contains(fallback, "first");
            StringAssert.Contains(fallback, "second");
            var primary = File.ReadAllText(Directory.GetFiles(blocked, "log*.txt").Single());
            StringAssert.Contains(primary, "recovered");
            Assert.AreEqual(1, diagnostics.Entries.Count(entry => entry.Message.StartsWith("Cannot write log", StringComparison.Ordinal)));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void BothFileFailuresStillReachTheOtherDiagnostics()
    {
        var directory = Directory.CreateTempSubdirectory("RecentFilesLogTest-").FullName;
        try
        {
            var blocked = Path.Combine(directory, "blocked");
            File.WriteAllText(blocked, "block directory creation");
            var diagnostics = new RecordingSink();
            using var sink = new ExtensionLogSink(Path.Combine(blocked, "primary.txt"), Path.Combine(blocked, "fallback.txt"), diagnostics);
            sink.Write(Entry("original failure"));
            sink.Write(Entry("another failure"));
            Assert.HasCount(4, diagnostics.Entries);
            Assert.AreEqual(2, diagnostics.Entries.Count(entry => entry.Message.Contains(nameof(IOException), StringComparison.Ordinal)));
            Assert.IsTrue(diagnostics.Entries.All(entry => entry.Exception is null));
            Assert.AreEqual("another failure", diagnostics.Entries[^1].Message);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static ExtensionHostLogEntry Entry(string message) => new(DateTimeOffset.Now, ExtensionHostLogLevel.Error, "Test", 0, message);

    private sealed class RecordingSink : IExtensionHostLogSink
    {
        public List<ExtensionHostLogEntry> Entries { get; } = [];
        public void Write(ExtensionHostLogEntry entry) => this.Entries.Add(entry);
    }

    private sealed class ThrowingSink : IExtensionHostLogSink
    {
        public int WriteCount { get; private set; }

        public void Write(ExtensionHostLogEntry entry)
        {
            this.WriteCount++;
            throw new InvalidOperationException("test disconnected diagnostics");
        }
    }
}