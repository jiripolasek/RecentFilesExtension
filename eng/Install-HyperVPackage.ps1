<#
.SYNOPSIS
Installs a staged development package inside a Hyper-V guest.

.DESCRIPTION
Internal entry point for Deploy-HyperVPackage.ps1. Run with the guest's
Windows PowerShell as the account that will use the installed package.
#>

[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$report = [ordered]@{
    Succeeded = $false
    ComputerName = $env:COMPUTERNAME
    UserName = [Security.Principal.WindowsIdentity]::GetCurrent().Name
    OSVersion = [Environment]::OSVersion.Version.ToString()
    PreviousVersion = $null
    PackageFullName = $null
    Error = $null
}
$transcribing = $false
try {
    Start-Transcript -LiteralPath (Join-Path $PSScriptRoot 'install.log') -Force | Out-Null
    $transcribing = $true
    $deployment = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'deployment.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    if ([IO.Path]::GetFileName($deployment.PackageFileName) -cne $deployment.PackageFileName) {
        throw 'The package filename must not contain a directory.'
    }
    $packagePath = Join-Path $PSScriptRoot $deployment.PackageFileName
    if ((Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash -ne $deployment.PackageHash) {
        throw 'The copied package does not match the signed host package.'
    }
    $guestArchitecture = switch ($env:PROCESSOR_ARCHITECTURE) {
        'AMD64' { 'x64' }
        'ARM64' { 'arm64' }
        default { throw "Unsupported guest architecture '$env:PROCESSOR_ARCHITECTURE'." }
    }
    if ($guestArchitecture -ne $deployment.Architecture) {
        throw "Package architecture '$($deployment.Architecture)' does not match guest '$guestArchitecture'."
    }
    $certificatePath = Join-Path $PSScriptRoot 'DevCertificate.cer'
    $certificate = [Security.Cryptography.X509Certificates.X509Certificate2]::new([IO.File]::ReadAllBytes($certificatePath))
    try {
        if ($certificate.Thumbprint -ne $deployment.CertificateThumbprint -or
            $certificate.Subject -cne $deployment.Publisher -or $certificate.HasPrivateKey) {
            throw 'The public certificate does not match the expected package publisher and thumbprint.'
        }
    }
    finally { $certificate.Dispose() }

    $previous = Get-AppxPackage -Name $deployment.Name | Select-Object -First 1
    if ($previous) {
        $report.PreviousVersion = [string] $previous.Version
        if ($previous.Publisher -cne $deployment.Publisher) {
            throw 'The installed package has a different publisher.'
        }
        if ([version] $previous.Version -gt [version] $deployment.Version) {
            throw 'Refusing to downgrade the installed package. Use an older test checkpoint or increase the package version.'
        }
    }
    if ($deployment.TrustCertificate -and
        -not (Test-Path -LiteralPath "Cert:\LocalMachine\TrustedPeople\$($deployment.CertificateThumbprint)")) {
        Import-Certificate -FilePath $certificatePath -CertStoreLocation 'Cert:\LocalMachine\TrustedPeople' | Out-Null
    }
    $installParameters = @{
        Path = $packagePath
        ForceTargetApplicationShutdown = $true
        # Allow repeated development builds of the same version; downgrades are rejected above.
        ForceUpdateFromAnyVersion = $true
        ErrorAction = 'Stop'
    }
    $dependencyDirectory = Join-Path $PSScriptRoot 'Dependencies'
    if (Test-Path -LiteralPath $dependencyDirectory -PathType Container) {
        $dependencies = @(Get-ChildItem -LiteralPath $dependencyDirectory -File |
            Where-Object Extension -In @('.appx', '.msix') | Select-Object -ExpandProperty FullName)
        if ($dependencies.Count -gt 0) { $installParameters.DependencyPath = $dependencies }
    }
    Add-AppxPackage @installParameters
    $installed = Get-AppxPackage -Name $deployment.Name | Select-Object -First 1
    if (-not $installed -or [version] $installed.Version -ne [version] $deployment.Version -or
        $installed.Publisher -cne $deployment.Publisher -or [string] $installed.Architecture -ine $deployment.Architecture) {
        throw 'The installed package identity does not match the deployment.'
    }
    $report.PackageFullName = [string] $installed.PackageFullName
    $report.Succeeded = $true
}
catch {
    $report.Error = ($_ | Out-String).Trim()
}
finally {
    $report | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'result.json') -Encoding UTF8
    if ($transcribing) { Stop-Transcript | Out-Null }
}
if (-not $report.Succeeded) { throw $report.Error }
