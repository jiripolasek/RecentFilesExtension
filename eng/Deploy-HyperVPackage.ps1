#requires -Version 7.0
<#
.SYNOPSIS
Builds, signs, and deploys a development MSIX to configured Hyper-V VMs.

.DESCRIPTION
Reuses Deploy-Package.ps1 -BuildOnly and Get-PackageConfig.ps1. Native AOT and
trimming are enabled by default. Use Release for release validation.
Signs once per architecture using a certificate in the host's Personal store. Copies only
the public certificate and installs for the supplied guest user through
PowerShell Direct. VMs are started when necessary. Checkpoints are restored
only with -RestoreCheckpoint. Reports are retained under artifacts/hyperv.
MSIX_HYPERV_* environment variables supply defaults for omitted settings.
The local HyperV.config.psd1 is optional when VM names and a certificate
thumbprint are supplied through parameters or environment defaults.

.EXAMPLE
.\eng\Deploy-HyperVPackage.ps1 -WhatIf

.EXAMPLE
.\eng\Deploy-HyperVPackage.ps1 -VMName Windows-Test -TrustCertificate

.EXAMPLE
.\eng\Deploy-HyperVPackage.ps1 -Credential (Get-Credential) -RestoreCheckpoint -Aot
#>

[CmdletBinding(SupportsShouldProcess)]
param(
    [string] $ConfigPath,
    [string[]] $VMName,
    [pscredential] $Credential,
    [string] $Configuration,
    [switch] $Aot = $true,
    [string] $VisualStudioPath,
    [switch] $TrustCertificate,
    [switch] $RestoreCheckpoint,
    [ValidateRange(1, 3600)]
    [int] $BootTimeoutSeconds = 180
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$configPathSpecified = -not [string]::IsNullOrWhiteSpace($ConfigPath) -or
    -not [string]::IsNullOrWhiteSpace($env:MSIX_HYPERV_CONFIG_PATH)
if ([string]::IsNullOrWhiteSpace($ConfigPath)) { $ConfigPath = $env:MSIX_HYPERV_CONFIG_PATH }
if ([string]::IsNullOrWhiteSpace($ConfigPath)) { $ConfigPath = Join-Path $PSScriptRoot 'HyperV.config.psd1' }
$ConfigPath = [IO.Path]::GetFullPath($ConfigPath)
$config = @{}
if (Test-Path -LiteralPath $ConfigPath -PathType Leaf) {
    $config = Import-PowerShellDataFile -LiteralPath $ConfigPath
}
elseif ($configPathSpecified) {
    throw "Hyper-V configuration was not found: '$ConfigPath'. Check -ConfigPath or MSIX_HYPERV_CONFIG_PATH."
}
foreach ($defaults in @(
    @{
        CertificateThumbprint = $env:MSIX_HYPERV_CERTIFICATE_THUMBPRINT
        CertificateStoreLocation = $env:MSIX_HYPERV_CERTIFICATE_STORE_LOCATION
        GuestPackageDirectory = $env:MSIX_HYPERV_GUEST_PACKAGE_DIRECTORY
    },
    @{
        PackageConfigPath = Join-Path $PSScriptRoot 'Package.config.psd1'
        CertificateStoreLocation = 'CurrentUser'
        GuestPackageDirectory = 'C:\ProgramData\MSIXDeployments'
    }
)) {
    foreach ($key in $defaults.Keys) {
        if (-not $config.ContainsKey($key) -or [string]::IsNullOrWhiteSpace([string] $config[$key])) {
            $config[$key] = $defaults[$key]
        }
    }
}
foreach ($key in @('PackageConfigPath', 'CertificateThumbprint', 'CertificateStoreLocation', 'GuestPackageDirectory')) {
    if (-not $config.ContainsKey($key) -or [string]::IsNullOrWhiteSpace([string] $config[$key])) {
        throw "Hyper-V configuration is missing '$key'."
    }
}
if ($config.CertificateStoreLocation -notin @('CurrentUser', 'LocalMachine')) {
    throw 'CertificateStoreLocation must be CurrentUser or LocalMachine.'
}
$thumbprint = ([string] $config.CertificateThumbprint) -replace '\s', ''
if ($thumbprint -notmatch '^[0-9a-fA-F]{40}$') { throw 'CertificateThumbprint must contain 40 hexadecimal characters.' }
if ($config.GuestPackageDirectory -notmatch '^[a-zA-Z]:\\') { throw 'GuestPackageDirectory must be an absolute guest drive path.' }
$packageConfigPath = [IO.Path]::GetFullPath($config.PackageConfigPath, (Split-Path -Parent $ConfigPath))
if ([string]::IsNullOrWhiteSpace($Configuration)) { $Configuration = $env:MSIX_HYPERV_CONFIGURATION }
$packageDefaults = Import-PowerShellDataFile -LiteralPath $packageConfigPath
if ([string]::IsNullOrWhiteSpace($Configuration)) { $Configuration = [string] $packageDefaults.DefaultConfiguration }
if (-not $PSBoundParameters.ContainsKey('VMName') -and -not [string]::IsNullOrWhiteSpace($env:MSIX_HYPERV_VM_NAMES)) {
    $VMName = @($env:MSIX_HYPERV_VM_NAMES -split ';' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    if ($VMName.Count -eq 0) { throw 'MSIX_HYPERV_VM_NAMES must contain at least one VM name.' }
}
$machines = @()
if ($config.ContainsKey('VirtualMachines') -and $null -ne $config.VirtualMachines) {
    $machines = @($config.VirtualMachines)
}
if ($machines.Count -eq 0) {
    if (-not $VMName) {
        throw 'No virtual machines specified. Pass -VMName, set MSIX_HYPERV_VM_NAMES, or define VirtualMachines in HyperV.config.psd1.'
    }
    $machines = @($VMName | ForEach-Object { @{ Name = $_ } })
}
$names = @()
foreach ($machine in $machines) {
    if ($machine -isnot [Collections.IDictionary] -or -not $machine.Contains('Name') -or
        [string]::IsNullOrWhiteSpace([string] $machine.Name) -or $machine.Name -in $names) {
        throw 'VirtualMachines must contain unique, nonempty VM names.'
    }
    $names += [string] $machine.Name
    if (-not $machine.Contains('Platform') -or [string]::IsNullOrWhiteSpace([string] $machine.Platform)) {
        $machine.Platform = $env:MSIX_HYPERV_PLATFORM
        if ([string]::IsNullOrWhiteSpace([string] $machine.Platform)) { $machine.Platform = [string] $packageDefaults.DefaultPlatform }
    }
    if (-not $machine.Contains('UserName') -or [string]::IsNullOrWhiteSpace([string] $machine.UserName)) {
        $machine.UserName = $env:MSIX_HYPERV_GUEST_USERNAME
    }
}
foreach ($requestedName in $VMName) {
    if ($requestedName -notin $names) { throw "VM '$requestedName' is not configured." }
}
if ($VMName) { $machines = @($machines | Where-Object { $_.Name -in $VMName }) }
$packageConfigs = @{}
foreach ($platform in @($machines.Platform | Sort-Object -Unique)) {
    $packageConfig = & (Join-Path $PSScriptRoot 'Get-PackageConfig.ps1') -ConfigPath $packageConfigPath -Configuration $Configuration -Platform $platform
    if (-not $packageConfig.RuntimeIdentifiers.ContainsKey($platform)) { throw "Unsupported platform '$platform'." }
    $packageConfigs[$platform] = $packageConfig
}
if ($RestoreCheckpoint) {
    foreach ($machine in $machines) {
        if (-not $machine.Contains('CheckpointName') -or [string]::IsNullOrWhiteSpace([string] $machine.CheckpointName)) {
            throw "VM '$($machine.Name)' has no CheckpointName configured."
        }
    }
}
$action = 'Build, sign, start VMs as needed, and install the package for the supplied guest users'
if ($TrustCertificate) { $action += '; trust the public development certificate in guest TrustedPeople stores' }
if ($RestoreCheckpoint) { $action += '; DISCARD current VM changes by restoring configured checkpoints' }
if (-not $PSCmdlet.ShouldProcess(($machines.Name -join ', '), $action)) { return }

Import-Module Hyper-V -ErrorAction Stop
$certificate = Get-Item -LiteralPath "Cert:\$($config.CertificateStoreLocation)\My\$thumbprint"
if (-not $certificate.HasPrivateKey -or $certificate.NotBefore -gt (Get-Date) -or $certificate.NotAfter -le (Get-Date)) {
    throw 'The signing certificate must be valid and have an accessible private key.'
}
$manifests = @{}
foreach ($platform in $packageConfigs.Keys) {
    $packageConfig = $packageConfigs[$platform]
    [xml] $manifest = Get-Content -LiteralPath ([IO.Path]::GetFullPath($packageConfig.PackageManifestPath, $packageConfig.RepositoryRoot)) -Raw
    if ($certificate.Subject -cne [string] $manifest.Package.Identity.Publisher) {
        throw "The signing certificate subject does not match the package manifest Publisher for '$platform'."
    }
    $manifests[$platform] = $manifest
}
$signTool = Get-Command signtool.exe -ErrorAction SilentlyContinue | Select-Object -First 1
if ($signTool) { $signToolPath = $signTool.Source }
else {
    $signTool = Get-ChildItem -LiteralPath (Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin') -Filter signtool.exe -File -Recurse |
        Where-Object { $_.FullName -match '\\x64\\signtool\.exe$' } |
        Sort-Object { [version] $_.Directory.Parent.Name } -Descending | Select-Object -First 1
    if (-not $signTool) { throw 'SignTool was not found. Install the Windows SDK signing tools.' }
    $signToolPath = $signTool.FullName
}
$targets = foreach ($machine in $machines) {
    $vm = @(Get-VM -Name ([WildcardPattern]::Escape($machine.Name)) -ErrorAction Stop)
    if ($vm.Count -ne 1 -or $vm[0].Name -ne $machine.Name) { throw "Expected one exact VM named '$($machine.Name)'." }
    $checkpoint = $null
    if ($RestoreCheckpoint) {
        $snapshots = @(Get-VMSnapshot -VM $vm[0] -Name ([WildcardPattern]::Escape($machine.CheckpointName)) -ErrorAction Stop)
        if ($snapshots.Count -ne 1) { throw "Expected one checkpoint '$($machine.CheckpointName)' for '$($machine.Name)'." }
        $checkpoint = $snapshots[0]
    }
    $guestCredential = $Credential
    if ($null -eq $guestCredential) {
        $prompt = @{ Message = "Guest account that will use the package in '$($machine.Name)' (local administrator)" }
        if (-not [string]::IsNullOrWhiteSpace([string] $machine.UserName)) { $prompt.UserName = [string] $machine.UserName }
        $guestCredential = Get-Credential @prompt
        if ($null -eq $guestCredential) { throw "No guest credential supplied for '$($machine.Name)'." }
    }
    [pscustomobject]@{ Config = $machine; VM = $vm[0]; Checkpoint = $checkpoint; Credential = $guestCredential }
}
$runId = "$(Get-Date -Format 'yyyyMMdd-HHmmss')-$([guid]::NewGuid().ToString('N'))"
$runDirectories = @{}
$payloads = @{}
foreach ($platform in @($machines.Platform | Sort-Object -Unique)) {
    $packageConfig = $packageConfigs[$platform]
    $manifest = $manifests[$platform]
    $runDirectory = Join-Path ([IO.Path]::GetFullPath($packageConfig.ArtifactsPath, $packageConfig.RepositoryRoot)) "hyperv\$runId"
    New-Item -ItemType Directory -Path $runDirectory -Force | Out-Null
    $runDirectories[$platform] = $runDirectory
    $buildParameters = @{ ConfigPath = $packageConfigPath; Configuration = $Configuration; Platform = $platform; BuildOnly = $true; Aot = $Aot }
    if ($VisualStudioPath) { $buildParameters.VisualStudioPath = $VisualStudioPath }
    $package = & (Join-Path $PSScriptRoot 'Deploy-Package.ps1') @buildParameters
    if ($package -isnot [IO.FileInfo]) { throw 'The build-only deployment did not return a single MSIX file.' }
    $identity = & (Join-Path $PSScriptRoot 'Get-MsixIdentity.ps1') -Path $package.FullName
    if ($identity.Name -cne [string] $manifest.Package.Identity.Name -or
        $identity.Publisher -cne $certificate.Subject -or $identity.Architecture -ine $platform -or
        $identity.Version -ne [version] $manifest.Package.Identity.Version -or $identity.HasSignature) {
        throw 'The built MSIX does not match the expected unsigned package identity.'
    }
    $payloadDirectory = Join-Path $runDirectory $platform
    New-Item -ItemType Directory -Path $payloadDirectory | Out-Null
    $signedPackagePath = Join-Path $payloadDirectory $package.Name
    Copy-Item -LiteralPath $package.FullName -Destination $signedPackagePath
    $signArguments = @('sign', '/fd', 'SHA256', '/s', 'My', '/sha1', $thumbprint)
    if ($config.CertificateStoreLocation -eq 'LocalMachine') { $signArguments += '/sm' }
    & $signToolPath @signArguments $signedPackagePath | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "SignTool failed with exit code $LASTEXITCODE." }
    $signedIdentity = & (Join-Path $PSScriptRoot 'Get-MsixIdentity.ps1') -Path $signedPackagePath
    if (-not $signedIdentity.HasSignature) { throw 'SignTool did not produce a signed MSIX.' }
    [IO.File]::WriteAllBytes((Join-Path $payloadDirectory 'DevCertificate.cer'), $certificate.RawData)
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Install-HyperVPackage.ps1') -Destination $payloadDirectory
    $dependencyRoot = Join-Path $package.DirectoryName 'Dependencies'
    foreach ($dependencyDirectory in @($dependencyRoot, (Join-Path $dependencyRoot $platform), (Join-Path $dependencyRoot 'neutral'))) {
        if (Test-Path -LiteralPath $dependencyDirectory -PathType Container) {
            foreach ($dependency in @(Get-ChildItem -LiteralPath $dependencyDirectory -File | Where-Object Extension -In @('.appx', '.msix'))) {
                $destination = Join-Path $payloadDirectory 'Dependencies'
                New-Item -ItemType Directory -Path $destination -Force | Out-Null
                Copy-Item -LiteralPath $dependency.FullName -Destination $destination
            }
        }
    }
    @{
        PackageFileName = $package.Name
        PackageHash = (Get-FileHash -LiteralPath $signedPackagePath -Algorithm SHA256).Hash
        Name = $identity.Name
        Publisher = $identity.Publisher
        Version = $identity.Version.ToString()
        Architecture = $identity.Architecture
        CertificateThumbprint = $thumbprint
        TrustCertificate = $TrustCertificate.IsPresent
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $payloadDirectory 'deployment.json') -Encoding UTF8
    $payloads[$platform] = $payloadDirectory
}

$results = [Collections.Generic.List[object]]::new()
Write-Host "Deployment artifacts: $(($runDirectories.Values | Sort-Object -Unique) -join ', ')"
foreach ($target in $targets) {
    $session = $null
    $runDirectory = $runDirectories[$target.Config.Platform]
    $guestDirectory = Join-Path $config.GuestPackageDirectory "$runId\$($target.Config.Platform)"
    $resultDirectory = Join-Path $runDirectory "results\$($target.VM.Id)"
    New-Item -ItemType Directory -Path $resultDirectory -Force | Out-Null
    $result = [pscustomobject]@{ VMName = $target.Config.Name; Platform = $target.Config.Platform; Configuration = $Configuration; Aot = $Aot.IsPresent; Succeeded = $false; Report = $null; Error = $null }
    try {
        if ($RestoreCheckpoint) { Restore-VMSnapshot -VMSnapshot $target.Checkpoint -Confirm:$false -ErrorAction Stop }
        $vm = Get-VM -Id $target.VM.Id -ErrorAction Stop
        if ($vm.State -in @('Off', 'Saved')) { Start-VM -VM $vm -Confirm:$false -ErrorAction Stop | Out-Null }
        elseif ($vm.State -ne 'Running') { throw "VM '$($vm.Name)' must be Running, Off, or Saved; current state is '$($vm.State)'." }
        Wait-VM -VM $vm -For Heartbeat -Timeout $BootTimeoutSeconds -ErrorAction Stop
        try {
            $session = New-PSSession -VMId $vm.Id -Credential $target.Credential -ErrorAction Stop
        }
        catch {
            if ($target.Credential.Password.Length -eq 0) {
                throw [InvalidOperationException]::new(
                    "PowerShell Direct connection failed: $($_.Exception.Message) The supplied guest password is empty; guest logon policy may reject blank passwords. Set a nonempty password for the guest account and retry.",
                    $_.Exception)
            }
            throw
        }
        Invoke-Command -Session $session -ArgumentList $guestDirectory -ScriptBlock {
            param($directory)
            New-Item -ItemType Directory -Path $directory -Force -ErrorAction Stop | Out-Null
        } -ErrorAction Stop
        foreach ($file in Get-ChildItem -LiteralPath $payloads[$target.Config.Platform]) {
            Copy-Item -LiteralPath $file.FullName -Destination $guestDirectory -ToSession $session -Recurse -ErrorAction Stop
        }
        Invoke-Command -Session $session -ArgumentList $guestDirectory -ScriptBlock {
            param($directory)
            $installer = Join-Path $directory 'Install-HyperVPackage.ps1'
            & "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $installer
            if ($LASTEXITCODE -ne 0) { throw "Guest installer failed with exit code $LASTEXITCODE. See install.log and result.json." }
        } -ErrorAction Stop | Out-Host
        $result.Report = Invoke-Command -Session $session -ArgumentList $guestDirectory -ScriptBlock {
            param($directory)
            Get-Content -LiteralPath (Join-Path $directory 'result.json') -Raw | ConvertFrom-Json
        } -ErrorAction Stop
        if (-not $result.Report.Succeeded) { throw $result.Report.Error }
        $result.Succeeded = $true
        Write-Host "Installed on '$($target.Config.Name)': $($result.Report.PackageFullName)"
    }
    catch {
        $result.Error = $_.Exception.Message
        Write-Warning "Deployment failed on '$($target.Config.Name)': $($result.Error)"
    }
    finally {
        if ($null -ne $session) {
            try {
                foreach ($fileName in @('install.log', 'result.json')) {
                    Copy-Item -LiteralPath (Join-Path $guestDirectory $fileName) -Destination $resultDirectory -FromSession $session -ErrorAction Stop
                }
            }
            catch { Write-Warning "Could not collect all guest logs for '$($target.Config.Name)': $($_.Exception.Message)" }
            Remove-PSSession -Session $session -ErrorAction SilentlyContinue
        }
        $results.Add($result)
        foreach ($directory in $runDirectories.Values | Sort-Object -Unique) {
            ConvertTo-Json -InputObject @($results.ToArray()) -Depth 8 | Set-Content -LiteralPath (Join-Path $directory 'results.json') -Encoding UTF8
        }
    }
}
if (@($results | Where-Object { -not $_.Succeeded }).Count -gt 0) {
    throw "One or more VM deployments failed. See results.json under: $(($runDirectories.Values | Sort-Object -Unique) -join ', ')."
}
$results.ToArray()
