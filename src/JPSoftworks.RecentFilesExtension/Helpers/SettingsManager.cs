// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using JPSoftworks.RecentFilesExtension.Resources;

namespace JPSoftworks.RecentFilesExtension.Helpers;

internal sealed class SettingsManager : JsonSettingsManager, IRecentFilesSettings
{
    private const string DefaultNamespace = "jpsoftworks.recentfiles";

    [SuppressMessage("Maintainability", "CA1507:Use nameof to express symbol names", Justification = "Persisted settings keys must remain stable.")]
    private readonly ToggleSetting _includeOpenSaveMru = new(
        Namespaced("Sources.OpenSaveMru"),
        Resource("Settings_OpenSaveMru_Title"),
        Resource("Settings_OpenSaveMru_Subtitle"),
        true);

    [SuppressMessage("Maintainability", "CA1507:Use nameof to express symbol names", Justification = "Persisted settings keys must remain stable.")]
    private readonly ToggleSetting _includeOfficeRecentFiles = new(
        Namespaced("Sources.OfficeRecentFiles"),
        Resource("Settings_OfficeRecentFiles_Title"),
        Resource("Settings_OfficeRecentFiles_Subtitle"),
        true);

    private readonly ChoiceSetSetting _viewMode = new(
        Namespaced("Appearance.ViewMode"),
        Strings.Settings_ViewMode_Title,
        Strings.Settings_ViewMode_Subtitle,
        [
            new(Strings.Settings_ViewMode_List, "list"),
            new(Strings.Settings_ViewMode_Grid, "grid"),
            new(Strings.Settings_ViewMode_Gallery, "gallery"),
        ]) { IsRequired = true, Value = "list" };

    private readonly ToggleSetting _hideFileExtensions = new(
        Namespaced("Appearance.HideFileExtensions"),
        Strings.Settings_HideFileExtensions_Title,
        Strings.Settings_HideFileExtensions_Subtitle,
        false);

    private readonly ToggleSetting _showFileTypeInSubtitle = new(
        Namespaced("Appearance.ShowFileTypeInSubtitle"),
        Strings.Settings_ShowFileTypeInSubtitle_Title,
        Strings.Settings_ShowFileTypeInSubtitle_Subtitle,
        false);

    private readonly ToggleSetting _showFileTypeTags = new(
        Namespaced("Appearance.ShowFileTypeTags"),
        Strings.Settings_ShowFileTypeTags_Title,
        Strings.Settings_ShowFileTypeTags_Subtitle,
        false);

    private readonly ToggleSetting _showDetails = new(
        Namespaced("Appearance.ShowDetails"),
        Strings.Settings_ShowDetails_Title,
        Strings.Settings_ShowDetails_Subtitle,
        false);

    private (bool OpenSaveMru, bool OfficeRecentFiles) _lastSources;

    public SettingsManager(string? settingsPath = null)
    {
        this.FilePath = settingsPath ?? SettingsJsonPath();
        this.Settings.Add(new SettingsGroupHeader(
            Namespaced("Layout.Sources"),
            Resource("Settings_Group_Sources"),
            showSeparator: false));
        this.Settings.Add(this._includeOpenSaveMru);
        this.Settings.Add(this._includeOfficeRecentFiles);
        this.Settings.Add(new SettingsGroupHeader(
            Namespaced("Layout.Appearance"),
            Strings.Settings_Group_Appearance));
        this.Settings.Add(this._viewMode);
        this.Settings.Add(this._hideFileExtensions);
        this.Settings.Add(this._showFileTypeInSubtitle);
        this.Settings.Add(this._showFileTypeTags);
        this.Settings.Add(this._showDetails);

        this.LoadSettings();
        this._lastSources = (this.IncludeOpenSaveMru, this.IncludeOfficeRecentFiles);
        this.Settings.SettingsChanged += this.OnSettingsChanged;
    }

    public event EventHandler? SourcesChanged;

    public event EventHandler? AppearanceChanged;

    public bool IncludeOpenSaveMru => this._includeOpenSaveMru.Value;

    public bool IncludeOfficeRecentFiles => this._includeOfficeRecentFiles.Value;

    public string ViewMode
    {
        get => this._viewMode.Value ?? "list";
        set
        {
            this._viewMode.Value = value;
            this.OnSettingsChanged(this, this.Settings);
        }
    }

    public bool HideFileExtensions => this._hideFileExtensions.Value;

    public bool ShowFileTypeInSubtitle => this._showFileTypeInSubtitle.Value;

    public bool ShowFileTypeTags => this._showFileTypeTags.Value;

    public bool ShowDetails
    {
        get => this._showDetails.Value;
        set
        {
            this._showDetails.Value = value;
            this.OnSettingsChanged(this, this.Settings);
        }
    }

    private void OnSettingsChanged(object sender, Settings args)
    {
        this.SaveSettings();
        var sources = (this.IncludeOpenSaveMru, this.IncludeOfficeRecentFiles);
        if (sources != this._lastSources)
        {
            this._lastSources = sources;
            this.SourcesChanged?.Invoke(this, EventArgs.Empty);
        }

        this.AppearanceChanged?.Invoke(this, EventArgs.Empty);
    }

    private static string Namespaced(string propertyName)
    {
        return $"{DefaultNamespace}.{propertyName}";
    }

    private static string Resource(string name)
    {
        return Strings.ResourceManager.GetString(name, Strings.Culture) ?? name;
    }

    private static string SettingsJsonPath()
    {
        var directory = Utilities.BaseSettingsPath("Microsoft.CmdPal");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "settings.json");
    }
}
