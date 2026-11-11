// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using JPSoftworks.RecentFilesExtension.Helpers;
using JPSoftworks.RecentFilesExtension.Pages;
using JPSoftworks.RecentFilesExtension.Sources;

namespace JPSoftworks.RecentFilesExtension.UnitTests;

[TestClass]
public sealed partial class ShellLinkHelperTests
{
    private const string Arguments = "/test";
    private const string Description = "Saved description";

    [TestMethod]
    [DoNotParallelize]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void FolderListingEvictsDeletedShortcutsIncludingFailedReads(bool office, bool corrupt)
    {
        using var logs = new TestLogCapture();
        var directory = Directory.CreateTempSubdirectory("RecentFilesCacheEviction-");
        try
        {
            var link = Path.Combine(directory.FullName, "replace.lnk");
            var first = Path.Combine(directory.FullName, "first.txt");
            var second = Path.Combine(directory.FullName, "second.txt");
            File.WriteAllText(first, string.Empty);
            File.WriteAllText(second, string.Empty);
            if (corrupt)
            {
                File.WriteAllText(link, "not a shell link");
            }
            else
            {
                CreateShortcut(first, link);
            }
            var timestamp = File.GetLastWriteTimeUtc(link);
            using IRecentFileSource source = office ? new OfficeRecentFolderSource(directory.FullName) : new RecentItemsFolderSource(directory.FullName);
            Assert.HasCount(corrupt ? 0 : 1, source.GetRecentFiles(CancellationToken.None));
            File.Delete(link);
            Assert.IsEmpty(source.GetRecentFiles(CancellationToken.None));
            CreateShortcut(second, link);
            File.SetLastWriteTimeUtc(link, timestamp);
            Assert.AreEqual(second, source.GetRecentFiles(CancellationToken.None).Single().TargetPath);
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
    public void FolderSourcesLogBrokenShortcutsOnceAndRecoverWhenTheyChange(bool office)
    {
        using var logs = new TestLogCapture();
        var directory = Directory.CreateTempSubdirectory("RecentFilesBrokenLink-");
        try
        {
            var path = Path.Combine(directory.FullName, "broken.lnk");
            File.WriteAllText(path, "not a shell link");
            using IRecentFileSource source = office ? new OfficeRecentFolderSource(directory.FullName) : new RecentItemsFolderSource(directory.FullName);
            Assert.IsEmpty(source.GetRecentFiles(CancellationToken.None));
            logs.AssertLogged("Failed to retrieve a shell link");
            Assert.IsEmpty(source.GetRecentFiles(CancellationToken.None));
            var target = Path.Combine(directory.FullName, "recovered.txt");
            File.WriteAllText(target, string.Empty);
            var timestamp = File.GetLastWriteTimeUtc(path);
            CreateShortcut(target, path);
            File.SetLastWriteTimeUtc(path, timestamp.AddSeconds(1));
            Assert.AreEqual(target, source.GetRecentFiles(CancellationToken.None).Single().TargetPath);
            Assert.AreEqual(1, logs.Messages.Count(entry => entry.Message.Contains("Failed to retrieve a shell link", StringComparison.Ordinal)));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [TestMethod]
    [DoNotParallelize]
    public void FailedShortcutReadsRetryAfterFiveMinutesWithoutRepeatedWarnings()
    {
        using var logs = new TestLogCapture();
        var time = new CacheTimeProvider();
        var cache = new CachedShellLinksHelper(time);
        var directory = Directory.CreateTempSubdirectory("RecentFilesLinkRetry-");
        try
        {
            var path = Path.Combine(directory.FullName, "retry.lnk");
            File.WriteAllText(path, "not a shell link");
            var file = new FileInfo(path);
            var timestamp = file.LastWriteTimeUtc;
            Assert.IsFalse(cache.TryGetShellLink(new FileInfo(path), out _));
            time.Now = time.Now.AddMinutes(5);
            Assert.IsFalse(cache.TryGetShellLink(new FileInfo(path), out _));
            var target = Path.Combine(directory.FullName, "recovered.txt");
            File.WriteAllText(target, string.Empty);
            CreateShortcut(target, path);
            File.SetLastWriteTimeUtc(path, timestamp);
            time.Now = time.Now.AddMinutes(4);
            Assert.IsFalse(cache.TryGetShellLink(new FileInfo(path), out _));
            time.Now = time.Now.AddMinutes(1);
            Assert.IsTrue(cache.TryGetShellLink(new FileInfo(path), out var recovered));
            Assert.AreEqual(target, recovered.TargetPath);
            logs.AssertLogged("Failed to retrieve a shell link");
            Assert.AreEqual(1, logs.Messages.Count(entry => entry.Message.Contains("Failed to retrieve a shell link", StringComparison.Ordinal)));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private sealed class CacheTimeProvider : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => this.Now;
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public void ReadersPreserveLongTargets(bool isDirectory, bool fromStream)
    {
        var directory = Directory.CreateTempSubdirectory("RecentFileLongPath-");
        try
        {
            var parent = directory.FullName;
            while (parent.Length < 320)
            {
                parent = Path.Combine(parent, new string('p', 40));
            }

            Directory.CreateDirectory(parent);
            var target = Path.Combine(parent, "Reports.2026");
            if (isDirectory)
            {
                Directory.CreateDirectory(target);
            }
            else
            {
                File.WriteAllText(target, string.Empty);
            }

            var shortcut = Path.Combine(directory.FullName, "Reports.lnk");
            CreateShortcut(target, shortcut);
            using var helper = new ShellLinkHelper();

            Assert.IsTrue(target.Length > 260);
            Assert.AreEqual(target, ReadTarget(helper, shortcut, fromStream));
            if (!fromStream)
            {
                Assert.AreEqual(isDirectory, helper.IsDirectory);
                Assert.IsTrue(new CachedShellLinksHelper().TryGetShellLink(new FileInfo(shortcut), out var recentFile));
                Assert.AreEqual(target, recentFile.TargetPath);
                Assert.AreEqual(isDirectory, recentFile.IsDirectory());
            }

            if (isDirectory)
            {
                Directory.Delete(target);
            }
            else
            {
                File.Delete(target);
            }

            Assert.AreEqual(target, ReadTarget(helper, shortcut, fromStream));
            if (!fromStream)
            {
                Assert.AreEqual(isDirectory, helper.IsDirectory);
            }
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ReadersKeepRawPathFallbackWithoutAnIdList(bool fromStream)
    {
        var directory = Directory.CreateTempSubdirectory("RecentFileRawPath-");
        try
        {
            const string target = @"%SystemRoot%\System32\notepad.exe";
            var shortcut = Path.Combine(directory.FullName, "report.lnk");
            CreateShortcut(target, shortcut, useIdList: false);

            var bytes = File.ReadAllBytes(shortcut);
            var flags = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(20));
            Assert.AreEqual(0x201u, flags & 0x201u, "The fixture needs an ID list and an environment-variable target.");
            var idListLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(76));
            // Remove only LinkTargetIDList, retaining the environment block and saved strings.
            var withoutIdList = bytes[..76].Concat(bytes[(78 + idListLength)..]).ToArray();
            BinaryPrimitives.WriteUInt32LittleEndian(withoutIdList.AsSpan(20), flags & ~1u);
            File.WriteAllBytes(shortcut, withoutIdList);

            using var helper = new ShellLinkHelper();
            Assert.AreEqual(target, ReadTarget(helper, shortcut, fromStream));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    internal static unsafe void CreateShortcut(string target, string shortcut, bool useIdList = true)
    {
        using var link = ShellLinkComObject.Create();
        if (useIdList)
        {
            Marshal.ThrowExceptionForHR(SHParseDisplayName(target, 0, out var itemIdList, 0, out _));
            try
            {
                Marshal.ThrowExceptionForHR(link.ShellLink.SetIDList(itemIdList));
            }
            finally
            {
                Marshal.FreeCoTaskMem(itemIdList);
            }
        }
        else
        {
            fixed (char* targetPath = target)
            {
                Marshal.ThrowExceptionForHR(link.ShellLink.SetPath(targetPath));
            }
        }

        fixed (char* arguments = Arguments)
        fixed (char* description = Description)
        fixed (char* path = shortcut)
        {
            Marshal.ThrowExceptionForHR(link.ShellLink.SetArguments(arguments));
            Marshal.ThrowExceptionForHR(link.ShellLink.SetDescription(description));
            Marshal.ThrowExceptionForHR(link.PersistFile.Save(path, 1));
        }

        if (useIdList)
        {
            // SetIDList leaves the header's saved file attributes empty for long targets.
            var bytes = File.ReadAllBytes(shortcut);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(24), (uint)File.GetAttributes(target));
            File.WriteAllBytes(shortcut, bytes);
        }
    }

    private static unsafe string ReadTarget(ShellLinkHelper helper, string shortcut, bool fromStream)
    {
        if (!fromStream)
        {
            return helper.LoadTargetPath(shortcut);
        }

        var bytes = File.ReadAllBytes(shortcut);
        nint streamPointer;
        fixed (byte* buffer = bytes)
        {
            streamPointer = SHCreateMemStream(buffer, (uint)bytes.Length);
        }

        Assert.AreNotEqual(nint.Zero, streamPointer);
        IStructuredStorageStream? stream = null;
        try
        {
            stream = UniqueComInterfaceMarshaller<IStructuredStorageStream>.ConvertToManaged((void*)streamPointer)!;
            var data = helper.LoadTargetData(stream);
            Assert.AreEqual(Arguments, data.Arguments);
            Assert.AreEqual(Description, data.Description);
            return data.TargetPath;
        }
        finally
        {
            ((ComObject?)(object?)stream)?.FinalRelease();
            Marshal.Release(streamPointer);
        }
    }

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHParseDisplayName(string path, nint bindContext, out nint itemIdList, uint attributesIn, out uint attributesOut);

    [LibraryImport("shlwapi.dll")]
    private static unsafe partial nint SHCreateMemStream(byte* bytes, uint length);
}