// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using JPSoftworks.RecentFilesExtension.Helpers;
using JPSoftworks.RecentFilesExtension.Resources;
using Windows.System;

namespace JPSoftworks.RecentFilesExtension.Commands;

internal sealed partial class OpenGitHubIssuesCommand : InvokableCommand
{
    private static readonly Uri GitHubIssuesUri = new("https://github.com/jiripolasek/RecentFilesExtension/issues/new");
    private readonly Func<Task<bool>> _launch;

    public OpenGitHubIssuesCommand() : this(() => Launcher.LaunchUriAsync(GitHubIssuesUri).AsTask())
    {
    }

    internal OpenGitHubIssuesCommand(Func<Task<bool>> launch)
    {
        this._launch = launch;
        this.Name = Strings.ReportProblem_OpenGitHub_Title;
        this.Icon = Icons.OpenInNewWindow;
    }

    public override ICommandResult Invoke()
    {
        try
        {
            if (Task.Run(this._launch).WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult())
            {
                return CommandResult.KeepOpen();
            }

            Logger.LogWarning("GitHub Issues launch was rejected.");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Unable to open GitHub Issues {ExceptionDiagnostics.Format(ex)}");
        }

        return CommandResult.ShowToast(new ToastArgs
        {
            Message = Strings.ReportProblem_OpenGitHub_Failed,
            Result = CommandResult.KeepOpen(),
        });
    }
}