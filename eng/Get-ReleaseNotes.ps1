<#
.SYNOPSIS
Reads the matching release section from the repository changelog.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $Tag,
    [string] $ChangelogPath = (Join-Path $PSScriptRoot '../CHANGELOG.md')
)

$ErrorActionPreference = 'Stop'
$version = & (Join-Path $PSScriptRoot 'Get-ReleaseVersion.ps1') -Tag $Tag
$changelog = Get-Content -LiteralPath $ChangelogPath -Raw
$pattern = '(?ms)^## \[' + [regex]::Escape($version.ToString(3)) + '\][^\r\n]*\r?\n(?<notes>.*?)(?=^## |\z)'
$notes = [regex]::Match($changelog, $pattern).Groups['notes'].Value.Trim()
if ([string]::IsNullOrWhiteSpace($notes)) { throw "No release notes found for '$Tag' in '$ChangelogPath'." }
return $notes
