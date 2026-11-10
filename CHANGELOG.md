# Changelog

## [0.9.0] - Unreleased

### Added

- File-type filters for documents, images, videos, audio, archives, code, scripts and installers, projects, fonts, and databases, plus a folders-only filter.
- Location filters for Documents, Pictures, Downloads, Desktop, Local AppData, and the user profile.
- Recent-file discovery from Windows open/save dialog history and Microsoft Office recent-file lists, recent shortcuts, and Jump Lists, in addition to Windows Recent Items.
- Settings to enable or disable Windows open/save history and Office recent-file sources.
- Support for cloud document URLs and Office launch links, with appropriate application icons.
- List, grid, and gallery views, including larger thumbnail previews for the gallery.
- Appearance settings to hide file extensions, show file types in subtitles or tags, and display a details pane with the item's name, path, type, and last-used time.
- Global appearance commands after a separator in every item's context menu: Alt+V cycles List → Grid → Gallery, and Alt+P toggles the details pane. Both apply immediately, persist across restarts, and keep Command Palette open.

### Changed

- Merge recent-file sources into one list, remove duplicate targets, and order entries by their most recent usage.
- Reduce initial shortcut-loading work and load thumbnails in the background so results can appear before their icons are ready.
- Limit concurrent thumbnail extraction and cancel obsolete thumbnail work when the search changes.
- Align existing action shortcuts with Command Palette: Ctrl+Shift+C copies the path, and Ctrl+Shift+E shows the item in File Explorer.

### Fixed

- Avoid synchronous directory probes on unavailable network shares when loading results, filtering folders, or changing appearance settings.
- Keep search, paging, and refresh state consistent during initialization, rapid searches, source updates, and shutdown; ignore results from canceled or superseded work.
- Refresh cached entries when shortcuts are renamed or replaced, even when their targets remain unchanged, and retry transient shortcut-reading failures on later refreshes.
- Match location filters at directory boundaries instead of including unrelated folders with the same path prefix.
- Recognize exact project filenames such as `CMakeLists.txt`, `Makefile`, and `pom.xml`, with case-insensitive matching.
- Release shell-link COM objects, display-name buffers, and known-folder paths correctly, and dispose source watchers and event subscriptions during shutdown.
- Display unknown last-used timestamps as unknown rather than showing an invalid date.

### Development

- Add Native AOT publishing for x64 and ARM64. CI and release packages enable AOT and trimming; local builds default to managed output with `-Aot` available for native builds.
- Move MSIX packaging and deployment into a separate Windows Application Packaging project with reusable version 8 templates and shared MSBuild configuration.
- Restore Visual Studio's redirected publish assets with the selected publishing mode before project evaluation, and save restore diagnostics as binary logs.
- Add root `Build.ps1` and `Test.ps1` wrappers, optional MSBuild/TRX logs, and isolated package builds that preserve the running local installation.
- Add CI, manifest-based release tagging, unsigned Store upload artifacts, draft release notes, and WinGet submission workflows. Store upload and publication remain manual.
- Add Hyper-V test deployment with per-architecture builds, development signing, optional certificate trust and checkpoint restoration, dry runs, and deployment reports.
- Expand automated coverage for recent-file parsing, discovery, paths, settings, appearance, packaging, and release/deployment tooling; update Command Palette and Windows interop dependencies.
- Add development, release, and VM-testing documentation and repository skills.
