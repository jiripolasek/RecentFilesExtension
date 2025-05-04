// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using JPSoftworks.RecentFilesExtension.Pages;
using JPSoftworks.RecentFilesExtension.Resources;

namespace JPSoftworks.RecentFilesExtension;

public partial class RecentFilesExtensionCommandsProvider : CommandProvider
{
    private readonly ICommandItem[] _commands;

    public RecentFilesExtensionCommandsProvider()
    {
        this.Id = "JPSoftworks.CmdPal.RecentFiles";
        this.DisplayName = Strings.Page_RecentFiles!;
        this.Icon = IconHelpers.FromRelativePath("Assets\\StoreLogo.png");
        this._commands =
        [
            new CommandItem(new RecentFilesExtensionPage()) { Subtitle = Strings.Page_RecentFiles_Subtitle! }
        ];
    }

    public override ICommandItem[] TopLevelCommands()
    {
        return this._commands;
    }
}