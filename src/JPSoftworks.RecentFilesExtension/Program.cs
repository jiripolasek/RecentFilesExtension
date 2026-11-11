// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using JPSoftworks.CommandPalette.Extensions.Toolkit;
using JPSoftworks.CommandPalette.Extensions.Toolkit.Logging.Abstractions;
using JPSoftworks.RecentFilesExtension.Helpers;

namespace JPSoftworks.RecentFilesExtension;

public static class Program
{
    [MTAThread]
    public static async Task Main(string[] args)
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        using var logs = new ExtensionLogSink(
            string.IsNullOrEmpty(localAppData) ? string.Empty : Path.Combine(localAppData,
                "JPSoftworks", "RecentFilesExtension", "log.txt"),
            Path.Combine(Path.GetTempPath(), "JPSoftworks", "RecentFilesExtension", "log.txt"),
            new CompositeExtensionHostLogSink(new IExtensionHostLogSink[]
            {
                CommandPaletteExtensionHostLogSink.Instance,
                TraceExtensionHostLogSink.Instance
            }));

        void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            LogFailure(logs, "Unhandled process exception", e.ExceptionObject as Exception);
        }

        void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            LogFailure(logs, "Unobserved background task exception", e.Exception);
            e.SetObserved();
        }

        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        try
        {
            await ExtensionHostRunner.CreateBuilder(args, new()
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
            })
                .ClearDefaultLogSinks()
                .AddLogSink(logs)
                .RunAsync();
        }
        catch (Exception ex)
        {
            LogFailure(logs, "Failed to initialize the extension host", ex);
            Environment.ExitCode = 1;
        }
        finally
        {
            AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
            TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
        }
    }

    internal static void LogFailure(IExtensionHostLogSink logs, string operation, Exception? exception)
    {
        logs.LogError(nameof(Program), exception is null ? operation : $"{operation} {ExceptionDiagnostics.Format(exception)}");
    }
}