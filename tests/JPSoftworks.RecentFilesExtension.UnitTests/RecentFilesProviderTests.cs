// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using JPSoftworks.RecentFilesExtension.Model;
using JPSoftworks.RecentFilesExtension.Pages;
using JPSoftworks.RecentFilesExtension.Sources;

namespace JPSoftworks.RecentFilesExtension.UnitTests;

[TestClass]
[DoNotParallelize]
public sealed class RecentFilesProviderTests
{
    [TestMethod]
    public async Task InvalidWildcardUsesLiteralSearchAndRecoversOnTheNextQueryAsync()
    {
        using var logs = new TestLogCapture(includeDebug: true);
        using var source = new StubSource(File("one.txt"), File("two.txt"));
        using var provider = new RecentFilesProvider(source);
        using var loader = new RecentFilesLoader(provider, pageSize: 1);
        Assert.IsTrue(await provider.WaitForInitialLoadAsync(TimeSpan.FromSeconds(5)));
        var token = Guid.NewGuid();
        loader.Restart(new("*.txt", [], [], string.Empty, false), token);
        Assert.AreEqual("one.txt", loader.LoadNextPage(token).Single().DisplayName);

        var invalidToken = Guid.NewGuid();
        loader.Restart(new("*[a-]", [], [], string.Empty, false), invalidToken);
        Assert.IsEmpty(loader.LoadNextPage(invalidToken));
        Assert.IsFalse(loader.HasMore);
        logs.AssertLogged("using a literal search");
        loader.Restart(new("*[a-]", [], [], string.Empty, false), invalidToken);
        var invalidRefreshed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        loader.SourceChanged += (_, _) => invalidRefreshed.TrySetResult();
        source.Files = [File("one.txt"), File("two.txt"), File("extra.pdf")];
        source.RaiseChanged();
        await invalidRefreshed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(1, logs.Messages.Count(entry => entry.Message.Contains("using a literal search", StringComparison.Ordinal)));
        Assert.IsFalse(logs.Messages.Any(entry => entry.Message.Contains("*[a-]", StringComparison.Ordinal)));
        loader.Restart(new("*.txt", [], [], string.Empty, false), token);
        Assert.AreEqual("one.txt", loader.LoadNextPage(token).Single().DisplayName);

        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        loader.SourceChanged += (_, _) => changed.TrySetResult();
        source.Files = [File("three.txt"), File("ignore.pdf")];
        source.RaiseChanged();
        await changed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual("three.txt", loader.LoadNextPage(token).Single().DisplayName);
        Assert.IsFalse(loader.HasMore);
    }

    [TestMethod]
    public async Task TimerLogsSubscriberFailureAndRetriesNotificationOnTheNextRefreshAsync()
    {
        using var logs = new TestLogCapture();
        using var source = new StubSource(File("one.txt"));
        using var provider = new RecentFilesProvider(source);
        Assert.IsTrue(await provider.WaitForInitialLoadAsync(TimeSpan.FromSeconds(5)));
        EventHandler<IList<IRecentFile>> failing = (_, _) => throw new InvalidOperationException("subscriber failure");
        provider.RecentFilesChanged += failing;
        source.Files = [File("two.txt")];
        source.RaiseChanged();
        logs.AssertLogged("Failed to refresh recent files or notify subscribers");
        provider.RecentFilesChanged -= failing;

        var notified = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        provider.RecentFilesChanged += (_, _) => notified.TrySetResult();
        source.RaiseChanged();
        await notified.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual("two.txt", provider.GetRecentFiles().Single().DisplayName);
    }

    [TestMethod]
    public async Task FailingInitializationSubscriberDoesNotBlockOtherWaitersAsync()
    {
        using var logs = new TestLogCapture();
        using var gate = new ManualResetEventSlim();
        using var source = new StubSource { BeforeRead = () => gate.Wait(TimeSpan.FromSeconds(5)) };
        using var provider = new RecentFilesProvider(source);
        provider.InitialLoadComplete += (_, _) => throw new InvalidOperationException("initialization subscriber failure");
        var loaded = provider.WaitForInitialLoadAsync(TimeSpan.FromSeconds(5));
        gate.Set();
        Assert.IsTrue(await loaded);
        logs.AssertLogged("Failed to notify a subscriber that initial loading completed");
    }

    private static RecentPathFile File(string name) => new($"https://example.invalid/{name}", DateTimeOffset.UtcNow);

    [TestMethod]
    public async Task WildcardEvaluationFailuresAreNotTreatedAsInvalidInputAsync()
    {
        var file = new UnreadableFile();
        using var source = new StubSource(file);
        using var provider = new RecentFilesProvider(source);
        using var loader = new RecentFilesLoader(provider);
        Assert.IsTrue(await provider.WaitForInitialLoadAsync(TimeSpan.FromSeconds(5)));

        Assert.ThrowsExactly<InvalidDataException>(() =>
            loader.Restart(new("*.txt", [], [], string.Empty, false), Guid.NewGuid()));
        Assert.AreEqual(1, file.DisplayNameReads);
    }

