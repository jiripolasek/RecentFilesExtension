<#
.SYNOPSIS
Combines script options with evaluated properties from the sibling CmdPal.Extension.props.
#>

[CmdletBinding()]
param(
    [string] $ConfigPath = (Join-Path $PSScriptRoot 'Package.config.psd1'),
    [string] $Configuration,
    [string] $Platform
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ConfigPath = [IO.Path]::GetFullPath($ConfigPath)
$config = Import-PowerShellDataFile -LiteralPath $ConfigPath
foreach ($key in @('DefaultConfiguration', 'DefaultPlatform')) {
    if (-not $config.ContainsKey($key) -or [string]::IsNullOrWhiteSpace($config[$key])) {
        throw "Package configuration is missing '$key'."
    }
}
if ([string]::IsNullOrWhiteSpace($Configuration)) { $Configuration = $config.DefaultConfiguration }
if ([string]::IsNullOrWhiteSpace($Platform)) { $Platform = $config.DefaultPlatform }
$propsPath = Join-Path (Split-Path -Parent $ConfigPath) 'CmdPal.Extension.props'
$propertyNames = @{
    RepositoryRoot = 'CmdPalRepositoryRoot'
    AppProjectPath = 'CmdPalExtensionProject'
    WapProjectPath = 'CmdPalPackageProject'
    PackageManifestPath = 'CmdPalPackageManifest'
    ArtifactsPath = 'CmdPalArtifactsPath'
    PackageExecutablePath = 'CmdPalPackageExecutable'
    RuntimeIdentifiers = 'CmdPalRuntimeIdentifiers'
}
foreach ($key in $propertyNames.Keys) {
    if ($config.ContainsKey($key)) { throw "Move '$key' from '$ConfigPath' to '$propsPath'." }
}
if (-not (Test-Path -LiteralPath $propsPath -PathType Leaf)) { throw "Shared properties were not found: '$propsPath'." }
$properties = (& (Join-Path $PSScriptRoot 'Get-MSBuildEvaluation.ps1') -Path $propsPath -Property $propertyNames.Values -Configuration $Configuration -Platform $Platform).Properties
foreach ($key in $propertyNames.Keys) {
    $value = $properties[$propertyNames[$key]]
    if ([string]::IsNullOrWhiteSpace($value)) { throw "Shared property '$($propertyNames[$key])' is missing or empty." }
    $config[$key] = $value
}
if (-not [IO.Path]::IsPathFullyQualified($config.RepositoryRoot)) { throw 'CmdPalRepositoryRoot must evaluate to an absolute path.' }
$runtimes = @{}
foreach ($runtime in $config.RuntimeIdentifiers.Split(';', [StringSplitOptions]::RemoveEmptyEntries -bor [StringSplitOptions]::TrimEntries)) {
    $runtimePlatform = switch -CaseSensitive ($runtime) {
        'win-x64' { 'x64' }
        'win-arm64' { 'ARM64' }
        default { throw "Unsupported platform/runtime identifier: '$runtime'." }
    }
    $runtimes[$runtimePlatform] = $runtime
}
if ($runtimes.Count -eq 0) { throw 'At least one runtime identifier is required.' }
$config.RuntimeIdentifiers = $runtimes
return $config
