<#
.SYNOPSIS
Checks Hyper-V deployment with temporary packages, an ephemeral certificate,
and mocked VM, signing, and AppX commands. Does not access real VMs or stores.
#>

[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Test-PublishingFixture.ps1')
$testRoot = Join-Path ([IO.Path]::GetTempPath()) "HyperV Tests [fixture]-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $testRoot | Out-Null
$originalExitCode = Get-Variable LASTEXITCODE -ValueOnly -ErrorAction SilentlyContinue
$originalArchitecture = $env:PROCESSOR_ARCHITECTURE
$environmentNames = @(
    'MSIX_HYPERV_CONFIG_PATH', 'MSIX_HYPERV_CERTIFICATE_THUMBPRINT', 'MSIX_HYPERV_CERTIFICATE_STORE_LOCATION',
    'MSIX_HYPERV_GUEST_PACKAGE_DIRECTORY', 'MSIX_HYPERV_VM_NAMES', 'MSIX_HYPERV_GUEST_USERNAME',
    'MSIX_HYPERV_PLATFORM', 'MSIX_HYPERV_CONFIGURATION'
)
$originalEnvironment = @{}
foreach ($environmentName in $environmentNames) {
    $originalEnvironment[$environmentName] = [Environment]::GetEnvironmentVariable($environmentName, 'Process')
}
$rsa = [Security.Cryptography.RSA]::Create(2048)
$request = [Security.Cryptography.X509Certificates.CertificateRequest]::new(
    'CN=ExamplePublisher', $rsa, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pkcs1)
$certificate = $request.CreateSelfSigned([DateTimeOffset]::Now.AddDays(-1), [DateTimeOffset]::Now.AddDays(10))
$scriptPath = Join-Path $testRoot 'Deploy-HyperVPackage.ps1'
$configPath = Join-Path $testRoot 'HyperV.config.psd1'
foreach ($fileName in @('Deploy-HyperVPackage.ps1', 'Install-HyperVPackage.ps1', 'Get-MsixIdentity.ps1', 'Get-PackageConfig.ps1', 'Get-MSBuildEvaluation.ps1')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $fileName) -Destination $testRoot
}
$packageConfigPath = New-TestWorkflowRepository -Directory $testRoot
@'
[CmdletBinding()]
param($ConfigPath, $Configuration, $Platform, [switch] $BuildOnly, [switch] $Aot, $VisualStudioPath)
if (-not $BuildOnly) { throw 'VM deployment must not register a local package.' }
$state.Builds.Add(@{ Platform = $Platform; Aot = $Aot.IsPresent; Configuration = $Configuration })
if ($state.Mode -eq 'build-failure') { throw 'Mock build failed.' }
$path = Join-Path $PSScriptRoot "build-$Platform.msix"
if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path }
$publisher = if ($state.Mode -eq 'wrong-package') { 'CN=WrongPublisher' } else { 'CN=ExamplePublisher' }
$identityName = 'Contoso.ExampleExtension'
$version = '0.4.0.0'
if ($state.Mode -eq 'conditional-settings') {
    $identityName += ".$Platform"
    $version = if ($Platform -eq 'ARM64') { '0.6.0.0' } else { '0.5.0.0' }
}
New-TestMsix -Path $path -Architecture $Platform -IdentityName $identityName -Publisher $publisher -Version $version
$dependencies = Join-Path $PSScriptRoot "Dependencies\$Platform"
New-Item -ItemType Directory -Path $dependencies -Force | Out-Null
Set-Content -LiteralPath (Join-Path $dependencies 'Framework.appx') -Value 'dependency fixture'
Get-Item -LiteralPath $path
'@ | Set-Content -LiteralPath (Join-Path $testRoot 'Deploy-Package.ps1')
@'
$state.Signs++
$global:LASTEXITCODE = if ($state.Mode -eq 'sign-failure') { 1 } else { 0 }
if ($LASTEXITCODE -ne 0) { return }
$archive = [IO.Compression.ZipFile]::Open($args[-1], [IO.Compression.ZipArchiveMode]::Update)
try { $archive.CreateEntry('AppxSignature.p7x') | Out-Null }
finally { $archive.Dispose() }
'@ | Set-Content -LiteralPath (Join-Path $testRoot 'Mock SignTool.ps1')

