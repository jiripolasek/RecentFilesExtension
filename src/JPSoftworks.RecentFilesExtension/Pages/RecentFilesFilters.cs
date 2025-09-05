// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using Windows.Win32;
using JPSoftworks.RecentFilesExtension.Helpers;

namespace JPSoftworks.RecentFilesExtension.Pages;

internal class RecentFilesFilters : Filters
{
    private const string EverythingFilterId = "Everything";

    public RecentFilesFilters()
    {
        this.CurrentFilterId = EverythingFilterId;
    }

    public override IFilterItem[] GetFilters()
    {
        return
        [
            new FileFilter
            {
                Name = "All files", Id = EverythingFilterId, Extensions = [], Icon = Icons.Filters.AllFiles
            },

            new Separator(),

            new FileFilter
            {
                Name = "Documents",
                Id = "documents",
                Extensions =
                [
                    ".doc", ".docx", ".dotx", ".pdf", ".txt", ".rtf", ".odt", ".ott",
                    ".xlsx", ".xls", ".xlsm", ".ods", ".csv", ".tsv",
                    ".ppt", ".pptx", ".odp", ".key"
                ],
                Icon = Icons.Filters.Documents
            },
            new FileFilter
            {
                Name = "Images",
                Id = "images",
                Extensions =
                [
                    ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".tiff", ".tif", ".svg",
                    ".webp", ".ico", ".heif", ".heic", ".raw", ".dng"
                ],
                Icon = Icons.Filters.Images
            },
            new FileFilter
            {
                Name = "Videos",
                Id = "videos",
                Extensions =
                [
                    ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm",
                    ".mpeg", ".mpg", ".3gp", ".mts", ".m2ts"
                ],
                Icon = Icons.Filters.Videos
            },
            new FileFilter
            {
                Name = "Audio",
                Id = "audio",
                Extensions =
                [
                    ".mp3", ".wav", ".flac", ".aac", ".ogg", ".oga",
                    ".wma", ".m4a", ".opus", ".alac", ".mid", ".midi"
                ],
                Icon = Icons.Filters.Music
            },
            new FileFilter
            {
                Name = "Archives",
                Id = "archives",
                Extensions =
                [
                    ".zip", ".rar", ".7z", ".tar", ".gz", ".bz2", ".xz", ".lz",
                    ".lzma", ".zst", ".cab", ".iso", ".img", ".dmg"
                ],
                Icon = Icons.Filters.Archives
            },
            new FileFilter
            {
                Name = "Code",
                Id = "code",
                Extensions =
                [
                    ".cs", ".js", ".ts", ".html", ".htm", ".css", ".scss", ".less",
                    ".java", ".py", ".cpp", ".cxx", ".cc", ".c", ".h", ".hpp",
                    ".rs", ".go", ".php", ".rb", ".pl", ".swift", ".kt", ".m", ".mm",
                    ".json", ".xml", ".yml", ".yaml", ".toml", ".ini", ".cfg",
                    ".sql", ".sh", ".bat", ".ps1", ".psm1", ".psd1",
                    ".md", ".rst", ".aspx", ".vue", ".svelte"
                ],
                Icon = Icons.Filters.Code
            },
            new FileFilter
            {
                Name = "Scripts & Installers",
                Id = "executables",
                Extensions =
                [
                    ".exe", ".msi", ".msix", ".appx", ".bat", ".cmd", ".sh",
                    ".app", ".apk", ".ps1", ".psm1", ".psd1", ".vbs", ".js", ".wsf"
                ],
                Icon = Icons.Filters.Scripts
            },
            new FileFilter
            {
                Name = "Projects",
                Id = "projects",
                Extensions =
                [
                    ".sln", ".slnx", ".csproj", ".vcxproj", ".fsproj", ".xcodeproj",
                    ".pyproj", ".vbproj", ".makefile", ".cmake", "CMakeLists.txt",
                    ".gradle", ".pom", ".props", ".targets", ".proj"
                ],
                Icon = Icons.Filters.Projects
            },
            new FileFilter
            {
                Name = "Fonts",
                Id = "fonts",
                Extensions =
                [
                    ".ttf", ".otf", ".woff", ".woff2", ".eot"
                ],
                Icon = Icons.Filters.Fonts
            },
            new FileFilter
            {
                Name = "Databases",
                Id = "databases",
                Extensions =
                [
                    ".db", ".sqlite", ".sqlite3", ".accdb", ".mdb", ".sql", ".ndf", ".mdf"
                ],
                Icon = Icons.Filters.Database
            },
            new Separator(),

            new LocationFilter("folder_documents", Environment.SpecialFolder.MyDocuments)
            {
                Icon = Icons.Filters.Documents, Name = "Documents folder"
            },

            new LocationFilter("folder_pictures", Environment.SpecialFolder.MyPictures)
            {
                Icon = Icons.Filters.Images, Name = "Pictures folder"
            },

            new LocationFilter("folder_localappdata",
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData))
            {
                Icon = Icons.FileExplorerSegoe, Name = "Local AppData folder"
            },

            // downloads
            new LocationFilter("folder_downloads", PInvoke.FOLDERID_Downloads) // downloads is inside user profile
            {
                Icon = Icons.Filters.DownloadsFolder, Name = "Downloads folder"
            },

            // desktop
            new LocationFilter("folder_desktop", Environment.SpecialFolder.Desktop)
            {
                Icon = Icons.Filters.DesktopFolder, Name = "Desktop folder"
            },

            // user profile
            new LocationFilter("folder_home", Environment.SpecialFolder.UserProfile)
            {
                Icon = Icons.Filters.UserProfileFolder, Name = "User Profile folder"
            }
        ];
    }
}