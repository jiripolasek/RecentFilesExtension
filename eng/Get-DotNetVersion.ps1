<#
.SYNOPSIS
Reads and validates the bootstrap SDK version without requiring MSBuild.
#>

[CmdletBinding()]
param([string] $ConfigPath = (Join-Path $PSScriptRoot 'Package.config.psd1'))

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$config = Import-PowerShellDataFile -LiteralPath $ConfigPath
if (-not $config.ContainsKey('Workflow') -or $config.Workflow -isnot [Collections.IDictionary] -or
    -not $config.Workflow.Contains('DotNetVersion') -or [string]::IsNullOrWhiteSpace($config.Workflow.DotNetVersion)) {
    throw "Workflow configuration must define a nonempty 'DotNetVersion'."
}
return [string] $config.Workflow.DotNetVersion