function Assert-True([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}

function Reset-TestCase([string] $Mode = 'success') {
    foreach ($environmentName in $environmentNames) { [Environment]::SetEnvironmentVariable($environmentName, $null, 'Process') }
    $script:state = @{
        Mode = $Mode
        Builds = [Collections.Generic.List[object]]::new()
        Signs = 0
        Starts = 0
        Restores = 0
        Sessions = 0
        RemovedSessions = 0
        Imports = 0
        Installs = [Collections.Generic.List[object]]::new()
        CurrentVM = ''
        Installed = @{}
        Credentials = [Collections.Generic.List[object]]::new()
        CertificatePaths = [Collections.Generic.List[string]]::new()
    }
    $script:vmIds = @{ Win10 = [guid]::NewGuid(); Win11 = [guid]::NewGuid() }
    @"
@{
    PackageConfigPath = 'Package.config.psd1'
    CertificateThumbprint = '$($certificate.Thumbprint)'
    CertificateStoreLocation = 'CurrentUser'
    GuestPackageDirectory = '$($testRoot.Replace("'", "''"))\guest'
    VirtualMachines = @(
        @{ Name = 'Win10'; Platform = 'x64'; UserName = 'TestUser'; CheckpointName = 'Baseline' }
        @{ Name = 'Win11'; Platform = 'x64'; UserName = 'TestUser'; CheckpointName = 'Baseline' }
    )
}
"@ | Set-Content -LiteralPath $configPath
}

function Import-Module { param($Name, $ErrorAction) Assert-True ($Name -eq 'Hyper-V') 'Unexpected module import.' }
function Get-Command { param($Name, $ErrorAction) [pscustomobject]@{ Source = (Join-Path $testRoot 'Mock SignTool.ps1') } }
function Get-Item {
    param($LiteralPath)
    if ($LiteralPath -like 'Cert:*') {
        $state.CertificatePaths.Add($LiteralPath)
        return $certificate
    }
    Microsoft.PowerShell.Management\Get-Item -LiteralPath $LiteralPath
}
function Test-Path {
    param($LiteralPath, $PathType = 'Any')
    if ($LiteralPath -like 'Cert:*') { return $false }
    Microsoft.PowerShell.Management\Test-Path -LiteralPath $LiteralPath -PathType $PathType
}
function Get-VM {
    param($Name, $Id, $ErrorAction)
    if ($Id) { $Name = @($vmIds.Keys | Where-Object { $vmIds[$_] -eq $Id })[0] }
    if ($state.Mode -eq 'missing-vm') { throw 'Mock VM not found.' }
    [pscustomobject]@{ Name = $Name; Id = $vmIds[$Name]; State = 'Off' }
}
function Get-VMSnapshot { param($VM, $Name, $ErrorAction) [pscustomobject]@{ Name = $Name; VMId = $VM.Id } }
function Restore-VMSnapshot { param($VMSnapshot, $Confirm, $ErrorAction) $state.Restores++ }
function Start-VM { param($VM, $Confirm, $ErrorAction) $state.Starts++ }
function Wait-VM { param($VM, $For, $Timeout, $ErrorAction) }
function Get-Credential {
    param($Message, $UserName)
    if ([string]::IsNullOrWhiteSpace($UserName)) { $UserName = 'PromptedTestUser' }
    [pscredential]::new($UserName, (ConvertTo-SecureString 'test-only' -AsPlainText -Force))
}
function New-PSSession {
    param($VMId, $Credential, $ErrorAction)
    $state.Sessions++
    $state.Credentials.Add($Credential)
    if ($state.Mode -eq 'session-failure') { throw 'The credential is invalid.' }
    [pscustomobject]@{ VMId = $VMId }
}
function Remove-PSSession { param($Session, $ErrorAction) $state.RemovedSessions++ }
function Get-MockGuestPath($Session, [string] $Path) {
    $guestRoot = Join-Path $testRoot 'guest'
    Assert-True ($Path.StartsWith($guestRoot)) 'Unexpected guest directory.'
    (Join-Path $testRoot "guest-$($Session.VMId)") + $Path.Substring($guestRoot.Length)
}
function Copy-Item {
    param($LiteralPath, $Destination, $ToSession, $FromSession, [switch] $Recurse, $ErrorAction)
    if ($ToSession) { $Destination = Get-MockGuestPath $ToSession $Destination }
    if ($FromSession) { $LiteralPath = Get-MockGuestPath $FromSession $LiteralPath }
    Assert-True ($LiteralPath.StartsWith($testRoot) -and $Destination.StartsWith($testRoot)) 'A test attempted to copy outside its temporary directory.'
    Microsoft.PowerShell.Management\Copy-Item -LiteralPath $LiteralPath -Destination $Destination -Recurse:$Recurse
}
function Invoke-Command {
    param($Session, [object[]] $ArgumentList, $ScriptBlock, $ErrorAction)
    $state.CurrentVM = [string] $Session.VMId
    $ArgumentList = @($ArgumentList | ForEach-Object { Get-MockGuestPath $Session $_ })
    if ($ScriptBlock.ToString().Contains('powershell.exe')) {
        $directory = $ArgumentList[0]
        $deploymentPath = Join-Path $directory 'deployment.json'
        $deployment = Get-Content -LiteralPath $deploymentPath -Raw | ConvertFrom-Json
        $env:PROCESSOR_ARCHITECTURE = if ($deployment.Architecture -eq 'arm64') { 'ARM64' } else { 'AMD64' }
        if ($state.Mode -eq 'tampered-package') {
            Add-Content -LiteralPath (Join-Path $directory $deployment.PackageFileName) -Value 'tampered'
        }
        if ($state.Mode -eq 'wrong-certificate') {
            $deployment.CertificateThumbprint = '0' * 40
            $deployment | ConvertTo-Json | Set-Content -LiteralPath $deploymentPath
        }
        if ($state.Mode -eq 'downgrade') {
            $state.Installed[$state.CurrentVM] = [pscustomobject]@{ Version = '9.0.0.0'; Publisher = 'CN=ExamplePublisher' }
        }
        & (Join-Path $directory 'Install-HyperVPackage.ps1')
        return
    }
    & $ScriptBlock @ArgumentList
}
function Start-Transcript { param($LiteralPath, [switch] $Force) Set-Content -LiteralPath $LiteralPath -Value 'mock transcript' }
function Stop-Transcript { }
function Import-Certificate { param($FilePath, $CertStoreLocation) $state.Imports++ }
function Get-AppxPackage {
    param($Name)
    if ($state.Installed.ContainsKey($state.CurrentVM)) { $state.Installed[$state.CurrentVM] }
}
function Add-AppxPackage {
    param($Path, $DependencyPath, $ForceTargetApplicationShutdown, $ForceUpdateFromAnyVersion, $ErrorAction)
    if ($state.Mode -eq 'guest-failure' -and $state.CurrentVM -eq [string] $vmIds.Win10) { throw 'Mock AppX installation failed.' }
    Assert-True ($ForceUpdateFromAnyVersion -and $ForceTargetApplicationShutdown) 'Same-version development updates must be supported.'
    Assert-True (@($DependencyPath).Count -eq 1) 'The matching WAP dependency was not forwarded.'
    $state.Installs.Add($Path)
    $identity = & (Join-Path $testRoot 'Get-MsixIdentity.ps1') -Path $Path
    $state.Installed[$state.CurrentVM] = [pscustomobject]@{
        Version = $identity.Version
        Publisher = $identity.Publisher
        Architecture = $identity.Architecture
        PackageFullName = "Contoso.ExampleExtension_$($identity.Version)_$($identity.Architecture)"
    }
}

function Assert-Rejected([string] $ExpectedMessage, [pscredential] $TestCredential) {
    $failure = $null
    $parameters = @{ ConfigPath = $configPath; TrustCertificate = $true }
    if ($null -ne $TestCredential) { $parameters.Credential = $TestCredential }
    try { & $scriptPath @parameters | Out-Null }
    catch { $failure = $_.Exception.Message }
    Assert-True ($null -ne $failure -and $failure -like "*$ExpectedMessage*") "Expected '$ExpectedMessage', received '$failure'."
}

try {
    Reset-TestCase
    & $scriptPath -ConfigPath $configPath -WhatIf
    Assert-True ($state.Builds.Count -eq 0 -and $state.Sessions -eq 0 -and $state.Signs -eq 0) 'WhatIf changed the host or a VM.'

    foreach ($case in @(
        @("CertificateThumbprint = '$($certificate.Thumbprint)'", "CertificateThumbprint = 'invalid'", '40 hexadecimal'),
        @("Name = 'Win11'", "Name = 'Win10'", 'unique, nonempty'),
        @("Platform = 'x64'", "Platform = 'x86'", 'Unsupported platform')
    )) {
        Reset-TestCase
        (Get-Content -LiteralPath $configPath -Raw).Replace($case[0], $case[1]) | Set-Content -LiteralPath $configPath
        Assert-Rejected $case[2]
        Assert-True ($state.Builds.Count -eq 0 -and $state.Sessions -eq 0) 'Invalid configuration reached a build or VM.'
    }

    Reset-TestCase
    $results = @(& $scriptPath -ConfigPath $configPath -TrustCertificate)
    Assert-True ($results.Count -eq 2 -and @($results | Where-Object { $_.Succeeded }).Count -eq 2) 'Both guests must be verified.'
    Assert-True ($state.Builds.Count -eq 1 -and $state.Signs -eq 1) 'The same architecture was built or signed more than once.'
    Assert-True ($state.Builds[0].Aot -and $state.Builds[0].Configuration -eq 'Release') 'Default VM deployment must build Release with Native AOT.'
    Assert-True (@($results | Where-Object { $_.Aot -and $_.Configuration -eq 'Release' }).Count -eq 2) 'Deployment reports must record the build mode.'
    Assert-True ($state.Restores -eq 0 -and $state.Installs.Count -eq 2 -and $state.Imports -eq 2) 'Default deployment restored a checkpoint or failed to install.'
    Assert-True ($state.Sessions -eq 2 -and $state.RemovedSessions -eq 2) 'Sessions were not released.'
    $publicCertificatePath = Join-Path (Split-Path -Parent $state.Installs[0]) 'DevCertificate.cer'
    $publicCertificate = [Security.Cryptography.X509Certificates.X509Certificate2]::new([IO.File]::ReadAllBytes($publicCertificatePath))
    try { Assert-True (-not $publicCertificate.HasPrivateKey) 'A private key was copied into a guest.' }
    finally { $publicCertificate.Dispose() }
    Assert-True (@(Get-ChildItem -LiteralPath $testRoot -Recurse -Filter results.json).Count -eq 1) 'The run summary was not saved.'

    $propsPath = Join-Path $testRoot 'CmdPal.Extension.props'
    $originalProps = Get-Content -LiteralPath $propsPath -Raw
    $conditionalProps = $originalProps.Replace('</Project>', @'
  <PropertyGroup Condition="'$(Configuration)' == 'Debug'">
    <CmdPalRuntimeIdentifiers>win-$(Platform.ToLowerInvariant())</CmdPalRuntimeIdentifiers>
    <CmdPalPackageManifest>$(CmdPalRepositoryRoot)Debug/$(Platform)/Package.appxmanifest</CmdPalPackageManifest>
    <CmdPalArtifactsPath>$(CmdPalRepositoryRoot)output/Debug/$(Platform)</CmdPalArtifactsPath>
  </PropertyGroup>
</Project>
'@)
    try {
        Set-Content -LiteralPath $propsPath -Value $conditionalProps
        foreach ($platform in @('x64', 'ARM64')) {
            $directory = Join-Path $testRoot "Debug/$platform"
            New-Item -ItemType Directory -Path $directory -Force | Out-Null
            $version = if ($platform -eq 'ARM64') { '0.6.0.0' } else { '0.5.0.0' }
            "<Package><Identity Name='Contoso.ExampleExtension.$platform' Publisher='CN=ExamplePublisher' Version='$version' /></Package>" |
                Set-Content -LiteralPath (Join-Path $directory 'Package.appxmanifest')
        }
        Reset-TestCase 'conditional-settings'
        (Get-Content -LiteralPath $configPath -Raw).Replace("Name = 'Win11'; Platform = 'x64'", "Name = 'Win11'; Platform = 'ARM64'") |
            Set-Content -LiteralPath $configPath
        $results = @(& $scriptPath -ConfigPath $configPath -Configuration Debug)
        Assert-True ($results.Count -eq 2 -and @($results | Where-Object Succeeded).Count -eq 2 -and $state.Builds.Count -eq 2) 'Conditional runtime and manifest settings were not evaluated for both target platforms.'
        foreach ($platform in @('x64', 'ARM64')) {
            $outputRoot = Join-Path $testRoot "output/Debug/$platform/hyperv"
            $reports = @(Get-ChildItem -LiteralPath $outputRoot -Recurse -Filter results.json)
            Assert-True ($reports.Count -eq 1) 'The platform artifact directory did not receive a run summary.'
            $report = @(Get-Content -LiteralPath $reports[0].FullName -Raw | ConvertFrom-Json)
            Assert-True ($report.Count -eq 2 -and @($report | Where-Object Succeeded).Count -eq 2) 'Per-platform output roots must retain the complete run summary.'
            Assert-True (@(Get-ChildItem -LiteralPath $outputRoot -Recurse -Filter '*.msix').Count -eq 1 -and
                @(Get-ChildItem -LiteralPath $outputRoot -Recurse -Filter install.log).Count -eq 1) 'Payloads or guest logs used the wrong platform artifact root.'
        }

        Reset-TestCase 'conditional-settings'
        (Get-Content -LiteralPath $configPath -Raw).Replace("Name = 'Win10'; Platform = 'x64'", "Name = 'Win10'; Platform = 'x86'").
            Replace("Name = 'Win11'; Platform = 'x64'", "Name = 'Win11'; Platform = 'ARM64'") | Set-Content -LiteralPath $configPath
        $results = @(& $scriptPath -ConfigPath $configPath -Configuration Debug -VMName Win11)
        Assert-True ($results.Count -eq 1 -and $results[0].Succeeded -and $state.Builds.Count -eq 1 -and $state.Builds[0].Platform -eq 'ARM64') 'Only selected VM platforms should be evaluated and built.'

        foreach ($invalidSetting in @('runtime', 'publisher')) {
            Reset-TestCase 'conditional-settings'
            (Get-Content -LiteralPath $configPath -Raw).Replace("Name = 'Win11'; Platform = 'x64'", "Name = 'Win11'; Platform = 'ARM64'") |
                Set-Content -LiteralPath $configPath
            Set-Content -LiteralPath $propsPath -Value $conditionalProps
            if ($invalidSetting -eq 'runtime') {
                $conditionalProps.Replace('win-$(Platform.ToLowerInvariant())', 'win-x64') | Set-Content -LiteralPath $propsPath
                $expected = "Unsupported platform 'ARM64'"
            }
            else {
                $manifestPath = Join-Path $testRoot 'Debug/ARM64/Package.appxmanifest'
                (Get-Content -LiteralPath $manifestPath -Raw).Replace('CN=ExamplePublisher', 'CN=WrongPublisher') | Set-Content -LiteralPath $manifestPath
                $expected = 'certificate subject does not match'
            }
            $failure = $null
            try { & $scriptPath -ConfigPath $configPath -Configuration Debug | Out-Null }
            catch { $failure = $_.Exception.Message }
            Assert-True ($failure -like "*$expected*" -and $state.Builds.Count -eq 0 -and $state.Sessions -eq 0) 'An invalid target platform runtime or publisher reached a build or VM.'
        }
    }
    finally { Set-Content -LiteralPath $propsPath -Value $originalProps }

    Reset-TestCase
    (Get-Content -LiteralPath $configPath -Raw).Replace("Name = 'Win11'; Platform = 'x64'", "Name = 'Win11'; Platform = 'ARM64'") | Set-Content -LiteralPath $configPath
    $credential = [pscredential]::new('SharedTestUser', (ConvertTo-SecureString 'test-only' -AsPlainText -Force))
    & $scriptPath -ConfigPath $configPath -Credential $credential -RestoreCheckpoint -Aot:$false -Configuration Debug | Out-Null
    Assert-True ($state.Builds.Count -eq 2 -and $state.Signs -eq 2 -and $state.Restores -eq 2) 'Architecture grouping or explicit checkpoint restore failed.'
    Assert-True (@($state.Builds | Where-Object { -not $_.Aot -and $_.Configuration -eq 'Debug' }).Count -eq 2) 'Explicit managed Debug build options were not forwarded.'
    Assert-True ($state.Imports -eq 0 -and $state.Credentials[0] -eq $credential) 'Certificate trust must be opt-in and the supplied credential must be reused.'

    Reset-TestCase
    $results = @(& $scriptPath -ConfigPath $configPath -VMName Win11)
    Assert-True ($results.Count -eq 1 -and $results[0].VMName -eq 'Win11') 'VM selection was ignored.'
    $failure = $null
    try { & $scriptPath -ConfigPath $configPath -VMName Unknown -WhatIf }
    catch { $failure = $_.Exception.Message }
    Assert-True ($failure -like '*not configured*') 'An unknown VM name was accepted.'

    Reset-TestCase
    $absoluteConfig = Get-Content -LiteralPath $configPath -Raw
    $absoluteConfig.Replace("PackageConfigPath = 'Package.config.psd1'", "PackageConfigPath = '$($packageConfigPath.Replace("'", "''"))'") |
        Set-Content -LiteralPath $configPath
    & $scriptPath -ConfigPath $configPath -VMName Win10 | Out-Null
    Assert-True ($state.Installs.Count -eq 1) 'An absolute package configuration path was not resolved.'

    Reset-TestCase
    $exampleConfig = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'HyperV.config.example.psd1') -Raw
    $exampleConfig.Replace('REPLACE_WITH_VM_NAME', 'Win10').Replace("CertificateThumbprint = ''", "CertificateThumbprint = '$($certificate.Thumbprint)'").
        Replace('# GuestPackageDirectory', 'GuestPackageDirectory').
        Replace('C:\ProgramData\MSIXDeployments', (Join-Path $testRoot 'guest').Replace("'", "''")) |
        Set-Content -LiteralPath $configPath
    $results = @(& $scriptPath -ConfigPath $configPath)
    Assert-True ($results.Count -eq 1 -and $results[0].Succeeded -and $state.Builds[0].Platform -eq 'x64') 'The shared example did not work with a contributor-owned certificate and VM.'
    Assert-True ($state.Credentials[0].UserName -eq 'PromptedTestUser' -and $state.Restores -eq 0) 'The example required a preset username or checkpoint.'
    $failure = $null
    try { & $scriptPath -ConfigPath $configPath -RestoreCheckpoint -WhatIf }
    catch { $failure = $_.Exception.Message }
    Assert-True ($failure -like '*no CheckpointName configured*') 'Checkpoint restore did not require an explicitly configured checkpoint.'

    Reset-TestCase
    $environmentConfig = (Get-Content -LiteralPath $configPath -Raw).
        Replace("CertificateThumbprint = '$($certificate.Thumbprint)'", "CertificateThumbprint = ''").
        Replace("Platform = 'x64'; ", '').Replace("UserName = 'TestUser'; ", '')
    $environmentConfig = $environmentConfig -replace '(?m)^[ \t]*(CertificateStoreLocation|GuestPackageDirectory)[^\r\n]*\r?\n', ''
    $environmentConfig | Set-Content -LiteralPath $configPath
    $env:MSIX_HYPERV_CONFIG_PATH = $configPath
    $env:MSIX_HYPERV_CERTIFICATE_THUMBPRINT = $certificate.Thumbprint
    $env:MSIX_HYPERV_CERTIFICATE_STORE_LOCATION = 'LocalMachine'
    $env:MSIX_HYPERV_GUEST_PACKAGE_DIRECTORY = Join-Path $testRoot 'guest-environment'
    $env:MSIX_HYPERV_VM_NAMES = ' ; Win11; ; '
    $env:MSIX_HYPERV_GUEST_USERNAME = 'EnvironmentGuest'
    $env:MSIX_HYPERV_PLATFORM = 'ARM64'
    $env:MSIX_HYPERV_CONFIGURATION = 'Debug'
    $results = @(& $scriptPath)
    Assert-True ($results.Count -eq 1 -and $results[0].VMName -eq 'Win11' -and $results[0].Succeeded) 'The environment config path or VM selection was ignored.'
    Assert-True ($state.Builds[0].Platform -eq 'ARM64' -and $state.Builds[0].Configuration -eq 'Debug') 'Environment build defaults were ignored.'
    Assert-True ($state.Credentials[0].UserName -eq 'EnvironmentGuest') 'The environment username did not pre-fill the credential prompt.'
    Assert-True ($state.CertificatePaths[0] -eq "Cert:\LocalMachine\My\$($certificate.Thumbprint)" -and $state.Installs[0].Contains('-environment')) 'Environment certificate or staging defaults were ignored.'
    Assert-True ($state.Imports -eq 0 -and $state.Restores -eq 0) 'Environment defaults enabled certificate trust or checkpoint restore.'

    Reset-TestCase
    $env:MSIX_HYPERV_CONFIG_PATH = Join-Path $testRoot 'missing-config.psd1'
    $env:MSIX_HYPERV_CERTIFICATE_THUMBPRINT = 'invalid'
    $env:MSIX_HYPERV_CERTIFICATE_STORE_LOCATION = 'invalid'
    $env:MSIX_HYPERV_GUEST_PACKAGE_DIRECTORY = 'relative-directory'
    $env:MSIX_HYPERV_VM_NAMES = 'Unknown'
    $env:MSIX_HYPERV_GUEST_USERNAME = 'EnvironmentGuest'
    $env:MSIX_HYPERV_PLATFORM = 'x86'
    $env:MSIX_HYPERV_CONFIGURATION = 'Debug'
    $results = @(& $scriptPath -ConfigPath $configPath -VMName Win10 -Configuration Release)
    Assert-True ($results.Count -eq 1 -and $results[0].VMName -eq 'Win10' -and $results[0].Succeeded) 'Explicit parameters did not override environment defaults.'
    Assert-True ($state.Builds[0].Platform -eq 'x64' -and $state.Builds[0].Configuration -eq 'Release') 'Explicit platform or configuration lost to an environment default.'
    Assert-True ($state.Credentials[0].UserName -eq 'TestUser' -and $state.CertificatePaths[0] -eq "Cert:\CurrentUser\My\$($certificate.Thumbprint)") 'Local configuration did not override environment defaults.'

    Reset-TestCase
    foreach ($environmentName in $environmentNames) { [Environment]::SetEnvironmentVariable($environmentName, ' ', 'Process') }
    (Get-Content -LiteralPath $configPath -Raw).Replace("Platform = 'x64'; ", '').Replace("UserName = 'TestUser'; ", '') |
        Set-Content -LiteralPath $configPath
    $results = @(& $scriptPath -ConfigPath $configPath)
    Assert-True ($results.Count -eq 2 -and $state.Builds[0].Platform -eq 'x64' -and $state.Builds[0].Configuration -eq 'Release') 'Blank environment values displaced repository defaults.'
    Assert-True ($state.Credentials[0].UserName -eq 'PromptedTestUser') 'A blank environment username suppressed the normal credential prompt.'

    Reset-TestCase
    $env:MSIX_HYPERV_VM_NAMES = '; ;'
    Assert-Rejected 'at least one VM name'
    Assert-True ($state.Builds.Count -eq 0 -and $state.Sessions -eq 0) 'An empty environment VM list reached a build or VM.'

    Reset-TestCase
    (Get-Content -LiteralPath $configPath -Raw).Replace("CertificateThumbprint = '$($certificate.Thumbprint)'", "CertificateThumbprint = ''") |
        Set-Content -LiteralPath $configPath
    $env:MSIX_HYPERV_CERTIFICATE_THUMBPRINT = 'invalid'
    Assert-Rejected '40 hexadecimal'
    Assert-True ($state.Builds.Count -eq 0 -and $state.Sessions -eq 0) 'An invalid environment certificate reached a build or VM.'

    Reset-TestCase
    Remove-Item -LiteralPath $configPath
    $env:MSIX_HYPERV_CERTIFICATE_THUMBPRINT = $certificate.Thumbprint
    $env:MSIX_HYPERV_GUEST_PACKAGE_DIRECTORY = Join-Path $testRoot 'guest-environment'
    $env:MSIX_HYPERV_GUEST_USERNAME = 'EnvironmentGuest'
    $env:MSIX_HYPERV_VM_NAMES = 'Win10;Win11'
    $results = @(& $scriptPath)
    Assert-True ($results.Count -eq 2 -and @($results | Where-Object { $_.Succeeded }).Count -eq 2) 'Environment-only deployment required a Hyper-V configuration file.'
    Assert-True ($state.Builds.Count -eq 1 -and $state.Builds[0].Platform -eq 'x64' -and $state.Builds[0].Configuration -eq 'Release') 'Environment-only deployment lost the repository defaults.'
    Assert-True ($state.Credentials[0].UserName -eq 'EnvironmentGuest' -and $state.Restores -eq 0) 'Environment-only deployment ignored the username or restored a checkpoint.'
    Assert-True (-not (Test-Path -LiteralPath $configPath)) 'Environment-only deployment wrote a local configuration file.'

    Reset-TestCase
    Remove-Item -LiteralPath $configPath
    $env:MSIX_HYPERV_CERTIFICATE_THUMBPRINT = $certificate.Thumbprint
    $env:MSIX_HYPERV_GUEST_PACKAGE_DIRECTORY = Join-Path $testRoot 'guest-environment'
    $env:MSIX_HYPERV_VM_NAMES = 'Unknown'
    & $scriptPath -VMName Win10 -WhatIf
    Assert-True ($state.Builds.Count -eq 0 -and $state.Sessions -eq 0 -and $state.CertificatePaths.Count -eq 0) 'A dry run without a config file built, signed, or contacted a VM.'
    $results = @(& $scriptPath -VMName Win10)
    Assert-True ($results.Count -eq 1 -and $results[0].VMName -eq 'Win10' -and $results[0].Succeeded) 'Explicit VM names did not work without a config file or override the environment selection.'

    Reset-TestCase
    Remove-Item -LiteralPath $configPath
    $env:MSIX_HYPERV_CERTIFICATE_THUMBPRINT = $certificate.Thumbprint
    $failure = $null
    try { & $scriptPath -WhatIf }
    catch { $failure = $_.Exception.Message }
    Assert-True ($failure -like '*No virtual machines specified*') 'A missing VM selection did not explain how to provide target names.'
    Assert-True ($state.Builds.Count -eq 0 -and $state.Sessions -eq 0) 'A missing VM selection reached a build or VM.'

    foreach ($useEnvironmentPath in @($false, $true)) {
        Reset-TestCase
        $missingPath = Join-Path $testRoot 'missing-explicit-config.psd1'
        $failure = $null
        try {
            if ($useEnvironmentPath) {
                $env:MSIX_HYPERV_CONFIG_PATH = $missingPath
                & $scriptPath -VMName Win10 -WhatIf
            }
            else { & $scriptPath -ConfigPath $missingPath -VMName Win10 -WhatIf }
        }
        catch { $failure = $_.Exception.Message }
        Assert-True ($failure -like '*Hyper-V configuration was not found*') 'An explicitly selected missing config silently fell back to another configuration.'
        Assert-True ($state.Builds.Count -eq 0 -and $state.Sessions -eq 0) 'A missing explicit config reached a build or VM.'
    }

    foreach ($case in @(@('missing-vm', 'Mock VM not found'), @('build-failure', 'Mock build failed'), @('sign-failure', 'SignTool failed'), @('wrong-package', 'unsigned package identity'))) {
        Reset-TestCase $case[0]
        Assert-Rejected $case[1]
        Assert-True ($state.Sessions -eq 0 -and $state.Starts -eq 0) 'A preflight or build failure mutated a VM.'
    }
    foreach ($mode in @('tampered-package', 'wrong-certificate', 'downgrade')) {
        Reset-TestCase $mode
        Assert-Rejected 'VM deployments failed'
        Assert-True ($state.Installs.Count -eq 0 -and $state.Imports -eq 0) 'Invalid or downgraded packages modified the guest.'
        Assert-True ($state.RemovedSessions -eq 2) 'Guest rejection leaked a session.'
    }
    Reset-TestCase 'guest-failure'
    Assert-Rejected 'VM deployments failed'
    Assert-True ($state.Installs.Count -eq 1 -and $state.RemovedSessions -eq 2) 'A failed guest prevented deployment to the remaining VM or leaked sessions.'

    Reset-TestCase
    $emptyCredential = [pscredential]::new('TestUser', [Security.SecureString]::new())
    $results = @(& $scriptPath -ConfigPath $configPath -VMName Win10 -Credential $emptyCredential)
    Assert-True ($results[0].Succeeded -and $state.Credentials[0] -eq $emptyCredential -and $state.Credentials[0].Password.Length -eq 0) 'An empty password was changed or rejected before the transport could authenticate it.'
    Assert-True ($state.RemovedSessions -eq 1) 'A successful empty-password session was not released.'

    foreach ($testCredential in @($emptyCredential, $credential)) {
        Reset-TestCase 'session-failure'
        $previousReports = @(Get-ChildItem -LiteralPath $testRoot -Recurse -File -Filter results.json | Select-Object -ExpandProperty FullName)
        Assert-Rejected 'VM deployments failed' $testCredential
        $newReports = @(Get-ChildItem -LiteralPath $testRoot -Recurse -File -Filter results.json | Where-Object FullName -NotIn $previousReports)
        Assert-True ($newReports.Count -eq 1) 'Connection failures did not produce a run report.'
        $failures = @(Get-Content -LiteralPath $newReports[0].FullName -Raw | ConvertFrom-Json)
        Assert-True ($failures.Count -eq 2 -and @($failures | Where-Object { $_.Error -like '*The credential is invalid.*' }).Count -eq 2) 'The original guest authentication error was lost.'
        $hintCount = @($failures | Where-Object { $_.Error -like '*guest password is empty*' }).Count
        Assert-True ($hintCount -eq $(if ($testCredential.Password.Length -eq 0) { 2 } else { 0 })) 'The empty-password hint was missing or applied to a nonempty password.'
        Assert-True ($state.Sessions -eq 2 -and $state.RemovedSessions -eq 0 -and $state.Installs.Count -eq 0) 'A failed authentication stopped remaining attempts or reported an installed package.'
    }

    Write-Output 'Passed Hyper-V deployment checks for guest authentication diagnostics, optional configuration, environment defaults and precedence, dry runs, VM selection, signing, public-only certificates, dependencies, architecture grouping, build options, checkpoint opt-in, tampering, downgrades, failure reporting, and session cleanup.'
}
finally {
    $certificate.Dispose()
    $rsa.Dispose()
    $env:PROCESSOR_ARCHITECTURE = $originalArchitecture
    foreach ($environmentName in $environmentNames) { [Environment]::SetEnvironmentVariable($environmentName, $originalEnvironment[$environmentName], 'Process') }
    $global:LASTEXITCODE = if ($null -eq $originalExitCode) { 0 } else { $originalExitCode }
    $resolvedTestRoot = [IO.Path]::GetFullPath($testRoot)
    $temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (-not $resolvedTestRoot.StartsWith($temporaryRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove test data outside '$temporaryRoot'."
    }
    Remove-Item -LiteralPath $resolvedTestRoot -Recurse -Force
}
