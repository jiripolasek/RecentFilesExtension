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

    public SettingsManager()
    {
        this.FilePath = SettingsJsonPath();
        this.Settings.Add(new SettingsGroupHeader(
            Namespaced("Layout.Sources"),
            Resource("Settings_Group_Sources"),
            showSeparator: false));
        this.Settings.Add(this._includeOpenSaveMru);
        this.Settings.Add(this._includeOfficeRecentFiles);

        this.LoadSettings();
        this.Settings.SettingsChanged += this.OnSettingsChanged;
    }

    public event EventHandler? SourcesChanged;

    public bool IncludeOpenSaveMru => this._includeOpenSaveMru.Value;

    public bool IncludeOfficeRecentFiles => this._includeOfficeRecentFiles.Value;

    private void OnSettingsChanged(object sender, Settings args)
    {
        this.SaveSettings();
        this.SourcesChanged?.Invoke(this, EventArgs.Empty);
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