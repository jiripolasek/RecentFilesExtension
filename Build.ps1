#requires -Version 7.0

<#
.SYNOPSIS
Builds an unsigned extension MSIX and returns the package file. Use -BinLog to save restore and build logs.
#>
[CmdletBinding()]
param(
    [string] $Configuration,
    [ValidateSet('x64', 'ARM64')]
    [string] $Platform = ([Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq 'Arm64' ? 'ARM64' : 'x64'),
    [switch] $Aot,
    [switch] $BinLog
)

& "$PSScriptRoot/eng/Deploy-Package.ps1" -BuildOnly -Configuration $Configuration -Platform $Platform -Aot:$Aot -WriteBinLog $BinLog.IsPresent
