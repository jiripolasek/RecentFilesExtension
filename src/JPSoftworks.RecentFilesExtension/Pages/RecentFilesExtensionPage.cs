// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System;
using System.Collections.Generic;
using JPSoftworks.RecentFilesExtension.Helpers;
using JPSoftworks.RecentFilesExtension.Resources;

namespace JPSoftworks.RecentFilesExtension.Pages;

internal sealed partial class RecentFilesExtensionPage : ListPage
{
    public RecentFilesExtensionPage()
    {
        this.Icon = IconHelpers.FromRelativePath("Assets\\StoreLogo.svg");
        this.Title = Strings.Page_RecentFiles!;
        this.Name = Strings.Page_RecentFiles!;
        this.Id = "com.jpsoftworks.cmdpal.recentfiles";
    }

    public override IListItem[] GetItems()
    {
        try
        {
            var recentFiles = RecentFilesProvider.GetRecentFiles();
            var items = new List<ListItem>();

            foreach (var recentFile in recentFiles)
            {
                try
                {
                    items.Add(new RecentFileListItem(recentFile));
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Failed to add link {recentFile.FullPath}", ex);
                }
            }

            return [.. items];

        }
        catch (Exception ex)
        {
            Logger.LogError(ex);
            throw;
        }
    }
}