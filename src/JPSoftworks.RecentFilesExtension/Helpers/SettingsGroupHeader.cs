// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.Text.Json.Nodes;

namespace JPSoftworks.RecentFilesExtension.Helpers;

internal sealed class SettingsGroupHeader : Setting<string>
{
    private readonly bool _showSeparator;

    public SettingsGroupHeader(string key, string title, bool showSeparator = true)
        : base(key, title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        this._showSeparator = showSeparator;
    }

    public override Dictionary<string, object> ToDictionary()
    {
        return new()
        {
            { "type", "TextBlock" },
            { "text", this.Value ?? string.Empty },
            { "weight", "Bolder" },
            { "size", "Medium" },
            { "wrap", true },
            { "separator", this._showSeparator },
            { "spacing", this._showSeparator ? "Large" : "None" },
        };
    }

    public override void Update(JsonObject payload)
    {
    }

    public override string ToState()
    {
        return $"\"{this.Key}\": null";
    }
}