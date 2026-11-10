#requires -Version 7.0

<#
.SYNOPSIS
Runs the solution's managed tests for x64 or ARM64. Use -BinLog to save MSBuild logs and TRX results.
#>
[CmdletBinding()]
param(
    [string] $Configuration,
    [ValidateSet('x64', 'ARM64')]
    [string] $Platform = ([Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq 'Arm64' ? 'ARM64' : 'x64'),
    [switch] $BinLog
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
if ($Platform -eq 'ARM64' -and [Runtime.InteropServices.RuntimeInformation]::OSArchitecture -ne 'Arm64') {
    throw 'ARM64 tests require Windows on ARM64. To cross-build the package, use .\Build.ps1 -Platform ARM64.'
}
$testProjects = @(& "$PSScriptRoot/eng/Get-WorkflowConfig.ps1" -TestProjectsOnly)
if ($testProjects.Count -eq 0) { throw 'The solution contains no test projects.' }
$config = & "$PSScriptRoot/eng/Get-PackageConfig.ps1" -Configuration $Configuration -Platform $Platform
$runtime = $config.RuntimeIdentifiers[$Platform]
if (!$runtime) { throw "Platform '$Platform' is not configured for tests." }
if (!$Configuration) { $Configuration = $config.DefaultConfiguration }

$logArguments = @()
if ($BinLog) {
    $artifactsPath = [IO.Path]::GetFullPath($config.ArtifactsPath, $config.RepositoryRoot)
    $logDirectory = [IO.Directory]::CreateDirectory((Join-Path $artifactsPath 'logs')).FullName
    Write-Host "Writing MSBuild logs and test results to $logDirectory"
}

foreach ($project in $testProjects) {
    $projectPath = Join-Path $PSScriptRoot $project
    if ($BinLog) {
        $projectName = [IO.Path]::GetFileNameWithoutExtension($project)
        $logArguments = @(
            "-bl:$logDirectory/$projectName-{}.binlog"
            '--logger', "trx;LogFilePrefix=$projectName"
            '--results-directory', $logDirectory
        )
    }

    dotnet test $projectPath --configuration $Configuration --runtime $runtime "-p:Platform=$Platform" -p:SkipKillRunningExecutable=true @logArguments
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
