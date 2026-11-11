// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.Security;
using Microsoft.Win32;

namespace JPSoftworks.RecentFilesExtension.Sources;

internal static class RegistryReadHelper
{
    public static RegistryKey? OpenReadableKey(RegistryKey parent, string name)
    {
        try
        {
            return parent.OpenSubKey(name);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            return null;
        }
    }

    public static string[] GetReadableSubKeyNames(RegistryKey key) => TryGetSubKeyNames(key) ?? [];

    public static string[]? TryGetSubKeyNames(RegistryKey key)
    {
        try
        {
            return key.GetSubKeyNames();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            return null;
        }
    }
}