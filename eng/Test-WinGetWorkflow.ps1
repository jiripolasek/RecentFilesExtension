<#
.SYNOPSIS
Checks WinGet release validation with local signed-package fixtures and a mocked GitHub API.
#>

[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Test-PublishingFixture.ps1')
$validateScript = Join-Path $PSScriptRoot 'Get-WinGetRelease.ps1'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) "WinGet Tests-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $testRoot | Out-Null
$configPath = New-TestWorkflowRepository -Directory $testRoot
$config = & (Join-Path $PSScriptRoot 'Get-WorkflowConfig.ps1') -ConfigPath $configPath
$names = @('WINGET_TOKEN', 'GITHUB_REPOSITORY')
$originalEnvironment = @{}
foreach ($name in $names) { $originalEnvironment[$name] = [Environment]::GetEnvironmentVariable($name) }
$originalExitCode = Get-Variable LASTEXITCODE -ValueOnly -ErrorAction SilentlyContinue

function Reset-TestCase {
    $env:WINGET_TOKEN = 'test-token'
    $env:GITHUB_REPOSITORY = 'example/AnotherExtension'
    $script:testTag = 'v0.4.0'
    $script:state = @{ ExitCode = 0; DownloadFailure = $false }
    Get-ChildItem -LiteralPath $testRoot -File -Filter '*.msix' | Remove-Item -Force
    foreach ($platform in @('x64', 'arm64')) {
        New-TestMsix -Path (Join-Path $testRoot "$platform.msix") -Architecture $platform -IdentityName $config.IdentityName -Publisher $config.Publisher -Signed
    }
    $script:release = [pscustomobject]@{
        tag_name = 'v0.4.0'
        html_url = 'https://github.com/example/AnotherExtension/releases/tag/v0.4.0'
        draft = $false
        prerelease = $false
        assets = @(
            @{ name = 'first-installer.Msix'; browser_download_url = 'https://downloads.invalid/x64.msix' },
            @{ name = 'second-installer.msix'; browser_download_url = 'https://downloads.invalid/arm64.msix' }
        )
    }
}

function gh {
    $global:LASTEXITCODE = $state.ExitCode
    $release | ConvertTo-Json -Depth 5
}

function Invoke-WebRequest {
    param($Uri, $OutFile, $TimeoutSec)
    if ($state.DownloadFailure) { throw 'Download failed.' }
    Copy-Item -LiteralPath (Join-Path $testRoot ([uri] $Uri).Segments[-1]) -Destination $OutFile
}

function Assert-Rejected([string] $ExpectedMessage) {
    $failure = $null
    try { & $validateScript -Tag $testTag -ConfigPath $configPath | Out-Null }
    catch { $failure = $_.Exception.Message }
    if ($null -eq $failure -or $failure -notlike "*$ExpectedMessage*") {
        throw "Expected '$ExpectedMessage', received '$failure'."
    }
}

try {
    foreach ($tag in @('v0.4.0', 'v0.4.0.0')) {
        Reset-TestCase
        $script:testTag = $tag
        $release.tag_name = $tag
        $result = & $validateScript -Tag $tag -ConfigPath $configPath
        if ($result.Tag -ne $tag -or $result.Version -ne '0.4.0.0' -or $result.Url -ne $release.html_url -or $result.Identifier -ne 'Contoso.ExampleExtension') {
            throw 'Incorrect WinGet release metadata.'
        }
    }
    foreach ($tag in @('master', 'v0.4.0.1', 'v0.4.0-beta')) {
        Reset-TestCase
        $script:testTag = $tag
        Assert-Rejected 'Select a vMAJOR'
    }
    Reset-TestCase
    $script:testTag = 'v65536.0.0'
    Assert-Rejected 'between 0 and 65535'
    foreach ($property in @('draft', 'prerelease')) {
        Reset-TestCase
        $release.$property = $true
        Assert-Rejected 'published stable release'
    }
    Reset-TestCase
    $release.assets = @($release.assets[0])
    Assert-Rejected 'per configured architecture'
    Reset-TestCase
    $release.assets[1].browser_download_url = 'https://downloads.invalid/x64.msix'
    Assert-Rejected 'exactly once'
    Reset-TestCase
    $release.assets[1].name = 'unsigned.msixupload'
    Assert-Rejected 'per configured architecture'
    Reset-TestCase
    $env:WINGET_TOKEN = ''
    $withoutPublishingToken = & $validateScript -Tag $testTag -ConfigPath $configPath
    if ($withoutPublishingToken.Version -ne '0.4.0.0') { throw 'Read-only release validation must not need the publishing token.' }
    Reset-TestCase
    $state.ExitCode = 1
    Assert-Rejected 'Failed to read'

    foreach ($case in @(
        @('0.3.0.0', $config.IdentityName, $config.Publisher, $true, 'identity'),
        @('0.4.0.0', 'Wrong.Extension', $config.Publisher, $true, 'identity'),
        @('0.4.0.0', $config.IdentityName, 'CN=WrongPublisher', $true, 'publisher'),
        @('0.4.0.0', $config.IdentityName, $config.Publisher, $false, 'signature')
    )) {
        Reset-TestCase
        $path = Join-Path $testRoot 'arm64.msix'
        Remove-Item -LiteralPath $path
        New-TestMsix -Path $path -Architecture arm64 -Version $case[0] -IdentityName $case[1] -Publisher $case[2] -Signed:$case[3]
        Assert-Rejected $case[4]
    }
    Reset-TestCase
    $state.DownloadFailure = $true
    Assert-Rejected 'Download failed'

    Reset-TestCase
    $configPath = New-TestWorkflowRepository -Directory $testRoot -Architectures @('x64')
    $release.assets = @($release.assets[0])
    $result = & $validateScript -Tag $testTag -ConfigPath $configPath
    if ($result.Identifier -ne 'Contoso.ExampleExtension') { throw 'Single-architecture extension failed.' }
    Write-Output 'Passed 19 WinGet scenarios with different extension settings and real ZIP payload checks.'
}
finally {
    foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name, $originalEnvironment[$name]) }
    $global:LASTEXITCODE = if ($null -eq $originalExitCode) { 0 } else { $originalExitCode }
    Get-ChildItem -LiteralPath $testRoot -File | Remove-Item -Force
    Remove-Item -LiteralPath $testRoot
}
