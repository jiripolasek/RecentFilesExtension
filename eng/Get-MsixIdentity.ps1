<#
.SYNOPSIS
Reads package identity and signature presence from MSIX or single-package MSIXUPLOAD files.
#>

[CmdletBinding()]
param([Parameter(Mandatory)][string] $Path)

$archive = [IO.Compression.ZipFile]::OpenRead([IO.Path]::GetFullPath($Path))
$package = $archive
$packageStream = $null
try {
    if ([IO.Path]::GetExtension($Path) -ieq '.msixupload') {
        $entries = @($archive.Entries | Where-Object FullName -Match '\.msix$')
        if ($entries.Count -ne 1) { throw "Expected one MSIX inside '$Path'." }
        $packageStream = $entries[0].Open()
        $package = [IO.Compression.ZipArchive]::new($packageStream, [IO.Compression.ZipArchiveMode]::Read)
    }
    $manifestEntry = $package.GetEntry('AppxManifest.xml')
    if ($null -eq $manifestEntry) { throw "MSIX manifest was not found in '$Path'." }
    $reader = [IO.StreamReader]::new($manifestEntry.Open())
    try { [xml] $manifest = $reader.ReadToEnd() }
    finally { $reader.Dispose() }
    [pscustomobject]@{
        Name = [string] $manifest.Package.Identity.Name
        Publisher = [string] $manifest.Package.Identity.Publisher
        Version = [version] $manifest.Package.Identity.Version
        Architecture = ([string] $manifest.Package.Identity.ProcessorArchitecture).ToLowerInvariant()
        HasSignature = $null -ne $package.GetEntry('AppxSignature.p7x')
    }
}
finally {
    if ($package -ne $archive) { $package.Dispose() }
    if ($null -ne $packageStream) { $packageStream.Dispose() }
    $archive.Dispose()
}
