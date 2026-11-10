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

namespace JPSoftworks.RecentFilesExtension.UnitTests;

[TestClass]
public sealed partial class ShellLinkHelperTests
{
    private const string Arguments = "/test";
    private const string Description = "Saved description";

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

    private static unsafe void CreateShortcut(string target, string shortcut, bool useIdList = true)
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
            stream = ComInterfaceMarshaller<IStructuredStorageStream>.ConvertToManaged((void*)streamPointer)!;
            var data = helper.LoadTargetData(stream);
            Assert.AreEqual(Arguments, data.Arguments);
            Assert.AreEqual(Description, data.Description);
            return data.TargetPath;
        }
        finally
        {
            (stream as IDisposable)?.Dispose();
            Marshal.Release(streamPointer);
        }
    }

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHParseDisplayName(string path, nint bindContext, out nint itemIdList, uint attributesIn, out uint attributesOut);

    [LibraryImport("shlwapi.dll")]
    private static unsafe partial nint SHCreateMemStream(byte* bytes, uint length);
}