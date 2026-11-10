// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using Windows.System;

namespace JPSoftworks.RecentFilesExtension.Helpers;

public static class KeyChords
{
    public static KeyChord CopyPath { get; } = new(VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, (int)VirtualKey.C, 0);

    public static KeyChord OpenInExplorer { get; } = new(VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, (int)VirtualKey.E, 0);

    public static KeyChord ToggleViewMode { get; } = new(VirtualKeyModifiers.Menu, (int)VirtualKey.V, 0);

    public static KeyChord ToggleDetails { get; } = new(VirtualKeyModifiers.Menu, (int)VirtualKey.P, 0);
}