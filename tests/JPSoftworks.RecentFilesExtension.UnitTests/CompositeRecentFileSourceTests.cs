// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using JPSoftworks.RecentFilesExtension.Model;
using JPSoftworks.RecentFilesExtension.Sources;

namespace JPSoftworks.RecentFilesExtension.UnitTests;

[TestClass]
public sealed class CompositeRecentFileSourceTests
{
    [TestMethod]
    public void MissingLocalTargetsAreRemovedWhileRemoteTargetsRemain()
    {
        var directory = Directory.CreateTempSubdirectory("RecentFilesExistence-");
        try
        {
            var presentPath = Path.Combine(directory.FullName, "present.txt");
            var deletedPath = Path.Combine(directory.FullName, "deleted.txt");
            var deletedFolder = Path.Combine(directory.FullName, "deleted-folder");
            File.WriteAllText(presentPath, string.Empty);
            File.WriteAllText(deletedPath, string.Empty);
            Directory.CreateDirectory(deletedFolder);
            var timestamp = DateTimeOffset.UtcNow;
            var present = new RecentPathFile(presentPath, timestamp);
            var folder = new RecentPathFile(directory.FullName, timestamp);
            var deleted = new RecentPathFile(deletedPath, timestamp);
            var deletedDirectory = new RecentPathFile(deletedFolder, timestamp);
            var network = new RecentPathFile(@"\\recent-files-unavailable.invalid\share\report.docx", timestamp);
            var cloud = new RecentPathFile("https://example.invalid/report.docx", timestamp);
            File.Delete(deletedPath);
            Directory.Delete(deletedFolder);
            using var source = new StubSource(present, folder, deleted, deletedDirectory, network, cloud);
            using var composite = new CompositeRecentFileSource([new(() => source, static () => true)]);

            CollectionAssert.AreEquivalent(
                new IRecentFile[] { present, folder, network, cloud },
                composite.GetRecentFiles(CancellationToken.None).ToArray());
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [TestMethod]
    public void GetRecentFilesDeduplicatesAcrossSourcesAndKeepsNewestEntry()
    {
        var older = new RecentPathFile(
            @"\\?\UNC\recent-files-tests.invalid\Documents\Report.docx",
            new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero));
        var newer = new RecentPathFile(
            @"\\recent-files-tests.invalid\documents\REPORT.docx",
            new DateTimeOffset(2026, 1, 2, 10, 0, 0, TimeSpan.Zero));
        var middle = new RecentPathFile(
            "file://recent-files-tests.invalid/Documents/Report.docx",
            new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
        using var firstSource = new StubSource(older);
        using var secondSource = new StubSource(middle, newer);
        using var composite = new CompositeRecentFileSource(
        [
            new(() => firstSource, static () => true),
            new(() => secondSource, static () => true),
        ]);

        var files = composite.GetRecentFiles(CancellationToken.None);

        Assert.HasCount(1, files);
        Assert.AreSame(newer, files[0]);
    }

    [TestMethod]
    public void GetRecentFilesOrdersDistinctEntriesByTimestamp()
    {
        var older = new RecentPathFile(
            @"\\recent-files-tests.invalid\Documents\Older.docx",
            new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero));
        var newer = new RecentPathFile(
            @"\\recent-files-tests.invalid\Documents\Newer.docx",
            new DateTimeOffset(2026, 1, 2, 10, 0, 0, TimeSpan.Zero));
        using var source = new StubSource(older, newer);
        using var composite = new CompositeRecentFileSource(
        [
            new(() => source, static () => true),
        ]);

        var files = composite.GetRecentFiles(CancellationToken.None);

        CollectionAssert.AreEqual(
            new[] { newer.TargetPath, older.TargetPath },
            files.Select(static item => item.TargetPath).ToArray());
    }

    [TestMethod]
    public void GetRecentFilesDeduplicatesSharePointWebLinkAndKeepsNewestEntry()
    {
        var older = new RecentPathFile(
            "https://contoso.sharepoint.com/Documents/tontr%C3%A4ger.xlsx",
            new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero));
        var newer = new RecentPathFile(
            "https://contoso.sharepoint.com/Documents/tonträger.xlsx?web=1#sheet",
            new DateTimeOffset(2026, 1, 2, 10, 0, 0, TimeSpan.Zero));
        using var source = new StubSource(older, newer);
        using var composite = new CompositeRecentFileSource(
        [
            new(() => source, static () => true),
        ]);

        var files = composite.GetRecentFiles(CancellationToken.None);

        Assert.HasCount(1, files);
        Assert.AreSame(newer, files[0]);
    }

