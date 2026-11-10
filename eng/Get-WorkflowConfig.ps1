<#
.SYNOPSIS
Validates packaging settings and discovers the workflow matrix and solution tests.
#>

[CmdletBinding()]
param(
    [string] $ConfigPath = (Join-Path $PSScriptRoot 'Package.config.psd1'),
    [switch] $TestProjectsOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ConfigPath = [IO.Path]::GetFullPath($ConfigPath)
$dotNetVersion = & (Join-Path $PSScriptRoot 'Get-DotNetVersion.ps1') -ConfigPath $ConfigPath
$config = & (Join-Path $PSScriptRoot 'Get-PackageConfig.ps1') -ConfigPath $ConfigPath
$repositoryRoot = $config.RepositoryRoot

function Get-RepositoryPath([string] $Path, [switch] $MustExist) {
    if ([string]::IsNullOrWhiteSpace($Path)) { throw 'Configured paths must not be empty.' }
    $absolutePath = [IO.Path]::GetFullPath($Path.Replace('\', '/'), $repositoryRoot)
    $relativePath = [IO.Path]::GetRelativePath($repositoryRoot, $absolutePath).Replace('\', '/').TrimEnd('/')
    if ([IO.Path]::IsPathRooted($relativePath) -or $relativePath -eq '..' -or $relativePath.StartsWith('../') -or $relativePath -eq '.') {
        throw "Configured path must stay inside the repository: '$Path'."
    }
    if ($MustExist -and -not (Test-Path -LiteralPath $absolutePath -PathType Leaf)) {
        throw "Configured file was not found: '$Path'."
    }
    return $relativePath
}

foreach ($key in @('WindowsSdkVersion', 'WingetPackageIdentifier')) {
    if (-not $config.Workflow.ContainsKey($key)) { throw "Workflow configuration is missing '$key'." }
    if ([string]::IsNullOrWhiteSpace($config.Workflow[$key])) { throw "Workflow setting '$key' must not be empty." }
}
$solutionFiles = @(Get-ChildItem -LiteralPath $repositoryRoot -Filter '*.slnx' -File)
if ($solutionFiles.Count -ne 1) { throw 'Expected exactly one .slnx solution in the repository root.' }
[xml] $solution = Get-Content -LiteralPath $solutionFiles[0].FullName -Raw
$testProjects = @(
    $solution.SelectNodes('//Project') |
        Where-Object { $_.Path -like '*Tests.csproj' } |
        ForEach-Object { Get-RepositoryPath $_.Path -MustExist } |
        Sort-Object -Unique
)
if ($TestProjectsOnly) { return $testProjects }

function ConvertTo-WorkflowConfig($PackageConfig) {
    $manifestPath = Get-RepositoryPath $PackageConfig.PackageManifestPath -MustExist
    [xml] $manifest = Get-Content -LiteralPath (Join-Path $repositoryRoot $manifestPath) -Raw
    if ([string]::IsNullOrWhiteSpace($manifest.Package.Identity.Name) -or [string]::IsNullOrWhiteSpace($manifest.Package.Identity.Publisher)) {
        throw 'The package manifest must define an identity and publisher.'
    }
    @{
        AppProjectPath = Get-RepositoryPath $PackageConfig.AppProjectPath -MustExist
        WapProjectPath = Get-RepositoryPath $PackageConfig.WapProjectPath -MustExist
        PackageManifestPath = $manifestPath
        ArtifactsPath = Get-RepositoryPath $PackageConfig.ArtifactsPath
        DefaultConfiguration = $PackageConfig.DefaultConfiguration
        DotNetVersion = $dotNetVersion
        WindowsSdkVersion = $PackageConfig.Workflow.WindowsSdkVersion
        WingetPackageIdentifier = $PackageConfig.Workflow.WingetPackageIdentifier
        TestProjects = $testProjects
        IdentityName = [string] $manifest.Package.Identity.Name
        Publisher = [string] $manifest.Package.Identity.Publisher
    }
}

$defaultConfig = ConvertTo-WorkflowConfig $config
$matrix = @(
    # Probe both supported platforms because their runtime lists can differ.
    foreach ($platform in @('ARM64', 'x64')) {
        $platformConfig = $config
        if ($platform -ine $config.DefaultPlatform) {
            $platformConfig = & (Join-Path $PSScriptRoot 'Get-PackageConfig.ps1') -ConfigPath $ConfigPath -Platform $platform
        }
        if ($platformConfig.RuntimeIdentifiers.ContainsKey($platform)) {
            $settings = if ($platform -ieq $config.DefaultPlatform) { $defaultConfig } else { ConvertTo-WorkflowConfig $platformConfig }
            @{ platform = $platform; runtime = $platformConfig.RuntimeIdentifiers[$platform]; config = $settings }
        }
    }
)
if ($matrix.Count -eq 0) { throw 'No supported platform enables its own runtime identifier.' }
if ($testProjects.Count -gt 0 -and 'x64' -notin $matrix.platform) {
    throw 'The Windows CI test host requires the x64 runtime when test projects are configured.'
}
$workflowConfig = $defaultConfig.Clone()
$workflowConfig.Matrix = @{ include = $matrix }
$workflowConfig.Architectures = @($matrix.platform | ForEach-Object { $_.ToLowerInvariant() })
[pscustomobject] $workflowConfig
