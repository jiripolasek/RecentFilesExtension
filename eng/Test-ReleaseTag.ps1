<#
.SYNOPSIS
Checks manifest-based release tagging with a local repository fixture and mocked GitHub CLI calls.
#>

[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Test-PublishingFixture.ps1')
$workflow = Get-Content -LiteralPath (Join-Path $PSScriptRoot '../.github/workflows/tag-release.yml') -Raw
$match = [regex]::Match($workflow, '(?ms)^      - name: Tag the package version and start Release\r?\n.*?^        run: \|\r?\n(?<script>.*)\z')
if (-not $match.Success) { throw 'Release tagging script was not found.' }
$tagRelease = [scriptblock]::Create(($match.Groups['script'].Value -replace '(?m)^          ', ''))
$testRoot = Join-Path ([IO.Path]::GetTempPath()) "Release Tag Tests-$([guid]::NewGuid().ToString('N'))"
$engRoot = Join-Path $testRoot 'eng'
New-Item -ItemType Directory -Path $engRoot | Out-Null
$configPath = New-TestWorkflowRepository -Directory $testRoot
Copy-Item -LiteralPath $configPath -Destination $engRoot
(Get-Content -LiteralPath (Join-Path $testRoot 'CmdPal.Extension.props') -Raw).Replace("::NormalizeDirectory('`$(MSBuildThisFileDirectory)')", "::NormalizeDirectory('`$(MSBuildThisFileDirectory)', '..')") |
    Set-Content -LiteralPath (Join-Path $engRoot 'CmdPal.Extension.props')
foreach ($name in @('Get-WorkflowConfig.ps1', 'Get-ReleaseVersion.ps1', 'Get-PackageConfig.ps1', 'Get-MSBuildEvaluation.ps1', 'Get-DotNetVersion.ps1')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $engRoot
}
$manifestPath = Join-Path $testRoot 'Package.appxmanifest'
$manifestTemplate = Get-Content -LiteralPath $manifestPath -Raw
$names = @('GITHUB_REPOSITORY', 'GITHUB_SHA', 'GITHUB_WORKSPACE', 'GITHUB_REF_TYPE', 'GITHUB_STEP_SUMMARY')
$originalEnvironment = @{}
foreach ($name in $names) { $originalEnvironment[$name] = [Environment]::GetEnvironmentVariable($name) }
$originalExitCode = Get-Variable LASTEXITCODE -ValueOnly -ErrorAction SilentlyContinue

function Reset-TestCase([string] $Version = '0.4.0.0', [string] $Tag = 'v0.4.0') {
    $env:GITHUB_REPOSITORY = 'example/Extension'
    $env:GITHUB_SHA = 'a' * 40
    $env:GITHUB_WORKSPACE = $testRoot
    $env:GITHUB_REF_TYPE = 'branch'
    $env:GITHUB_STEP_SUMMARY = Join-Path $testRoot 'summary.md'
    Set-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Value ''
    Set-Content -LiteralPath $manifestPath -Value $manifestTemplate.Replace('0.4.0.0', $Version)
    $script:state = @{
        Tag = $Tag; Refs = @(); Calls = [Collections.Generic.List[string]]::new()
        ListExitCode = 0; CreateExitCode = 0; DispatchExitCode = 0
    }
}

function gh {
    $arguments = $args -join '|'
    if ($arguments -ceq "api|repos/example/Extension/git/matching-refs/tags/$($state.Tag)") {
        $state.Calls.Add('list')
        $global:LASTEXITCODE = $state.ListExitCode
        return ConvertTo-Json -InputObject $state.Refs -Depth 5 -Compress
    }
    if ($arguments -ceq "api|--method|POST|repos/example/Extension/git/refs|-f|ref=refs/tags/$($state.Tag)|-f|sha=$('a' * 40)") {
        $state.Calls.Add('create')
        $global:LASTEXITCODE = $state.CreateExitCode
        return
    }
    if ($arguments -ceq "workflow|run|release.yml|--repo|example/Extension|--ref|$($state.Tag)") {
        $state.Calls.Add('dispatch')
        $global:LASTEXITCODE = $state.DispatchExitCode
        return
    }
    throw "Unexpected GitHub CLI call: $arguments"
}

function Assert-Rejected([string] $ExpectedMessage) {
    $failure = $null
    try { & $tagRelease | Out-Null }
    catch { $failure = $_.Exception.Message }
    if ($null -eq $failure -or $failure -notlike "*$ExpectedMessage*") {
        throw "Expected '$ExpectedMessage', received '$failure'."
    }
}

Push-Location $testRoot
try {
    foreach ($case in @(@('0.4.0.0', 'v0.4.0'), @('2.3.4.0', 'v2.3.4'))) {
        Reset-TestCase -Version $case[0] -Tag $case[1]
        & $tagRelease
        if ($state.Calls -join ',' -ne 'list,create,dispatch') { throw 'A new version must create its tag before starting Release.' }
    }

    foreach ($type in @('commit', 'tag')) {
        Reset-TestCase
        $state.Refs = @(@{ ref = 'refs/tags/v0.4.0'; object = @{ type = $type; sha = 'b' * 40 } })
        & $tagRelease
        if ($state.Calls -join ',' -ne 'list') { throw 'An existing tag must be left untouched without starting Release.' }
    }

    Reset-TestCase
    $state.Refs = @(@{ ref = 'refs/tags/v0.4.01' })
    & $tagRelease
    if ($state.Calls -join ',' -ne 'list,create,dispatch') { throw 'A tag with the same prefix must not block a new tag.' }

    foreach ($version in @('0.4.0', '0.4.0.1', '0.4.0-beta', '', 'invalid')) {
        Reset-TestCase -Version $version
        Assert-Rejected 'MAJOR.MINOR.PATCH.0'
        if ($state.Calls.Count -ne 0) { throw 'An invalid package version must not reach GitHub.' }
    }

    Reset-TestCase -Version '65536.0.0.0'
    Assert-Rejected 'between 0 and 65535'
    if ($state.Calls.Count -ne 0) { throw 'An out-of-range version must not reach GitHub.' }

    Reset-TestCase
    $env:GITHUB_REF_TYPE = 'tag'
    Assert-Rejected 'branch to release'
    if ($state.Calls.Count -ne 0) { throw 'A tag ref must not create another tag.' }

    Reset-TestCase
    $state.ListExitCode = 1
    Assert-Rejected 'Unable to check'
    if ($state.Calls -join ',' -ne 'list') { throw 'Tag lookup failure must stop before creating a tag.' }

    Reset-TestCase
    $state.CreateExitCode = 1
    Assert-Rejected 'Unable to create'
    if ($state.Calls -join ',' -ne 'list,create') { throw 'A rejected or concurrent tag creation must not start Release.' }

    Reset-TestCase
    $state.DispatchExitCode = 1
    Assert-Rejected "Run Release manually on 'v0.4.0'"
    if ($state.Calls -join ',' -ne 'list,create,dispatch') { throw 'A dispatch failure must preserve the created tag.' }

    Write-Output 'Passed release tagging checks for manifest versions, release dispatch, existing tags, prefix matches, branch selection, and API failures.'
}
finally {
    Pop-Location
    foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name, $originalEnvironment[$name]) }
    $global:LASTEXITCODE = if ($null -eq $originalExitCode) { 0 } else { $originalExitCode }
    Get-ChildItem -LiteralPath $engRoot -File | Remove-Item -Force
    Remove-Item -LiteralPath $engRoot
    Get-ChildItem -LiteralPath $testRoot -File | Remove-Item -Force
    Remove-Item -LiteralPath $testRoot
}
