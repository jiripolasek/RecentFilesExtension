// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.Runtime.InteropServices;
using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.InteropServices.Marshalling;
using JPSoftworks.RecentFilesExtension.Helpers;
using JPSoftworks.RecentFilesExtension.Sources;

namespace JPSoftworks.RecentFilesExtension.UnitTests;

[TestClass]
public sealed partial class ComLifetimeTests
{
    [TestMethod]
    public void ShellLinkDisposalReleasesTheNativeObjectImmediately()
    {
        using var link = ShellLinkComObject.Create();
        link.Dispose();
        Assert.ThrowsExactly<ObjectDisposedException>(() => link.PersistFile.IsDirty());
        link.Dispose();
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void StorageDisposalReleasesStreamAndFileLocksWithoutGarbageCollection(bool readerThrows)
    {
        var directory = Directory.CreateTempSubdirectory("RecentFilesComLifetime-");
        try
        {
            var path = Path.Combine(directory.FullName, "test.automaticDestinations-ms");
            CreateStorage(path);
            using (var storage = StructuredStorageFile.Open(path))
            {
                if (readerThrows)
                {
                    Assert.ThrowsExactly<InvalidOperationException>(() =>
                        storage.UseStream<int>("test", _ => throw new InvalidOperationException("test reader failure")));
                }
                else
                {
                    Assert.IsEmpty(storage.ReadStream("test", 1024));
                }

                Assert.IsEmpty(storage.ReadStream("test", 1024));
            }

            using var writer = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            Assert.IsTrue(writer.CanWrite);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [TestMethod]
    [DoNotParallelize]
    [DataRow(false)]
    [DataRow(true)]
    public void BrokenJumpListsAndEntriesLogOncePerFileVersion(bool brokenEntries)
    {
        using var logs = new TestLogCapture();
        var directory = Directory.CreateTempSubdirectory("RecentFilesJumpListFailures-");
        try
        {
            var path = Path.Combine(directory.FullName, OfficeApplications.DestinationFileNames.First());
            if (brokenEntries)
            {
                var data = CreateDestList(2);
                Assert.IsTrue(DestListParser.TryParse(data, out var parsed));
                Assert.HasCount(2, parsed.Entries);
                CreateStorage(path, data);
            }
            else
            {
                File.WriteAllText(path, "invalid storage");
            }
            var time = new ManualTimeProvider();
            using var source = new OfficeJumpListSource(directory.FullName, time);
            Assert.IsEmpty(source.GetRecentFiles(CancellationToken.None));
            logs.AssertLogged("Failed to read an Office Jump List");
            time.Advance(TimeSpan.FromMinutes(5));
            Assert.IsEmpty(source.GetRecentFiles(CancellationToken.None));
            Assert.AreEqual(1, FailureCount());
            File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddSeconds(1));
            Assert.IsEmpty(source.GetRecentFiles(CancellationToken.None));
            Assert.IsTrue(SpinWait.SpinUntil(() => FailureCount() >= 2, TimeSpan.FromSeconds(5)));
            Assert.AreEqual(2, FailureCount());
        }
        finally
        {
            directory.Delete(recursive: true);
        }

        int FailureCount() => logs.Messages.Count(entry => entry.Message.Contains("Failed to read an Office Jump List", StringComparison.Ordinal));
    }

    [TestMethod]
    public void JumpListCacheKeepsPartialResultsAndRetriesAfterFiveMinutesOrATimestampChange()
    {
        var directory = Directory.CreateTempSubdirectory("RecentFilesJumpListCache-");
        try
        {
            var path = Path.Combine(directory.FullName, OfficeApplications.DestinationFileNames.First());
            var targets = new[] { Path.Combine(directory.FullName, "first.txt"), Path.Combine(directory.FullName, "second.txt") };
            var links = targets.Select(target =>
            {
                File.WriteAllText(target, string.Empty);
                var shortcut = target + ".lnk";
                ShellLinkHelperTests.CreateShortcut(target, shortcut);
                return File.ReadAllBytes(shortcut);
            }).ToArray();
            var timestamp = DateTime.UtcNow.AddMinutes(-10);
            var time = new ManualTimeProvider();
            using var source = new OfficeJumpListSource(directory.FullName, time);

            WriteList(2, links[0]);
            CollectionAssert.AreEqual(targets[..1], ReadTargets());
            WriteList(2, links);
            time.Advance(TimeSpan.FromMinutes(5) - TimeSpan.FromTicks(1));
            CollectionAssert.AreEqual(targets[..1], ReadTargets());
            using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                time.Advance(TimeSpan.FromTicks(1));
                CollectionAssert.AreEqual(targets[..1], ReadTargets());
            }
            CollectionAssert.AreEqual(targets[..1], ReadTargets());
            time.Advance(TimeSpan.FromMinutes(5));
            CollectionAssert.AreEquivalent(targets, ReadTargets());
            using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                time.Advance(TimeSpan.FromMinutes(10));
                CollectionAssert.AreEquivalent(targets, ReadTargets());
            }

            timestamp = timestamp.AddMinutes(1);
            WriteList(2, links[1]);
            using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                CollectionAssert.AreEquivalent(targets, ReadTargets());
            }
            time.Advance(TimeSpan.FromMinutes(5));
            CollectionAssert.AreEqual(targets[1..], ReadTargets());
            timestamp = timestamp.AddSeconds(1);
            WriteList(2, links);
            CollectionAssert.AreEquivalent(targets, ReadTargets());
            File.Delete(path);
            Assert.IsEmpty(ReadTargets());
            WriteList(1, links[0]);
            CollectionAssert.AreEqual(targets[..1], ReadTargets());

            string[] ReadTargets() => source.GetRecentFiles(CancellationToken.None).Select(static file => file.TargetPath).ToArray();
            void WriteList(int entryCount, params byte[][] streams)
            {
                CreateStorage(path, CreateDestList(entryCount), streams);
                File.SetLastWriteTimeUtc(path, timestamp);
            }
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static byte[] CreateDestList(int entryCount)
    {
        var data = new byte[32 + entryCount * 114];
        BinaryPrimitives.WriteUInt32LittleEndian(data, 1);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), (uint)entryCount);
        for (var index = 0; index < entryCount; index++)
        {
            var entry = data.AsSpan(32 + index * 114);
            BinaryPrimitives.WriteUInt32LittleEndian(entry[88..], (uint)index + 1);
            BinaryPrimitives.WriteInt64LittleEndian(entry[100..], DateTime.UtcNow.ToFileTimeUtc());
        }
        return data;
    }

    private static unsafe void CreateStorage(string path, byte[]? destList = null, params byte[][] links)
    {
        var initialized = ShellLinkNativeMethods.CoInitializeEx(0, CoInit.MultiThreaded) >= 0;
        try
        {
            const uint createReadWriteExclusive = 0x1012;
            Marshal.ThrowExceptionForHR(StgCreateDocfile(path, createReadWriteExclusive, 0, out var storage));
            try
            {
                WriteStream(destList is null ? "test" : "DestList", destList);
                for (var index = 0; index < links.Length; index++)
                {
                    WriteStream((index + 1).ToString("x", CultureInfo.InvariantCulture), links[index]);
                }
                Marshal.ThrowExceptionForHR(storage.Commit(0));

                void WriteStream(string streamName, byte[]? data)
                {
                    nint stream;
                    fixed (char* name = streamName)
                    {
                        Marshal.ThrowExceptionForHR(storage.CreateStream(name, createReadWriteExclusive, 0, 0, &stream));
                    }
                    try
                    {
                        if (data is null)
                        {
                            return;
                        }
                        var writer = UniqueComInterfaceMarshaller<IStructuredStorageStream>.ConvertToManaged((void*)stream)!;
                        try
                        {
                            fixed (byte* buffer = data)
                            {
                                uint written;
                                Marshal.ThrowExceptionForHR(writer.Write(buffer, (uint)data.Length, &written));
                                Assert.AreEqual((uint)data.Length, written);
                            }
                        }
                        finally
                        {
                            ((ComObject)(object)writer).FinalRelease();
                        }
                    }
                    finally
                    {
                        Marshal.Release(stream);
                    }
                }
            }
            finally
            {
                ((ComObject)(object)storage).FinalRelease();
            }
        }
        finally
        {
            if (initialized)
            {
                ShellLinkNativeMethods.CoUninitialize();
            }
        }
    }

    [LibraryImport("ole32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int StgCreateDocfile(
        string name, uint mode, uint reserved,
        [MarshalUsing(typeof(UniqueComInterfaceMarshaller<IStructuredStorage>))] out IStructuredStorage storage);
}