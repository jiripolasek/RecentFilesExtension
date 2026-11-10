<#
.SYNOPSIS
Converts a release tag into the four-part MSIX version used by Store and WinGet.
#>

[CmdletBinding()]
param([Parameter(Mandatory)][string] $Tag)

if ($Tag -notmatch '^v(?<version>[0-9]+\.[0-9]+\.[0-9]+)(?:\.0)?$') {
    throw 'Select a vMAJOR.MINOR.PATCH or vMAJOR.MINOR.PATCH.0 tag.'
}
$version = [version] "$($Matches.version).0"
if ($version.Major -gt 65535 -or $version.Minor -gt 65535 -or $version.Build -gt 65535) {
    throw 'MSIX version components must be between 0 and 65535.'
}
return $version
