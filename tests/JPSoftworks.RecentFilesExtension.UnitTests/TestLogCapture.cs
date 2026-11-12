// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.Collections.Concurrent;
using System.Reflection;
using JPSoftworks.CommandPalette.Extensions.Toolkit;
using JPSoftworks.CommandPalette.Extensions.Toolkit.Logging;
using JPSoftworks.CommandPalette.Extensions.Toolkit.Logging.Abstractions;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Windows.Foundation;

namespace JPSoftworks.RecentFilesExtension.UnitTests;

internal sealed class TestLogCapture : IExtensionHost, IDisposable
{
    private readonly IExtensionHost? _previousHost = ExtensionHost.Host;
    private readonly FieldInfo _debugEnabled;
    private readonly FieldInfo _sink;
    private readonly object _syncRoot;
    private readonly object? _previousSink;
    private readonly bool _previousDebugEnabled;
    public ConcurrentQueue<ILogMessage> Messages { get; } = new();

    public TestLogCapture(bool includeDebug = false, IExtensionLogSink? sink = null)
    {
        var bridge = typeof(ExtensionHostRunner).Assembly.GetType(
            "JPSoftworks.CommandPalette.Extensions.Toolkit.Logging.LegacyLoggerBridge", throwOnError: true)!;
        this._debugEnabled = RequireField("_isDebugEnabled");
        this._sink = RequireField("_sink");
        this._syncRoot = RequireField("SyncRoot").GetValue(null)!;
        lock (this._syncRoot)
        {
            this._previousDebugEnabled = (bool)this._debugEnabled.GetValue(null)!;
            this._previousSink = this._sink.GetValue(null);
            this._debugEnabled.SetValue(null, includeDebug);
            this._sink.SetValue(null, sink ?? CommandPaletteLogSink.Instance);
        }
        ExtensionHost.Initialize(this);

        FieldInfo RequireField(string name) => bridge.GetField(name, BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"The toolkit logging test hook changed: missing {name}.");
    }

    public IAsyncAction LogMessage(ILogMessage message)
    {
        this.Messages.Enqueue(message);
        return Task.CompletedTask.AsAsyncAction();
    }

    public IAsyncAction ShowStatus(IStatusMessage message, StatusContext context) => Task.CompletedTask.AsAsyncAction();

    public IAsyncAction HideStatus(IStatusMessage message) => Task.CompletedTask.AsAsyncAction();

    public void AssertLogged(string message)
    {
        Assert.IsTrue(SpinWait.SpinUntil(
            () => this.Messages.Any(entry => entry.Message.Contains(message, StringComparison.Ordinal)),
            TimeSpan.FromSeconds(5)), $"Expected log entry containing: {message}");
    }

    public void Dispose()
    {
        lock (this._syncRoot)
        {
            this._sink.SetValue(null, this._previousSink);
            this._debugEnabled.SetValue(null, this._previousDebugEnabled);
        }
        ExtensionHost.Initialize(this._previousHost!);
    }
}