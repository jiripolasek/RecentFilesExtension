// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using JPSoftworks.RecentFilesExtension.Helpers;
using JPSoftworks.RecentFilesExtension.Resources;

namespace JPSoftworks.RecentFilesExtension.Commands;

internal sealed partial class SaveDiagnosticLogsCommand : InvokableCommand
{
    private readonly DiagnosticLogArchiveService _archiveService;

    public SaveDiagnosticLogsCommand(DiagnosticLogArchiveService archiveService)
    {
        ArgumentNullException.ThrowIfNull(archiveService);
        this._archiveService = archiveService;
        this.Name = Strings.ReportProblem_SaveLogs_Title;
        this.Icon = Icons.Save;
    }

    public override ICommandResult Invoke()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var cancellationToken = timeout.Token;
        try
        {
            var result = Task.Run(() => this._archiveService.CreateArchiveOnDesktopAsync(cancellationToken))
                .WaitAsync(cancellationToken).GetAwaiter().GetResult();
            return Toast(Strings.ReportProblem_SaveLogs_Succeeded.Replace(
                "{0}", Path.GetFileName(result.ArchivePath), StringComparison.Ordinal),
                new ShowFileInFolderCommand(result.ArchivePath) { Name = Strings.Command_ShowInFolder });
        }
        catch (Exception ex)
        {
            Logger.LogError($"Unable to save diagnostic logs {ExceptionDiagnostics.Format(ex)}");
            return Toast(Strings.ReportProblem_SaveLogs_Failed);
        }
    }

    private static CommandResult Toast(string message, ICommand? command = null) => CommandResult.ShowToast(new ToastArgs
    {
        Message = message,
        Command = command,
        Result = CommandResult.KeepOpen(),
    });
}