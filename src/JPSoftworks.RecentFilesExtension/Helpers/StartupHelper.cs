// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

using System.Diagnostics;
using JPSoftworks.RecentFilesExtension.Resources;
using Windows.ApplicationModel;
using Windows.Management.Deployment;

namespace JPSoftworks.RecentFilesExtension.Helpers;

/// <summary>
/// Helper class for user experience related tasks.
/// </summary>
internal static class StartupHelper
{
    private const string CommandPalettePackageFamilyName = "Microsoft.CommandPalette_8wekyb3d8bbwe";
    private const string CommandPaletteDevPackageFamilyName = "Microsoft.CommandPalette.Dev_8wekyb3d8bbwe";
    private const string StorePowerToysLink = "ms-windows-store://pdp/?productid=XP89DCGQ3K6VLD";

    /// <summary>
    /// Handles direct launch of the application by checking if PowerToys Command Palette is installed
    /// and attempts to launch it. Shows appropriate messages to the user based on the outcome.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    public static async Task HandleDirectLaunch()
    {
        try
        {
            await HandleDirectLaunchCore();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex);
            MessageBoxHelper.Show(
                Strings.UserExperienceHelper_GeneralErrorOnStart!,
                Strings.UserExperienceHelper_ErrorCaption!,
                MessageBoxHelper.IconType.Error,
                MessageBoxHelper.MessageBoxType.OK);
        }
    }

    private static async Task HandleDirectLaunchCore()
    {
        // Let's add something meaningful for the end-user experience.
        // 1. We are not running as a COM server, so we can show a message box.
        // 2. We can check if PowerToys Command Palette is installed.

        Logger.Initialize();

        var (retailCommandPalettePackage, devCommandPalettePackage) = FindCommandPaletteApps();

        if (retailCommandPalettePackage != null || devCommandPalettePackage != null)
        {
            var started = false;
            try
            {
                if (retailCommandPalettePackage != null)
                {
                    started = await StartCommandPalette(retailCommandPalettePackage);
                }
                else if (devCommandPalettePackage != null)
                {
                    started = await StartCommandPalette(devCommandPalettePackage);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex);
            }

            if (!started)
            {
                MessageBoxHelper.Show(
                    Strings.UserExperienceHelper_CmdPalIsInstalledButFailedToStart!,
                    Strings.UserExperienceHelper_InfoCaption!,
                    MessageBoxHelper.IconType.Warning,
                    MessageBoxHelper.MessageBoxType.OK);
            }
        }
        else
        {
            MessageBoxHelper.Show(
                Strings.UserExperienceHelper_CmdPalIsNotInstalled!,
                Strings.UserExperienceHelper_InfoCaption!,
                MessageBoxHelper.IconType.Warning,
                MessageBoxHelper.MessageBoxType.OK);
            try
            {
                Process.Start(new ProcessStartInfo(StorePowerToysLink) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                // ignore exception, we just want to open the store link
                Logger.LogError(ex);
            }
        }
    }

    private static async Task<bool> StartCommandPalette(Package package)
    {
        var appEntries = await package.GetAppListEntriesAsync()! ?? [];
        if (appEntries.Count > 0 && appEntries[0] != null)
        {
            return await appEntries[0]!.LaunchAsync()!;
        }

        return false;
    }

    private static PackageDetectionResult FindCommandPaletteApps()
    {
        try
        {
            var packageManager = new PackageManager();
            var retailPackage = packageManager.FindPackagesForUser("", CommandPalettePackageFamilyName)
                ?.FirstOrDefault();
            var devPackage = packageManager.FindPackagesForUser("", CommandPaletteDevPackageFamilyName)
                ?.FirstOrDefault();

            return new PackageDetectionResult(retailPackage, devPackage);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex);
            return new PackageDetectionResult(null, null);
        }
    }

    private record struct PackageDetectionResult(Package? RetailPackage, Package? DevPackage);
}