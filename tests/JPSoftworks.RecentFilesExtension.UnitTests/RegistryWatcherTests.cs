// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using JPSoftworks.RecentFilesExtension.Sources;
using Microsoft.Win32;
using System.Security.AccessControl;
using System.Security.Principal;

namespace JPSoftworks.RecentFilesExtension.UnitTests;

[TestClass]
public sealed class RegistryWatcherTests
{
    [TestMethod]
    public async Task OfficeCoalescesSubtreeWritesAndNotifiesOnlyForMruChangesAsync()
    {
        var path = $@"Software\RecentFilesExtension.Tests.{Guid.NewGuid():N}";
        try
        {
            using var root = Registry.CurrentUser.CreateSubKey(path);
            const string oldMruPath = @"16.0\Word\User MRU\First\File MRU";
            using (var initial = root.CreateSubKey(oldMruPath))
            {
                initial.SetValue("Item 1", "https://example.invalid/initial.txt");
            }
            using var options = root.CreateSubKey(@"16.0\Word\Options");
            var time = new ManualTimeProvider();
            using var source = new OfficeRecentFilesSource(path, time);
            var changes = 0;
            source.Changed += (_, _) => Interlocked.Increment(ref changes);
            var delay = TimeSpan.FromMilliseconds(250);

            for (var i = 0; i < 10; i++)
            {
                options.SetValue("Unrelated", i);
            }
            await WaitForChangeAsync(() => time.NextTimerDelay == delay);
            time.Advance(delay);
            Assert.AreEqual(0, changes);

            using (var mru = root.OpenSubKey(oldMruPath, writable: true)!)
            {
                for (var i = 0; i < 5; i++)
                {
                    mru.SetValue("Item 1", $"https://example.invalid/updated{i}.txt");
                    await Task.Delay(20);
                }
            }
            await WaitForChangeAsync(() => time.NextTimerDelay == delay);
            time.Advance(TimeSpan.FromMilliseconds(249));
            Assert.AreEqual(0, changes);
            time.Advance(TimeSpan.FromMilliseconds(1));
            Assert.AreEqual(1, changes);
            Assert.AreEqual(2, time.CallbackCount);
            Assert.AreEqual("https://example.invalid/updated4.txt", source.GetRecentFiles(CancellationToken.None).Single().TargetPath);

            root.DeleteSubKeyTree(oldMruPath);
            using var replacement = root.CreateSubKey(@"16.0\Word\User MRU\Second\File MRU");
            replacement.SetValue("Item 1", "https://example.invalid/replacement.txt");
            await WaitForChangeAsync(() => time.NextTimerDelay == delay);
            time.Advance(delay);
            Assert.AreEqual(2, changes, "Replacing an MRU path must be detected even when the key count stays the same.");
            Assert.AreEqual("https://example.invalid/replacement.txt", source.GetRecentFiles(CancellationToken.None).Single().TargetPath);
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
        }
    }

    [TestMethod]
    public async Task DisposingOfficeSourceCancelsPendingMruCheckAsync()
    {
        var path = $@"Software\RecentFilesExtension.Tests.{Guid.NewGuid():N}";
        try
        {
            using var root = Registry.CurrentUser.CreateSubKey(path);
            var time = new ManualTimeProvider();
            using var source = new OfficeRecentFilesSource(path, time);
            var changes = 0;
            source.Changed += (_, _) => Interlocked.Increment(ref changes);
            using var mru = root.CreateSubKey(@"16.0\Word\File MRU");
            await WaitForChangeAsync(() => time.NextTimerDelay.HasValue);
            source.Dispose();

            time.Advance(TimeSpan.FromMinutes(1));
            Assert.AreEqual(0, time.CallbackCount);
            Assert.AreEqual(0, changes);
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
        }
    }