    [TestMethod]
    public void SourcesAreCreatedOnlyWhenEnabledAndRecreatedAfterDisabling()
    {
        var settings = new TestRecentFilesSettings { IncludeOfficeRecentFiles = false };
        var created = new List<StubSource>();
        using var composite = new CompositeRecentFileSource(
        [
            new(() =>
            {
                var source = new StubSource(new RecentPathFile("https://example.invalid/file.txt", DateTimeOffset.UtcNow));
                created.Add(source);
                return source;
            }, () => settings.IncludeOfficeRecentFiles),
        ], settings);
        var changeCount = 0;
        composite.Changed += (_, _) => changeCount++;

        Assert.IsEmpty(created);
        Assert.IsEmpty(composite.GetRecentFiles(CancellationToken.None));
        Assert.AreEqual(0, changeCount);

        settings.IncludeOfficeRecentFiles = true;
        settings.RaiseSourcesChanged();
        Assert.HasCount(1, created);
        created[0].RaiseChanged();
        Assert.HasCount(1, composite.GetRecentFiles(CancellationToken.None));
        Assert.AreEqual(2, changeCount);

        settings.IncludeOfficeRecentFiles = false;
        settings.RaiseSourcesChanged();
        Assert.IsTrue(created[0].IsDisposed);
        created[0].RaiseChanged();
        Assert.AreEqual(3, changeCount);
        Assert.IsEmpty(composite.GetRecentFiles(CancellationToken.None));

        settings.IncludeOfficeRecentFiles = true;
        settings.RaiseSourcesChanged();
        Assert.HasCount(2, created);
        Assert.IsFalse(created[1].IsDisposed);
        Assert.HasCount(1, composite.GetRecentFiles(CancellationToken.None));
        settings.RaiseSourcesChanged();
        Assert.HasCount(2, created);

        composite.Dispose();
        composite.Dispose();
        Assert.IsTrue(created[1].IsDisposed);
        settings.RaiseSourcesChanged();
        Assert.HasCount(2, created);
    }

