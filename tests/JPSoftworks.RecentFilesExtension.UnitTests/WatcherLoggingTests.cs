// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.ComponentModel;
using System.Reflection;
using System.Security;
using System.Security.AccessControl;
using JPSoftworks.RecentFilesExtension.Sources;
using Microsoft.Win32;

namespace JPSoftworks.RecentFilesExtension.UnitTests;

[TestClass]
[DoNotParallelize]
public sealed class WatcherLoggingTests
{
    [TestMethod]
    [DataRow("_files")]
    [DataRow("_parent")]
    public async Task RecentFolderWatcherLogsOverflowAndContinuesReportingChangesAsync(string binding)
    {
        using var logs = new TestLogCapture();
        var root = Directory.CreateTempSubdirectory("RecentFilesWatcher-");
        try
        {
            var folder = root.CreateSubdirectory("Recent").FullName;
            var time = new ManualTimeProvider();
            var changed = 0;
            TaskCompletionSource? notified = null;
            using var monitor = new RecentFolderWatcher(folder, "Recent folder", () =>
            {
                Interlocked.Increment(ref changed);
                notified?.TrySetResult();
            }, "*.lnk", timeProvider: time);
            RaiseError(GetWatcher(monitor, binding)!, new InternalBufferOverflowException("test overflow"));
            Assert.AreEqual(1, changed);
            logs.AssertLogged(nameof(InternalBufferOverflowException));
            if (binding == "_parent")
            {
                Directory.Move(folder, folder + "-old");
                Directory.CreateDirectory(folder);
                time.Advance(TimeSpan.FromSeconds(5));
            }
            else
            {
                await ExpectChangeAsync(() => Directory.Move(folder, folder + "-old"));
                await ExpectChangeAsync(() => Directory.CreateDirectory(folder));
                Assert.IsNotNull(GetWatcher(monitor, "_files"), "A healthy parent can reattach the replacement before the retry.");
            }
            await ExpectChangeAsync(() => File.WriteAllText(Path.Combine(folder, "after-overflow.lnk"), string.Empty));

            async Task ExpectChangeAsync(Action change)
            {
                notified = new(TaskCreationOptions.RunContinuationsAsynchronously);
                change();
                await notified.Task.WaitAsync(TimeSpan.FromSeconds(5));
            }
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [TestMethod]
    public void RepeatedWatcherErrorsBackOffWithoutRepeatedWarningsOrReloads()
    {
        using var logs = new TestLogCapture();
        var folder = Directory.CreateTempSubdirectory("RecentFilesWatcher-");
        try
        {
            var time = new ManualTimeProvider();
            var changes = 0;
            using var monitor = new RecentFolderWatcher(folder.FullName, "Recent folder", () => changes++, timeProvider: time);
            foreach (var seconds in new[] { 5, 10, 20, 40, 80, 160, 300, 300 })
            {
                var files = GetWatcher(monitor, "_files")!;
                RaiseError(files, new Win32Exception(50));
                RaiseError(GetWatcher(monitor, "_parent")!, new Win32Exception(50));
                Assert.AreEqual(1, changes);
                var delay = TimeSpan.FromSeconds(seconds);
                Assert.AreEqual(delay, time.NextTimerDelay);
                time.Advance(delay - TimeSpan.FromMilliseconds(1));
                Assert.IsTrue(monitor.Refresh());
                Assert.IsNull(GetWatcher(monitor, "_files"));
                Assert.IsNull(GetWatcher(monitor, "_parent"));
                time.Advance(TimeSpan.FromMilliseconds(1));
                Assert.IsNotNull(GetWatcher(monitor, "_files"));
                Assert.AreNotSame(files, GetWatcher(monitor, "_files"));
                Assert.AreEqual(1, changes);
            }
            logs.AssertLogged(nameof(Win32Exception));
            Assert.AreEqual(1, logs.Messages.Count(entry => entry.Message.Contains(nameof(Win32Exception), StringComparison.Ordinal)));

            time.Advance(TimeSpan.FromMinutes(5));
            Assert.AreEqual(2, changes, "Rescan once for changes missed during the outage.");
            Assert.IsNull(time.NextTimerDelay);
            RaiseError(GetWatcher(monitor, "_files")!, new Win32Exception(50));
            Assert.AreEqual(TimeSpan.FromSeconds(5), time.NextTimerDelay);
            Assert.AreEqual(3, changes);
            monitor.Dispose();
            time.Advance(TimeSpan.FromMinutes(5));
            Assert.AreEqual(3, changes);
            Assert.IsNull(time.NextTimerDelay);
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    private static FileSystemWatcher? GetWatcher(RecentFolderWatcher monitor, string field) =>
        (FileSystemWatcher?)typeof(RecentFolderWatcher).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(monitor);

    private static void RaiseError(FileSystemWatcher watcher, Exception error) =>
        typeof(FileSystemWatcher).GetMethod("OnError", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(watcher, [new ErrorEventArgs(error)]);

    [TestMethod]
    [DataRow("Office Recent")]
    [DataRow("Jump Lists")]
    [DataRow("Recent Items")]
    public async Task FolderSourcesFollowCreationAndReplacementAsync(string kind)
    {
        var root = Directory.CreateTempSubdirectory("RecentFilesWatcher-");
        var folder = Path.Combine(root.FullName, "Office", "history");
        try
        {
            using IRecentFileSource source = kind switch
            {
                "Office Recent" => new OfficeRecentFolderSource(folder),
                "Jump Lists" => new OfficeJumpListSource(folder),
                _ => new RecentItemsFolderSource(folder),
            };
            var file = Path.Combine(folder, kind switch
            {
                "Office Recent" => "report.url",
                "Jump Lists" => OfficeApplications.DestinationFileNames.First(),
                _ => "report.lnk",
            });
            Assert.IsEmpty(source.GetRecentFiles(CancellationToken.None));
            await ExpectChangeAsync(() => Directory.CreateDirectory(folder));
            await ExpectChangeAsync(() => File.WriteAllText(file, "first"));
            await ExpectChangeAsync(() => Directory.Move(folder, folder + "-old"));
            await ExpectChangeAsync(() => Directory.CreateDirectory(folder));
            await ExpectChangeAsync(() => File.WriteAllText(file, "second"));
            await ExpectChangeAsync(() => File.AppendAllText(file, " update"));
            await ExpectChangeAsync(() => Directory.Delete(folder, recursive: true));
            await ExpectChangeAsync(() => Directory.CreateDirectory(folder));
            await ExpectChangeAsync(() => File.WriteAllText(file, "after deletion"));

            async Task ExpectChangeAsync(Action change)
            {
                var notified = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                void OnChanged(object? sender, EventArgs e) => notified.TrySetResult();
                source.Changed += OnChanged;
                try
                {
                    change();
                    await notified.Task.WaitAsync(TimeSpan.FromSeconds(5));
                }
                finally
                {
                    source.Changed -= OnChanged;
                }
            }
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [TestMethod]
    public async Task RegistryWatcherKeepsNotifyingOtherSubscribersAfterASubscriberThrowsAsync()
    {
        using var logs = new TestLogCapture();
        var path = $@"Software\RecentFilesExtension.Tests.{Guid.NewGuid():N}";
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(path);
            using var watcher = new RegistryChangeWatcher(path, RegistryView.Default);
            watcher.Changed += (_, _) => throw new SecurityException("test denied access");
            var notifications = 0;
            watcher.Changed += (_, _) => Interlocked.Increment(ref notifications);
            for (var attempt = 0; attempt < 50 && Volatile.Read(ref notifications) == 0; attempt++)
            {
                key.SetValue("Change", attempt);
                await Task.Delay(50);
            }

            logs.AssertLogged("Failed to notify a registry change subscriber");
            Assert.IsTrue(Volatile.Read(ref notifications) > 0, "Other subscribers must receive the same change.");

            var previous = Volatile.Read(ref notifications);
            key.SetValue("Change", "next change");
            for (var attempt = 0; attempt < 100 && Volatile.Read(ref notifications) == previous; attempt++)
            {
                await Task.Delay(50);
            }
            Assert.IsTrue(Volatile.Read(ref notifications) > previous, "The watcher must receive subsequent changes.");
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
        }
    }

    [TestMethod]
    public void RegistryTimestampAccessFailureIsLogged()
    {
        using var logs = new TestLogCapture();
        var path = $@"Software\RecentFilesExtension.Tests.{Guid.NewGuid():N}";
        try
        {
            var mruPath = path + @"\User MRU\private-office-account\File MRU";
            using var created = Registry.CurrentUser.CreateSubKey(mruPath);
            using var key = Registry.CurrentUser.OpenSubKey(mruPath, RegistryKeyPermissionCheck.Default, RegistryRights.SetValue);
            Assert.IsNotNull(key);
            Assert.IsNull(NativeRegistryMethods.GetLastWriteTimeUtc(key));
            logs.AssertLogged("Failed to read the last-write time");
            Assert.IsFalse(logs.Messages.Any(entry => entry.Message.Contains("private-office-account", StringComparison.Ordinal)));
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
        }
    }

    [TestMethod]
    public void RegistryWatcherCanBeDisposedImmediatelyAndRepeatedly()
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            using var watcher = new RegistryChangeWatcher("Software", RegistryView.Default);
            watcher.Dispose();
            watcher.Dispose();
        }
    }
}