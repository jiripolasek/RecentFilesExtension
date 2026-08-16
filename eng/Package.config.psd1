@{
    RepositoryRoot = '..'

    AppProjectPath      = 'src\JPSoftworks.RecentFilesExtension\JPSoftworks.RecentFilesExtension.csproj'
    WapProjectPath      = 'src\JPSoftworks.RecentFilesExtension.Package\JPSoftworks.RecentFilesExtension.Package.wapproj'
    PackageManifestPath = 'src\JPSoftworks.RecentFilesExtension.Package\Package.appxmanifest'
    ArtifactsPath       = 'artifacts'

    PackageExecutablePath = 'JPSoftworks.RecentFilesExtension\JPSoftworks.RecentFilesExtension.exe'

    DefaultConfiguration = 'Release'
    DefaultPlatform      = 'x64'

    RuntimeIdentifiers = @{
        x64   = 'win-x64'
        ARM64 = 'win-arm64'
    }

    Host = @{
        ProcessName = 'Microsoft.CmdPal.UI'
        LaunchUri   = 'x-cmdpal://'
        ReloadUri   = 'x-cmdpal://reload'
    }
}
