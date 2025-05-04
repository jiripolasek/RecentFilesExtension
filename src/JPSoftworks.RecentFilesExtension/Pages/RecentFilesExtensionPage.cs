// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Windows.Storage.Streams;
using JPSoftworks.RecentFilesExtension.Commands;
using JPSoftworks.RecentFilesExtension.Helpers;
using JPSoftworks.RecentFilesExtension.Model;
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
            var recentFiles = Environment.GetFolderPath(Environment.SpecialFolder.Recent);
            if (!string.IsNullOrWhiteSpace(recentFiles))
            {
                var recentFilesShortcuts = new DirectoryInfo(recentFiles)
                    .GetFiles("*.lnk", SearchOption.TopDirectoryOnly)
                    .OrderByDescending(static t => t.LastWriteTimeUtc)
                    .Take(32);

                // create a list of ListItem objects from the files
                var items = new List<ListItem>();

                foreach (var recentFileInfo in recentFilesShortcuts)
                {
                    var path = new string(recentFileInfo.FullName);

                    try
                    {
                        var shellLink = new ShellLinkHelper();
                        shellLink.RetrieveTargetPath(path);
                        var rf = new RecentFile(shellLink.TargetPath);

                        var listItem = new ListItem(new OpenFileCommand(rf))
                        {
                            Title = shellLink.DisplayName,
                            Subtitle = shellLink.TargetPath,
                            Icon = GetIcon(shellLink, recentFileInfo),
                            MoreCommands =
                            [
                                new CommandContextItem(
                                    new ShowFileInFolderCommand(rf.FullPath) { Name = Strings.Command_ShowInFolder! }),
                                new CommandContextItem(new OpenWithCommand(rf)),
                                new CommandContextItem(new CopyPathCommand(rf))
                            ]
                        };


                        items.Add(listItem);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError($"Failed to add link {recentFileInfo.FullName}", ex);
                    }
                }

                return items.ToArray();
            }

            return
            [
                new ListItem(new NoOpCommand()) { Title = "TODO: Implement your extension here" }
            ];
        }
        catch (Exception ex)
        {
            Logger.LogError(ex);
            throw;
        }
    }

    private static IconInfo? GetIcon(ShellLinkHelper shellLink, FileInfo recentFileInfo)
    {
        IconInfo? icon = null;
        try
        {
            var stream = ThumbnailHelper.GetThumbnail(shellLink.TargetPath).Result;
            if (stream != null)
            {
                var data = new IconData(RandomAccessStreamReference.CreateFromStream(stream)!);
                icon = new IconInfo(data, data);
                return icon;
            }
        }
        catch (Exception ex)
        {
            Logger.LogError("Failed to get the icon.", ex);
        }

        if (icon == null)
        {
            try
            {
                var stream = ThumbnailHelper.GetThumbnail(recentFileInfo.FullName).Result;
                if (stream != null)
                {
                    var data = new IconData(RandomAccessStreamReference.CreateFromStream(stream)!);
                    icon = new IconInfo(data, data);
                    return icon;
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("Failed to get the icon.", ex);
            }
        }

        return icon;
    }
}