    [TestMethod]
    [DoNotParallelize]
    public async Task WatcherRecoversWhenItsTargetBecomesReadableAgainAsync()
    {
        using var logs = new TestLogCapture();
        var path = $@"Software\RecentFilesExtension.Tests.{Guid.NewGuid():N}";
        try
        {
            using var root = Registry.CurrentUser.CreateSubKey(path);
            using var target = root.CreateSubKey("Target");
            var descriptor = target.GetAccessControl().GetSecurityDescriptorBinaryForm();
            var denied = target.GetAccessControl();
            using var identity = WindowsIdentity.GetCurrent();
            denied.AddAccessRule(new RegistryAccessRule(identity.User!, RegistryRights.ReadKey, AccessControlType.Deny));
            using var watcher = new RegistryChangeWatcher(path + @"\Target", RegistryView.Default);
            var changes = 0;
            var onPool = true;
            using var release = new ManualResetEventSlim();
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            watcher.Changed += (_, _) =>
            {
                onPool &= Thread.CurrentThread.IsThreadPoolThread;
                if (Interlocked.Increment(ref changes) == 1)
                {
                    entered.TrySetResult();
                    Assert.IsTrue(release.Wait(TimeSpan.FromSeconds(10)));
                }
            };
            try
            {
                target.SetValue("Signal", "first change");
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                target.SetValue("Signal", "queued before access loss");
                target.SetAccessControl(denied);
                Assert.AreEqual(TimeSpan.FromSeconds(5), RegistryChangeWatcher.GetAncestorWaitTimeout(root, "target"));
                release.Set();
                await WaitForChangeAsync(() => Volatile.Read(ref changes) >= 2);
                var previous = Volatile.Read(ref changes);
                RestoreAccess();
                await WaitForChangeAsync(() => Volatile.Read(ref changes) > previous);
                previous = Volatile.Read(ref changes);
                target.SetValue("Signal", "readable again");
                await WaitForChangeAsync(() => Volatile.Read(ref changes) > previous);
                Assert.IsTrue(onPool);
                Assert.IsFalse(logs.Messages.Any(entry => entry.Message.Contains("will retry after a failure", StringComparison.Ordinal)));
            }
            finally
            {
                release.Set();
                RestoreAccess();
            }

            void RestoreAccess()
            {
                var restored = new RegistrySecurity();
                restored.SetSecurityDescriptorBinaryForm(descriptor, AccessControlSections.Access);
                target.SetAccessControl(restored);
            }
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
        }
    }

