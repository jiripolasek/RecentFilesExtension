// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using JPSoftworks.CommandPalette.Extensions.Toolkit;
using JPSoftworks.CommandPalette.Extensions.Toolkit.Logging.Abstractions;
using JPSoftworks.RecentFilesExtension.Helpers;

namespace JPSoftworks.RecentFilesExtension;

public static class Program
{
    internal static string LogDirectoryPath
    {
        get
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return string.IsNullOrEmpty(localAppData) ? string.Empty : Path.Combine(localAppData, "JPSoftworks", "RecentFilesExtension");
        }
    }

    internal static string FallbackLogDirectoryPath => Path.Combine(Path.GetTempPath(), "JPSoftworks", "RecentFilesExtension");

    [MTAThread]
    public static async Task Main(string[] args)
    {
        var logDirectory = LogDirectoryPath;
        using var logs = new ExtensionLogSink(
            string.IsNullOrEmpty(logDirectory) ? string.Empty : Path.Combine(logDirectory, "log.txt"),
            Path.Combine(FallbackLogDirectoryPath, "log.txt"),
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
            LogStartup(logs);
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

    internal static void LogStartup(IExtensionHostLogSink logs)
    {
        var assembly = typeof(Program).Assembly;
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString();
        var configuration = assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration;
        logs.Write(new(DateTimeOffset.Now, ExtensionHostLogLevel.Information, nameof(Program), 0,
            $"Starting RecentFilesExtension | Version: {version}, Build: {configuration}, " +
            $"OS: {RuntimeInformation.OSDescription}, OS architecture: {RuntimeInformation.OSArchitecture}, " +
            $"Process architecture: {RuntimeInformation.ProcessArchitecture}, Runtime: {RuntimeInformation.FrameworkDescription}, " +
            $"Culture: {CultureInfo.CurrentCulture.Name}, UI culture: {CultureInfo.CurrentUICulture.Name}, " +
            $"Time zone: {TimeZoneInfo.Local.Id}, Process ID: {Environment.ProcessId}"));
    }

    internal static void LogFailure(IExtensionHostLogSink logs, string operation, Exception? exception)
    {
        logs.LogError(nameof(Program), exception is null ? operation : $"{operation} {ExceptionDiagnostics.Format(exception)}");
    }
}