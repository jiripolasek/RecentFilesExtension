@{
    DefaultConfiguration = 'Release'
    DefaultPlatform      = 'x64'

    Workflow = @{
        DotNetVersion = '11.0.x'
        WindowsSdkVersion = '10.0.26100.0'
        WingetPackageIdentifier = 'JiriPolasek.RecentFilesforCommandPalette'
    }

    Host = @{
        ProcessName = 'Microsoft.CmdPal.UI'
        LaunchUri   = 'x-cmdpal://'
        ReloadUri   = 'x-cmdpal://reload'
    }
}
