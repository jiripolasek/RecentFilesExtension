// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using JPSoftworks.RecentFilesExtension.Commands;
using JPSoftworks.RecentFilesExtension.Helpers;
using JPSoftworks.RecentFilesExtension.Resources;

namespace JPSoftworks.RecentFilesExtension.Pages;

internal sealed partial class ReportProblemPage : ListPage
{
    private readonly IListItem[] _items;

    public ReportProblemPage(DiagnosticLogArchiveService archiveService)
    {
        ArgumentNullException.ThrowIfNull(archiveService);

        this.Name = Strings.ReportProblem_Title!;
        this.Title = Strings.ReportProblem_Title!;
        this.Icon = Icons.ReportProblem;
        this.Id = "com.jpsoftworks.cmdpal.recentfiles.reportproblem";
        this.ShowDetails = true;

        var instructions = new Details
        {
            Title = Strings.ReportProblem_Title!,
            Body = Strings.ReportProblem_Instructions!,
        };

        this._items =
        [
            new ListItem(new SaveDiagnosticLogsCommand(archiveService))
            {
                Title = Strings.ReportProblem_SaveLogs_Title!,
                Subtitle = Strings.ReportProblem_SaveLogs_Subtitle!,
                Details = instructions,
            },
            new ListItem(new OpenGitHubIssuesCommand())
            {
                Title = Strings.ReportProblem_OpenGitHub_Title!,
                Subtitle = Strings.ReportProblem_OpenGitHub_Subtitle!,
                Details = instructions,
            },
        ];
    }

    public override IListItem[] GetItems() => this._items;
}