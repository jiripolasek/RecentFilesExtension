// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using JPSoftworks.CommandPalette.Extensions.Toolkit.Logging.Abstractions;

namespace JPSoftworks.RecentFilesExtension.Helpers;

internal sealed partial class ExtensionLogSink(
    string logPath,
    string fallbackPath,
    IExtensionHostLogSink diagnostics) : IExtensionHostLogSink, IDisposable
{
    private readonly Lock _syncRoot = new();
    private DailyFileExtensionHostLogSink? _file;
    private DailyFileExtensionHostLogSink? _fallback;
    private bool _fileFailed;
    private bool _fallbackFailed;
    private bool _isDisposed;

    public void Write(ExtensionHostLogEntry entry)
    {
        List<ExtensionHostLogEntry> failures = [];
        lock (this._syncRoot)
        {
            if (this._isDisposed)
            {
                return;
            }

            if (entry.Exception is { } exception)
            {
                entry = new(entry.Timestamp, entry.Level, entry.Category, entry.EventId,
                    $"{entry.Message} {ExceptionDiagnostics.Format(exception)}");
            }

            try
            {
                (this._file ??= new(logPath)).Write(entry);
                this._fileFailed = false;
            }
            catch (Exception ex)
            {
                if (!this._fileFailed)
                {
                    this._fileFailed = true;
                    var failure = Failure("Cannot write log; using fallback", ex);
                    failures.Add(failure);
                    this.WriteFallback(failure, failures);
                }

                this.WriteFallback(entry, failures);
            }
        }
        foreach (var failure in failures)
        {
            this.WriteDiagnostics(failure);
        }
        this.WriteDiagnostics(entry);
    }

    private void WriteFallback(ExtensionHostLogEntry entry, List<ExtensionHostLogEntry> failures)
    {
        try
        {
            (this._fallback ??= new(fallbackPath)).Write(entry);
            this._fallbackFailed = false;
        }
        catch (Exception ex)
        {
            if (!this._fallbackFailed)
            {
                this._fallbackFailed = true;
                failures.Add(Failure("Cannot write fallback log", ex));
            }
        }
    }

    public void Dispose()
    {
        List<ExtensionHostLogEntry> failures = [];
        lock (this._syncRoot)
        {
            if (this._isDisposed)
            {
                return;
            }

            try
            {
                this._file?.Dispose();
            }
            catch (Exception ex)
            {
                var failure = Failure("Cannot close log", ex);
                failures.Add(failure);
                this.WriteFallback(failure, failures);
            }

            try
            {
                this._fallback?.Dispose();
            }
            catch (Exception ex)
            {
                failures.Add(Failure("Cannot close fallback log", ex));
            }

            this._isDisposed = true;
        }
        foreach (var failure in failures)
        {
            this.WriteDiagnostics(failure);
        }
    }

    private void WriteDiagnostics(ExtensionHostLogEntry entry)
    {
        try
        {
            diagnostics.Write(entry);
        }
        catch
        {
            // A disconnected diagnostic sink must not interrupt file logging or shutdown.
        }
    }

    private static ExtensionHostLogEntry Failure(string message, Exception exception)
    {
        return new(DateTimeOffset.Now, ExtensionHostLogLevel.Error, nameof(ExtensionLogSink), 0, $"{message} {ExceptionDiagnostics.Format(exception)}");
    }
}