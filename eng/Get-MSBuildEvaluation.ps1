<#
.SYNOPSIS
Evaluates MSBuild properties and items without restoring or executing targets.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $Path,
    [ValidateNotNullOrEmpty()]
    [string[]] $Property = @(),
    [ValidateNotNullOrEmpty()]
    [string[]] $Item = @(),
    [string] $Configuration,
    [string] $Platform
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
if ($Property.Count -eq 0 -and $Item.Count -eq 0) { throw 'Request at least one MSBuild property or item.' }
$Path = [IO.Path]::GetFullPath($Path)
$arguments = @('msbuild', $Path, '-nologo')
if ($Property.Count -gt 0) { $arguments += "-getProperty:$($Property -join ',')" }
if ($Item.Count -gt 0) { $arguments += "-getItem:$($Item -join ',')" }
if (-not [string]::IsNullOrWhiteSpace($Configuration)) { $arguments += "-p:Configuration=$Configuration" }
if (-not [string]::IsNullOrWhiteSpace($Platform)) { $arguments += "-p:Platform=$Platform" }

$output = @(& dotnet @arguments 2>&1)
if ($LASTEXITCODE -ne 0) { throw "MSBuild evaluation failed for '$Path' (exit code $LASTEXITCODE): $($output -join [Environment]::NewLine)" }
foreach ($diagnostic in $output | Where-Object { $_ -isnot [string] }) {
    Write-Warning ([string] $diagnostic)
}
$stdout = ($output | Where-Object { $_ -is [string] }) -join [Environment]::NewLine
# MSBuild returns plain text for a single property query without items.
if ($Property.Count -eq 1 -and $Item.Count -eq 0) {
    return @{ Properties = @{ ($Property[0]) = $stdout } }
}
return $stdout | ConvertFrom-Json -AsHashtable