    [TestMethod]
    public void SettingsChangeReevaluatesEnabledSourcesAndRaisesChanged()
    {
        var settings = new TestRecentFilesSettings { IncludeOfficeRecentFiles = true };
        using var source = new StubSource(new RecentPathFile(
            @"\\recent-files-tests.invalid\Documents\Office.docx",
            DateTimeOffset.UtcNow));
        using var composite = new CompositeRecentFileSource(
        [
            new(() => source, () => settings.IncludeOfficeRecentFiles),
        ],
        settings);
        var changeCount = 0;
        composite.Changed += (_, _) => changeCount++;

        Assert.HasCount(1, composite.GetRecentFiles(CancellationToken.None));
        settings.IncludeOfficeRecentFiles = false;
        settings.RaiseSourcesChanged();

        Assert.IsEmpty(composite.GetRecentFiles(CancellationToken.None));
        Assert.AreEqual(1, changeCount);
        Assert.IsTrue(source.IsDisposed);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task StoppingASourceDefersDisposalWithoutBlockingSettingsOrShutdownAsync(bool dispose)
    {
        var settings = new TestRecentFilesSettings();
        using var gate = new ManualResetEventSlim();
        var reading = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var changing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var source = new StubSource
        {
            BeforeRead = () =>
            {
                reading.TrySetResult();
                Assert.IsTrue(gate.Wait(TimeSpan.FromSeconds(5)));
            },
        };
        using var composite = new CompositeRecentFileSource(
            [new(() => source, () => settings.IncludeOfficeRecentFiles)], settings);
        try
        {
            var read = Task.Run(() => composite.GetRecentFiles(CancellationToken.None));
            await reading.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var disable = Task.Run(() =>
            {
                if (dispose)
                {
                    composite.Dispose();
                }
                else
                {
                    settings.IncludeOfficeRecentFiles = false;
                    settings.RaiseSourcesChanged();
                }
                changing.TrySetResult();
            });
            await changing.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.IsFalse(source.IsDisposed);
            await disable.WaitAsync(TimeSpan.FromSeconds(1));
            gate.Set();
            await Task.WhenAll(read, disable).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.IsTrue(source.IsDisposed);
        }
        finally
        {
            gate.Set();
        }
    }

    [TestMethod]
    [DoNotParallelize]
    public void FailedAlwaysEnabledSourceRetriesWithBackoffAndReportsRecovery()
    {
        using var logs = new TestLogCapture();
        var time = new ManualTimeProvider();
        var attempts = 0;
        var item = new RecentPathFile("https://example.invalid/recovered.txt", DateTimeOffset.UtcNow);
        using var source = new StubSource(item);
        using var composite = new CompositeRecentFileSource(
        [
            new(() =>
            {
                if (Interlocked.Increment(ref attempts) < 3)
                {
                    throw new IOException("temporary startup failure");
                }
                return source;
            }, static () => true),
        ], null, time);
        var restarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        composite.Changed += (_, _) => restarted.TrySetResult();
        Assert.IsEmpty(composite.GetRecentFiles(CancellationToken.None));

        time.Advance(TimeSpan.FromSeconds(4));
        Assert.AreEqual(1, attempts);
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.AreEqual(2, attempts);
        time.Advance(TimeSpan.FromSeconds(9));
        Assert.AreEqual(2, attempts);
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.IsTrue(restarted.Task.IsCompletedSuccessfully);

        Assert.AreEqual(3, Volatile.Read(ref attempts));
        Assert.AreSame(item, composite.GetRecentFiles(CancellationToken.None).Single());
        logs.AssertLogged("Failed to start a recent-files source");
        Assert.AreEqual(1, logs.Messages.Count(entry => entry.Message.Contains("Failed to start a recent-files source", StringComparison.Ordinal)));
        logs.AssertLogged("recovered after a startup failure");
        time.Advance(TimeSpan.FromHours(1));
        Assert.AreEqual(3, attempts);
        var changed = 0;
        composite.Changed += (_, _) => changed++;
        source.RaiseChanged();
        Assert.AreEqual(1, changed);
    }

    [TestMethod]
    [DoNotParallelize]
    public void DisablingOrDisposingCancelsPendingStartupRetries()
    {
        using var logs = new TestLogCapture();
        var time = new ManualTimeProvider();
        var settings = new TestRecentFilesSettings();
        var disabledAttempts = 0;
        var disposedAttempts = 0;
        using var disabled = new CompositeRecentFileSource(
        [
            new(() =>
            {
                Interlocked.Increment(ref disabledAttempts);
                throw new IOException("temporary startup failure");
            }, () => settings.IncludeOfficeRecentFiles),
        ], settings, time);
        using var disposed = new CompositeRecentFileSource(
        [
            new(() =>
            {
                Interlocked.Increment(ref disposedAttempts);
                throw new IOException("temporary startup failure");
            }, static () => true),
        ], null, time);

        settings.IncludeOfficeRecentFiles = false;
        settings.RaiseSourcesChanged();
        disposed.Dispose();
        time.Advance(TimeSpan.FromHours(1));

        Assert.AreEqual(1, Volatile.Read(ref disabledAttempts));
        Assert.AreEqual(1, Volatile.Read(ref disposedAttempts));
    }

    [TestMethod]
    [DoNotParallelize]
    public void PermanentStartupFailuresBackOffToFiveMinutes()
    {
        using var logs = new TestLogCapture();
        var time = new ManualTimeProvider();
        var attempts = 0;
        using var composite = new CompositeRecentFileSource(
            [new(() => { attempts++; throw new IOException(); }, static () => true)], null, time);
        var expected = 1;
        foreach (var seconds in new[] { 5, 10, 20, 40, 80, 160, 300, 300 })
        {
            time.Advance(TimeSpan.FromSeconds(seconds - 1));
            Assert.AreEqual(expected, attempts);
            time.Advance(TimeSpan.FromSeconds(1));
            Assert.AreEqual(++expected, attempts);
        }
    }

    [TestMethod]
    public void CanceledReadAfterDisposalReportsCancellation()
    {
        using var composite = new CompositeRecentFileSource([]);
        composite.Dispose();
        Assert.ThrowsExactly<OperationCanceledException>(() => composite.GetRecentFiles(new CancellationToken(canceled: true)));
    }

    [TestMethod]
    public async Task RetiredSourceIsDisposedOnlyAfterItsLastReaderFinishesAsync()
    {
        using var firstGate = new ManualResetEventSlim();
        using var secondGate = new ManualResetEventSlim();
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        using var source = new StubSource(new RecentPathFile("https://example.invalid/retired.txt", DateTimeOffset.UtcNow))
        {
            BeforeRead = () =>
            {
                var first = Interlocked.Increment(ref calls) == 1;
                (first ? firstEntered : secondEntered).TrySetResult();
                Assert.IsTrue((first ? firstGate : secondGate).Wait(TimeSpan.FromSeconds(5)));
            },
        };
        using var composite = new CompositeRecentFileSource([new(() => source, static () => true)]);
        var firstRead = Task.Run(() => composite.GetRecentFiles(CancellationToken.None));
        Task<IReadOnlyList<IRecentFile>>? secondRead = null;
        try
        {
            await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            secondRead = Task.Run(() => composite.GetRecentFiles(CancellationToken.None));
            await secondEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            composite.Dispose();
            Assert.IsFalse(source.IsDisposed);
            firstGate.Set();
            Assert.IsEmpty(await firstRead.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.IsFalse(source.IsDisposed);
            secondGate.Set();
            Assert.IsEmpty(await secondRead.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.IsTrue(source.IsDisposed);
        }
        finally
        {
            firstGate.Set();
            secondGate.Set();
            await firstRead.WaitAsync(TimeSpan.FromSeconds(5));
            if (secondRead is not null)
            {
                await secondRead.WaitAsync(TimeSpan.FromSeconds(5));
            }
        }
    }

    private sealed class StubSource(params IRecentFile[] files) : IRecentFileSource
    {
        public event EventHandler? Changed;
        public bool IsDisposed { get; private set; }
        public Action? BeforeRead { get; init; }

        public IReadOnlyList<IRecentFile> GetRecentFiles(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            this.BeforeRead?.Invoke();
            ObjectDisposedException.ThrowIf(this.IsDisposed, this);
            return files;
        }

        public void RaiseChanged()
        {
            this.Changed?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose()
        {
            this.IsDisposed = true;
        }
    }
}