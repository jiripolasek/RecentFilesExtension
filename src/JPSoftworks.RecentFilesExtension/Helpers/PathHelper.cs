// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

namespace JPSoftworks.RecentFilesExtension.Helpers;

internal static class PathHelper
{
    internal static bool IsNetworkPath(this string path)
    {
        return path.StartsWith(@"\\", StringComparison.OrdinalIgnoreCase);
    }
}