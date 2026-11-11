// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.ComponentModel;
using JPSoftworks.RecentFilesExtension.Commands;
using JPSoftworks.RecentFilesExtension.Model;
using Microsoft.CommandPalette.Extensions;

namespace JPSoftworks.RecentFilesExtension.UnitTests;

[TestClass]
[DoNotParallelize]
public sealed class CommandLoggingTests
{
    [TestMethod]
    [DataRow(0, CommandResultKind.Dismiss)]
    [DataRow(1223, CommandResultKind.Dismiss)]
    [DataRow(5, CommandResultKind.KeepOpen)]
    public void OpenWithDistinguishesCancellationFromFailure(int errorCode, CommandResultKind expectedResult)
    {
        using var logs = new TestLogCapture();
        var command = new OpenWithCommand(new RecentPathFile(@"C:\test\file.txt", DateTimeOffset.UtcNow), _ =>
        {
            if (errorCode != 0)
            {
                throw new Win32Exception(errorCode);
            }
        });

        Assert.AreEqual(expectedResult, command.Invoke().Kind);
        if (expectedResult == CommandResultKind.KeepOpen)
        {
            logs.AssertLogged("Unable to show Open with");
        }
        else
        {
            Assert.IsEmpty(logs.Messages);
        }
    }

    [TestMethod]
    public void ClipboardWriteThatSilentlyFailsIsLogged()
    {
        using var logs = new TestLogCapture();
        var command = new CopyPathCommand(
            new RecentPathFile(@"C:\test\copy.txt", DateTimeOffset.UtcNow),
            _ => { }, () => "previous clipboard text");

        Assert.AreEqual(CommandResultKind.KeepOpen, command.Invoke().Kind);
        logs.AssertLogged("The clipboard did not retain the copied path");
        Assert.IsFalse(logs.Messages.Any(entry => entry.Message.Contains("Failed to copy", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void ClipboardExceptionIsLogged()
    {
        using var logs = new TestLogCapture();
        var command = new CopyPathCommand(
            new RecentPathFile(@"C:\test\copy.txt", DateTimeOffset.UtcNow),
            _ => throw new InvalidOperationException("test clipboard failure"), () => string.Empty);

        Assert.AreEqual(CommandResultKind.KeepOpen, command.Invoke().Kind);
        logs.AssertLogged("Failed to copy the recent file path to the clipboard");
        Assert.IsFalse(logs.Messages.Any(entry => entry.Message.Contains("test clipboard failure", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void OpenWithFailureIsLoggedAndKeepsThePaletteOpen()
    {
        using var logs = new TestLogCapture();
        var item = new RecentShortcutFile(new FileInfo(@"C:\test.lnk"), "test", string.Empty, false);
        var command = new OpenWithCommand(item);

        Assert.AreEqual(CommandResultKind.KeepOpen, command.Invoke().Kind);
        logs.AssertLogged("Unable to show Open with");
    }
}