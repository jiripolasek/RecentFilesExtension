// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.Runtime.InteropServices;
using JPSoftworks.RecentFilesExtension.Helpers;

namespace JPSoftworks.RecentFilesExtension;

[Guid("2a8e7706-3eaa-4c83-af51-4f764073fb51")]
public sealed partial class RecentFilesExtension : IExtension, IDisposable
{
    private readonly ManualResetEvent _extensionDisposedEvent;

    private readonly Dictionary<ShellIconSupport, RecentFilesExtensionCommandsProvider> _providers = [];
    private SettingsManager? _settings;
    private bool _isDisposed;

    public RecentFilesExtension(ManualResetEvent extensionDisposedEvent)
    {
        this._extensionDisposedEvent = extensionDisposedEvent;
    }

    public object? GetProvider(ProviderType providerType)
    {
        if (providerType != ProviderType.Commands)
        {
            return null;
        }

        var support = CmdPalHost.GetCallingShellIconSupport();
        // The COM server shares this extension between hosts that can support different icon protocols.
        lock (this._providers)
        {
            if (this._isDisposed)
            {
                return null;
            }

            if (!this._providers.TryGetValue(support, out var provider))
            {
                provider = new(this._settings ??= new(), support);
                this._providers.Add(support, provider);
            }

            return provider;
        }
    }

    public void Dispose()
    {
        lock (this._providers)
        {
            if (this._isDisposed)
            {
                return;
            }

            this._isDisposed = true;
            foreach (var provider in this._providers.Values)
            {
                provider.Dispose();
            }

            this._providers.Clear();
        }

        this._extensionDisposedEvent.Set();
    }
}