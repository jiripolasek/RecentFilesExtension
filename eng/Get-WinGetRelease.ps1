<#
.SYNOPSIS
Validates a published release and its signed MSIX payloads before a WinGet update.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $Tag,
    [string] $ConfigPath = (Join-Path $PSScriptRoot 'Package.config.psd1')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$version = & (Join-Path $PSScriptRoot 'Get-ReleaseVersion.ps1') -Tag $Tag
$config = & (Join-Path $PSScriptRoot 'Get-WorkflowConfig.ps1') -ConfigPath $ConfigPath
$releaseJson = gh api "repos/$env:GITHUB_REPOSITORY/releases/tags/$Tag"
if ($LASTEXITCODE -ne 0) { throw 'Failed to read the selected GitHub release.' }
$release = $releaseJson | ConvertFrom-Json
if ($release.draft -or $release.prerelease) {
    throw 'WinGet requires a published stable release with public installers.'
}
$installers = @($release.assets | Where-Object name -Match '\.msix$')
if ($installers.Count -ne $config.Architectures.Count) {
    throw 'Attach one Store-signed MSIX installer per configured architecture.'
}
$downloadDirectory = Join-Path ([IO.Path]::GetTempPath()) "WinGetRelease-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $downloadDirectory | Out-Null
$architectures = @()
try {
    foreach ($installer in $installers) {
        $path = Join-Path $downloadDirectory "$($architectures.Count).msix"
        Invoke-WebRequest -Uri $installer.browser_download_url -OutFile $path -TimeoutSec 600 | Out-Null
        $identity = & (Join-Path $PSScriptRoot 'Get-MsixIdentity.ps1') -Path $path
        if ($identity.Name -ne $config.IdentityName -or $identity.Publisher -ne $config.Publisher -or $identity.Version -ne $version) {
            throw 'Release installer identity, publisher, or version does not match this extension and tag.'
        }
        if (-not $identity.HasSignature) { throw 'Release MSIX installers must contain a signature.' }
        if ($identity.Architecture -notin $config.Architectures -or $identity.Architecture -in $architectures) {
            throw 'Release installers must cover each configured architecture exactly once.'
        }
        $architectures += $identity.Architecture
    }
}
finally {
    Get-ChildItem -LiteralPath $downloadDirectory -File | Remove-Item -Force
    Remove-Item -LiteralPath $downloadDirectory
}
[pscustomobject]@{
    Tag = [string] $release.tag_name
    Version = $version.ToString()
    Url = [string] $release.html_url
    Identifier = $config.WingetPackageIdentifier
}