    private sealed class UnreadableFile : IRecentFile
    {
        public int DisplayNameReads { get; private set; }
        public string DisplayName
        {
            get
            {
                this.DisplayNameReads++;
                throw new InvalidDataException("test item failure");
            }
        }
        public string TargetPath => "https://example.invalid/file.txt";
        public string FullPath => this.TargetPath;
        public string? IconPath => null;
        public DateTimeOffset LastUsedTimeUtc => DateTimeOffset.MinValue;
        public bool IsFileSystemPath => false;
        public bool IsDirectory() => false;
    }

    [TestMethod]
    public async Task FailedQueryDoesNotFailBackgroundRefreshesAndRecoversWithReadableItemsAsync()
    {
        using var logs = new TestLogCapture();
        using var source = new StubSource(new UnreadableFile());
        using var provider = new RecentFilesProvider(source);
        using var loader = new RecentFilesLoader(provider);
        Assert.IsTrue(await provider.WaitForInitialLoadAsync(TimeSpan.FromSeconds(5)));
        var token = Guid.NewGuid();
        Assert.ThrowsExactly<InvalidDataException>(() => loader.Restart(new("*.txt", [], [], string.Empty, false), token));
        for (var index = 0; index < 3; index++)
        {
            var refreshed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            EventHandler<IList<IRecentFile>> changed = (_, _) => refreshed.TrySetResult();
            loader.SourceChanged += changed;
            source.Files = index < 2 ? [new UnreadableFile(), File($"update{index}.txt")] : [File("recovered.txt")];
            source.RaiseChanged();
            await refreshed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            loader.SourceChanged -= changed;
            if (index < 2)
            {
                Assert.IsEmpty(loader.LoadNextPage(token));
                Assert.IsFalse(loader.HasMore);
            }
        }
        Assert.AreEqual("recovered.txt", loader.LoadNextPage(token).Single().DisplayName);
        Assert.IsFalse(logs.Messages.Any(entry => entry.Message.Contains("Failed to refresh recent files", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task ContinuousChangesDoNotPostponeThePendingRefreshAsync()
    {
        using var source = new StubSource(File("one.txt"));
        using var provider = new RecentFilesProvider(source);
        Assert.IsTrue(await provider.WaitForInitialLoadAsync(TimeSpan.FromSeconds(5)));
        var refreshed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        provider.RecentFilesChanged += (_, _) => refreshed.TrySetResult();
        source.Files = [File("two.txt")];

        for (var attempt = 0; attempt < 40 && !refreshed.Task.IsCompleted; attempt++)
        {
            source.RaiseChanged();
            await Task.Delay(50);
        }

        Assert.IsTrue(refreshed.Task.IsCompleted, "The provider must refresh while events are still arriving.");
        Assert.AreEqual("two.txt", provider.GetRecentFiles().Single().DisplayName);
    }

    [TestMethod]
    public async Task ChangeDuringAnActiveRefreshSchedulesAnotherReadWithoutOverlappingAsync()
    {
        using var gate = new ManualResetEventSlim();
        var reading = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var followUp = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = 0;
        using var source = new StubSource
        {
            BeforeRead = () =>
            {
                var count = Interlocked.Increment(ref reads);
                if (count == 2)
                {
                    reading.TrySetResult();
                    Assert.IsTrue(gate.Wait(TimeSpan.FromSeconds(5)));
                }
                else if (count == 3)
                {
                    followUp.TrySetResult();
                }
            },
        };
        using var provider = new RecentFilesProvider(source);
        try
        {
            Assert.IsTrue(await provider.WaitForInitialLoadAsync(TimeSpan.FromSeconds(5)));
            source.RaiseChanged();
            await reading.Task.WaitAsync(TimeSpan.FromSeconds(5));
            source.RaiseChanged();
            await Task.Delay(650);
            Assert.AreEqual(2, Volatile.Read(ref reads));
            gate.Set();
            await followUp.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            gate.Set();
        }
    }

    private sealed class StubSource(params IRecentFile[] files) : IRecentFileSource
    {
        public IRecentFile[] Files { get; set; } = files;
        public Action? BeforeRead { get; init; }
        public event EventHandler? Changed;

        public IReadOnlyList<IRecentFile> GetRecentFiles(CancellationToken cancellationToken)
        {
            this.BeforeRead?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            return this.Files;
        }

        public void RaiseChanged() => this.Changed?.Invoke(this, EventArgs.Empty);
        public void Dispose() { }
    }
}