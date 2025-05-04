// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

using System;
using System.Globalization;
using System.IO;
using Serilog;

namespace JPSoftworks.RecentFilesExtension.Helpers;

public static class Logger
{
    public static void Initialize()
    {
        var logFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "JPSoftworks", "RecentFilesExtension", "log.txt");
        var logDirectory = Path.GetDirectoryName(logFile);
        if (logDirectory != null && !Directory.Exists(logDirectory))
        {
            Directory.CreateDirectory(logDirectory);
        }

        Log.Logger = new LoggerConfiguration()
            .WriteTo.File(logFile, buffered: false, rollingInterval: RollingInterval.Day)
            .CreateLogger();
        Log.Logger.Information("Logger initialized.");
    }


    public static void LogDebug(string message)
    {
        Log.Logger.Debug(message);
#if DEBUG
        ExtensionHost.LogMessage(new LogMessage(message) { State = MessageState.Info });
#endif
    }

    public static void LogInformation(string message)
    {
        Log.Logger.Information(message);
        ExtensionHost.LogMessage(new LogMessage(message) { State = MessageState.Info });
    }

    public static void LogError(string message)
    {
        Log.Logger.Error(message);
        ExtensionHost.LogMessage(new LogMessage(message) { State = MessageState.Error });
    }

    public static void LogWarning(string message)
    {
        Log.Logger.Warning(message);
        ExtensionHost.LogMessage(new LogMessage(message) { State = MessageState.Warning });
    }

    public static void LogError(Exception exception)
    {
        var message = string.Format(CultureInfo.InvariantCulture, "{0}: {1}", exception.GetType().Name,
            exception.Message);
        Log.Logger.Error(exception, message);
        ExtensionHost.LogMessage(new LogMessage(message) { State = MessageState.Error });
    }

    public static void LogError(string message, Exception exception)
    {
        var formattedMessage = string.Format(CultureInfo.InvariantCulture, "{0}: {1}", message, exception.Message);
        Log.Logger.Error(exception, formattedMessage);
        ExtensionHost.LogMessage(new LogMessage(formattedMessage) { State = MessageState.Error });
    }
}