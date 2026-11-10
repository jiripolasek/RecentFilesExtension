<#
.SYNOPSIS
Creates local MSIX fixtures for publishing checks, without building or signing.
#>

function New-TestMsix {
    param(
        [string] $Path,
        [string] $Architecture,
        [string] $IdentityName,
        [string] $Publisher,
        [string] $Version = '0.4.0.0',
        [switch] $Signed
    )
    $archive = [IO.Compression.ZipFile]::Open($Path, [IO.Compression.ZipArchiveMode]::Create)
    try {
        $entry = $archive.CreateEntry('AppxManifest.xml')
        $writer = [IO.StreamWriter]::new($entry.Open())
        try {
            $identityNameXml = [Security.SecurityElement]::Escape($IdentityName)
            $publisherXml = [Security.SecurityElement]::Escape($Publisher)
            $writer.Write("<Package><Identity Name='$identityNameXml' Publisher='$publisherXml' Version='$Version' ProcessorArchitecture='$Architecture' /></Package>")
        }
        finally { $writer.Dispose() }
        if ($Signed) {
            $signature = $archive.CreateEntry('AppxSignature.p7x')
            $stream = $signature.Open()
            $stream.Dispose()
        }
    }
    finally { $archive.Dispose() }
}

function New-TestWorkflowRepository {
    param([string] $Directory, [string[]] $Architectures = @('x64', 'ARM64'))
    foreach ($file in @('Example Extension.csproj', 'Example.Package.wapproj', 'Example.Tests.csproj')) {
        Set-Content -LiteralPath (Join-Path $Directory $file) -Value '<Project />'
    }
    Set-Content -LiteralPath (Join-Path $Directory 'Example.slnx') -Value '<Solution><Project Path="Example Extension.csproj" /><Project Path="Example.Tests.csproj" /></Solution>'
    Set-Content -LiteralPath (Join-Path $Directory 'Package.appxmanifest') -Value "<Package><Identity Name='Contoso.ExampleExtension' Publisher='CN=ExamplePublisher' Version='0.4.0.0' /></Package>"
    $runtimeEntries = ($Architectures | ForEach-Object { "win-$($_.ToLowerInvariant())" }) -join ';'
    @'
<Project>
  <PropertyGroup>
    <CmdPalRepositoryRoot>$([MSBuild]::NormalizeDirectory('$(MSBuildThisFileDirectory)'))</CmdPalRepositoryRoot>
    <CmdPalExtensionProject>$(CmdPalRepositoryRoot)Example Extension.csproj</CmdPalExtensionProject>
    <CmdPalPackageProject>$(CmdPalRepositoryRoot)Example.Package.wapproj</CmdPalPackageProject>
    <CmdPalPackageManifest>$(CmdPalRepositoryRoot)Package.appxmanifest</CmdPalPackageManifest>
    <CmdPalPackageExecutable>Example/Example.exe</CmdPalPackageExecutable>
    <CmdPalArtifactsPath>$([MSBuild]::NormalizeDirectory('$(CmdPalRepositoryRoot)', 'output packages'))</CmdPalArtifactsPath>
    <CmdPalRuntimeIdentifiers>__RUNTIMES__</CmdPalRuntimeIdentifiers>
  </PropertyGroup>
</Project>
'@.Replace('__RUNTIMES__', $runtimeEntries) | Set-Content -LiteralPath (Join-Path $Directory 'CmdPal.Extension.props')
    $configPath = Join-Path $Directory 'Package.config.psd1'
    @"
@{
    DefaultConfiguration = 'Release'
    DefaultPlatform = 'x64'
    Workflow = @{
        DotNetVersion = '9.0.x'
        WindowsSdkVersion = '10.0.26100.0'
        WingetPackageIdentifier = 'Contoso.ExampleExtension'
    }
}
"@ | Set-Content -LiteralPath $configPath
    return $configPath
}
