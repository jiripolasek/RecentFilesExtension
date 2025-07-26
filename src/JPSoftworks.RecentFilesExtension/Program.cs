// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using JPSoftworks.CommandPalette.Extensions.Toolkit;

namespace JPSoftworks.RecentFilesExtension;

public static class Program
{
    [MTAThread]
    public static async Task Main(string[] args)
    {
        await ExtensionHostRunner.RunAsync(args, new()
        {
            PublisherMoniker = "JPSoftworks",
            ProductMoniker = "RecentFilesExtension",
            EnableEfficiencyMode = true,
            ExtensionFactories = [
                new DelegateExtensionFactory(extensionDisposedEvent => new RecentFilesExtension(extensionDisposedEvent))
            ],
#if DEBUG
            IsDebug = true,
#endif
        });
    }
}