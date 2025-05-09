// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using JPSoftworks.RecentFilesExtension.Helpers;
using Shmuelie.WinRTServer;
using Shmuelie.WinRTServer.CsWinRT;

namespace JPSoftworks.RecentFilesExtension;

public static class Program
{
    [MTAThread]
    public static async Task Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "-RegisterProcessAsComServer")
        {
            await using ComServer server = new();
            ManualResetEvent extensionDisposedEvent = new(false);

            // We are instantiating an extension instance once above, and returning it every time the callback in RegisterExtension below is called.
            // This makes sure that only one instance of SampleExtension is alive, which is returned every time the host asks for the IExtension object.
            // If you want to instantiate a new instance each time the host asks, create the new instance inside the delegate.
            RecentFilesExtension extensionInstance = new(extensionDisposedEvent);
            server.RegisterClass<RecentFilesExtension, IExtension>(() => extensionInstance);
            server.Start();

            // This will make the main thread wait until the event is signaled by the extension class.
            // Since we have single instance of the extension object, we exit as soon as it is disposed.
            extensionDisposedEvent.WaitOne();

            // Bye, bye
            server.Stop();
        }
        else
        {
            // ReSharper disable once LocalizableElement
            Console.WriteLine("Not being launched as a Extension... exiting");

            await StartupHelper.HandleDirectLaunch();
        }
    }
}