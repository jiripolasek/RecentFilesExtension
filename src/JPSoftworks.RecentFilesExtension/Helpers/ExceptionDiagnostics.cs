// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace JPSoftworks.RecentFilesExtension.Helpers;

internal static class ExceptionDiagnostics
{
    public static string Summarize(Exception exception) => string.Create(CultureInfo.InvariantCulture,
        $"({exception.GetType().Name}, HRESULT 0x{exception.HResult:X8})");

    public static string Format(Exception exception)
    {
        var result = new StringBuilder();
        Append(exception);
        return result.ToString();

        void Append(Exception error)
        {
            result.AppendLine(Summarize(error));
            result.Append(new StackTrace(error, fNeedFileInfo: false));
            if (error is AggregateException aggregate)
            {
                foreach (var inner in aggregate.InnerExceptions)
                {
                    Append(inner);
                }
            }
            else if (error.InnerException is { } inner)
            {
                Append(inner);
            }
        }
    }
}