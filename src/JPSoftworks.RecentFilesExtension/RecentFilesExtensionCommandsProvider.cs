// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using JPSoftworks.RecentFilesExtension.Helpers;
using JPSoftworks.RecentFilesExtension.Pages;
using JPSoftworks.RecentFilesExtension.Resources;

namespace JPSoftworks.RecentFilesExtension;

public sealed partial class RecentFilesExtensionCommandsProvider : CommandProvider
{
    private readonly RecentFilesExtensionPage _page;
    private readonly ICommandItem[] _commands;
    private bool _isDisposed;

    public RecentFilesExtensionCommandsProvider() : this(new SettingsManager(), ShellIconSupport.Unknown)
    {
    }

    internal RecentFilesExtensionCommandsProvider(SettingsManager settings, ShellIconSupport support)
    {
        this.Id = "JPSoftworks.CmdPal.RecentFiles";
        this.DisplayName = Strings.Page_RecentFiles!;
        this.Icon = IconHelpers.FromRelativePath("Assets\\Square20x20Logo.scale-100.png");
        this.Settings = settings.GetHostSettings(support);
        this._page = new(settings, shellIconSupport: support);
        this._commands =
        [
            new CommandItem(this._page)
            {
                Subtitle = Strings.Page_RecentFiles_Subtitle!,
                MoreCommands =
                [
                    new CommandContextItem(this.Settings.SettingsPage!),
                    new CommandContextItem(new ReportProblemPage(new DiagnosticLogArchiveService(
                        Program.LogDirectoryPath, Program.FallbackLogDirectoryPath))),
                ],
            },
        ];
    }

    public override ICommandItem[] TopLevelCommands()
    {
        return this._commands;
    }

    public override void Dispose()
    {
        if (this._isDisposed)
        {
            return;
        }

        this._isDisposed = true;
        this._page.Dispose();
        base.Dispose();
    }
}