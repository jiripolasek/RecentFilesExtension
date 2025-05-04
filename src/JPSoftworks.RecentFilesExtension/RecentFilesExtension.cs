// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System;
using System.Runtime.InteropServices;
using System.Threading;
using JPSoftworks.RecentFilesExtension.Helpers;

namespace JPSoftworks.RecentFilesExtension;

[Guid("2a8e7706-3eaa-4c83-af51-4f764073fb51")]
public sealed partial class RecentFilesExtension : IExtension, IDisposable
{
    private readonly ManualResetEvent _extensionDisposedEvent;

    private readonly RecentFilesExtensionCommandsProvider _provider = new();

    public RecentFilesExtension(ManualResetEvent extensionDisposedEvent)
    {
        this._extensionDisposedEvent = extensionDisposedEvent;

        Logger.Initialize();
    }

    public object? GetProvider(ProviderType providerType)
    {
        return providerType switch
        {
            ProviderType.Commands => this._provider,
            _ => null
        };
    }

    public void Dispose()
    {
        this._extensionDisposedEvent.Set();
    }
}