    [TestMethod]
    public async Task WatcherCanBeDisposedFromItsThreadPoolCallbackAsync()
    {
        var path = $@"Software\RecentFilesExtension.Tests.{Guid.NewGuid():N}";
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(path);
            using var watcher = new RegistryChangeWatcher(path, RegistryView.Default);
            var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            watcher.Changed += (_, _) =>
            {
                watcher.Dispose();
                stopped.TrySetResult();
            };
            key.SetValue("Signal", 1);
            await stopped.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.IsTrue(watcher.IsDisposed);
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
        }
    }

    [TestMethod]
    public async Task MissingKeyIgnoresAncestorNoiseAndFollowsCreationDeletionAndRecreationAsync()
    {
        var path = $@"Software\RecentFilesExtension.Tests.{Guid.NewGuid():N}";
        try
        {
            using var root = Registry.CurrentUser.CreateSubKey(path);
            using var sibling = root.CreateSubKey("Sibling");
            using var watcher = new RegistryChangeWatcher($@"{path}\Office\16.0\Word\File MRU", RegistryView.Default);
            Assert.AreEqual(Timeout.InfiniteTimeSpan, RegistryChangeWatcher.GetAncestorWaitTimeout(root, "Office"));
            var changes = 0;
            watcher.Changed += (_, _) => Interlocked.Increment(ref changes);

            for (var attempt = 0; attempt < 10; attempt++)
            {
                root.SetValue("Unrelated", attempt);
                sibling.SetValue("Unrelated", attempt);
                using var other = root.CreateSubKey($"Other{attempt}");
                await Task.Delay(30);
            }
            Assert.AreEqual(0, Volatile.Read(ref changes));

            using (root.CreateSubKey("Office")) { }
            await Task.Delay(100);
            Assert.AreEqual(0, Volatile.Read(ref changes));
            using (var mru = root.CreateSubKey(@"Office\16.0\Word\File MRU"))
            {
                mru.SetValue("Item 1", "https://example.invalid/created.txt");
            }
            await WaitForChangeAsync(() => Volatile.Read(ref changes) > 0);
            await Task.Delay(100);

            var beforeDelete = Volatile.Read(ref changes);
            root.DeleteSubKeyTree("Office");
            await WaitForChangeAsync(() => Volatile.Read(ref changes) > beforeDelete);
            var beforeRecreate = Volatile.Read(ref changes);
            using (var mru = root.CreateSubKey(@"Office\16.0\Word\File MRU"))
            {
                mru.SetValue("Item 1", "https://example.invalid/recreated.txt");
            }
            await WaitForChangeAsync(() => Volatile.Read(ref changes) > beforeRecreate);
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
        }
    }

    [TestMethod]
    public async Task OfficeRefreshesOnlyForMruChangesIncludingNewAccountsAndVersionsAsync()
    {
        var path = $@"Software\RecentFilesExtension.Tests.{Guid.NewGuid():N}";
        try
        {
            using var root = Registry.CurrentUser.CreateSubKey(path);
            using var mru = root.CreateSubKey(@"16.0\Word\File MRU");
            mru.SetValue("Item 1", "https://example.invalid/first.txt");
            using var source = new OfficeRecentFilesSource(path);
            var changes = 0;
            source.Changed += (_, _) => Interlocked.Increment(ref changes);
            for (var attempt = 0; attempt < 50 && Volatile.Read(ref changes) == 0; attempt++)
            {
                mru.SetValue("Item 1", $"https://example.invalid/updated{attempt}.txt");
                await Task.Delay(30);
            }
            Assert.IsTrue(Volatile.Read(ref changes) > 0);
            await Task.Delay(100);
            var beforeNoise = Volatile.Read(ref changes);

            for (var attempt = 0; attempt < 10; attempt++)
            {
                using var options = root.CreateSubKey($@"16.0\Word\Options\Option{attempt}");
                options.SetValue("Unrelated", attempt);
                await Task.Delay(30);
            }
            Assert.AreEqual(beforeNoise, Volatile.Read(ref changes));

            foreach (var mruPath in new[] { @"16.0\Word\User MRU\Account\File MRU", @"17.0\Excel\File MRU", @"17.0\Excel\User MRU\File MRU" })
            {
                var beforeAdd = Volatile.Read(ref changes);
                using var added = root.CreateSubKey(mruPath);
                added.SetValue("Item 1", "https://example.invalid/new.txt");
                await WaitForChangeAsync(() => Volatile.Read(ref changes) > beforeAdd);
            }
            Assert.HasCount(2, source.GetRecentFiles(CancellationToken.None));
            await Task.Delay(100);
            var beforeRemove = Volatile.Read(ref changes);
            root.DeleteSubKeyTree(@"16.0\Word\User MRU");
            await WaitForChangeAsync(() => Volatile.Read(ref changes) > beforeRemove);

            for (var attempt = 0; attempt < 100; attempt++)
            {
                using (var account = root.CreateSubKey(@"16.0\Word\User MRU\Transient\File MRU"))
                {
                    account.SetValue("Item 1", "https://example.invalid/transient.txt");
                }
                root.DeleteSubKeyTree(@"16.0\Word\User MRU");
            }
            await Task.Delay(100);
            var afterChurn = Volatile.Read(ref changes);
            for (var attempt = 0; attempt < 50 && Volatile.Read(ref changes) == afterChurn; attempt++)
            {
                mru.SetValue("Item 1", $"https://example.invalid/after-churn{attempt}.txt");
                await Task.Delay(30);
            }
            Assert.IsTrue(Volatile.Read(ref changes) > afterChurn, "The watcher must survive concurrent key removal.");
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
        }
    }

    [TestMethod]
    public async Task OfficeWatcherRecoversAfterAccessToAnMruKeyIsRestoredAsync()
    {
        var path = $@"Software\RecentFilesExtension.Tests.{Guid.NewGuid():N}";
        try
        {
            using var root = Registry.CurrentUser.CreateSubKey(path);
            using (var mru = root.CreateSubKey(@"16.0\Word\File MRU"))
            {
                mru.SetValue("Item 1", "https://example.invalid/recovered.txt");
            }
            using var application = root.OpenSubKey(@"16.0\Word", RegistryKeyPermissionCheck.ReadWriteSubTree, RegistryRights.FullControl)!;
            var originalDescriptor = application.GetAccessControl().GetSecurityDescriptorBinaryForm();
            var deniedSecurity = application.GetAccessControl();
            using var identity = WindowsIdentity.GetCurrent();
            deniedSecurity.AddAccessRule(new RegistryAccessRule(identity.User!, RegistryRights.ReadKey, AccessControlType.Deny));
            try
            {
                application.SetAccessControl(deniedSecurity);
                using var source = new OfficeRecentFilesSource(path);
                var changes = 0;
                source.Changed += (_, _) => Interlocked.Increment(ref changes);
                for (var attempt = 0; attempt < 10; attempt++)
                {
                    root.SetValue("Signal", attempt);
                    await Task.Delay(50);
                }
                Assert.AreEqual(0, Volatile.Read(ref changes), "Unrelated writes must not reload an inaccessible MRU key.");

                var previous = Volatile.Read(ref changes);
                RestoreAccess();
                await WaitForChangeAsync(() => Volatile.Read(ref changes) > previous);
                Assert.AreEqual("https://example.invalid/recovered.txt", source.GetRecentFiles(CancellationToken.None).Single().TargetPath);
            }
            finally
            {
                RestoreAccess();
            }

            void RestoreAccess()
            {
                var restored = new RegistrySecurity();
                restored.SetSecurityDescriptorBinaryForm(originalDescriptor, AccessControlSections.Access);
                application.SetAccessControl(restored);
            }
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
        }
    }

    [TestMethod]
    public async Task UnreadableOfficeChildDoesNotHideReadableMruEntriesOrReloadOnSettingsWritesAsync()
    {
        var path = $@"Software\RecentFilesExtension.Tests.{Guid.NewGuid():N}";
        try
        {
            using var root = Registry.CurrentUser.CreateSubKey(path);
            using var mru = root.CreateSubKey(@"16.0\Word\File MRU");
            mru.SetValue("Item 1", "https://example.invalid/readable.txt");
            using var blocked = root.CreateSubKey(@"16.0\Common");
            var descriptor = blocked.GetAccessControl().GetSecurityDescriptorBinaryForm();
            var denied = blocked.GetAccessControl();
            using var identity = WindowsIdentity.GetCurrent();
            denied.AddAccessRule(new RegistryAccessRule(identity.User!, RegistryRights.ReadKey, AccessControlType.Deny));
            try
            {
                blocked.SetAccessControl(denied);
                using var source = new OfficeRecentFilesSource(path);
                Assert.AreEqual("https://example.invalid/readable.txt", source.GetRecentFiles(CancellationToken.None).Single().TargetPath);
                var changes = 0;
                source.Changed += (_, _) => Interlocked.Increment(ref changes);
                for (var attempt = 0; attempt < 10; attempt++)
                {
                    root.SetValue("Unrelated", attempt);
                    blocked.SetValue("Unrelated", attempt);
                    await Task.Delay(50);
                }
                Assert.AreEqual(0, Volatile.Read(ref changes));
                mru.SetValue("Item 1", "https://example.invalid/updated.txt");
                await WaitForChangeAsync(() => Volatile.Read(ref changes) > 0);
                Assert.AreEqual("https://example.invalid/updated.txt", source.GetRecentFiles(CancellationToken.None).Single().TargetPath);
            }
            finally
            {
                var restored = new RegistrySecurity();
                restored.SetSecurityDescriptorBinaryForm(descriptor, AccessControlSections.Access);
                blocked.SetAccessControl(restored);
            }
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
        }
    }

    private static async Task WaitForChangeAsync(Func<bool> changed)
    {
        for (var attempt = 0; attempt < 200 && !changed(); attempt++)
        {
            await Task.Delay(50);
        }
        Assert.IsTrue(changed(), "Expected a registry change notification.");
    }
}