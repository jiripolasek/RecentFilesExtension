# Hyper-V development deployment

[Development guide](README.md)

[`Deploy-HyperVPackage.ps1`](../../eng/Deploy-HyperVPackage.ps1) builds a signed
test package once per selected architecture and deploys it to your existing
Hyper-V VMs. It reuses `Deploy-Package.ps1 -BuildOnly` and `Get-PackageConfig.ps1`,
which evaluates paths and platforms from `CmdPal.Extension.props` and reads
script defaults from `Package.config.psd1`. Native AOT and trimming are enabled by
default; the repository configuration selects Release. It does not register
a package on the host.

Each contributor supplies VM names and a development certificate through
environment defaults or an optional ignored local configuration. Guest credentials
are prompted for or passed as a `PSCredential`. The package identity and publisher
come from the configured manifest. No maintainer certificate or private key is
needed. Run the commands below from the repository root.

## Quick start (TL;DR)

This is the path from a fresh host/VM setup to testing one VM. **Host** means
your development PC; **guest** means the Windows VM. No `HyperV.config.psd1`
file is needed for this path.

1. **Host: install the tools.** Install PowerShell 7, the .NET 11 SDK, and
   Visual Studio with DesktopBridge/MSIX Packaging Tools and a Windows SDK
   containing MakeAppx and SignTool ([build requirements](WapPackaging.md#build-requirements)).
   [Enable Hyper-V](https://learn.microsoft.com/en-us/windows-server/virtualization/hyper-v/get-started/Install-Hyper-V)
   and restart if requested. Clone/open this repo, then open **PowerShell 7 as
   administrator** in its root directory for the remaining host commands.

2. **Guest: install Windows and PowerToys.** In Hyper-V Manager, use
   **New > Virtual Machine** to [create a Generation 2 VM](https://learn.microsoft.com/en-us/windows-server/virtualization/hyper-v/get-started/create-a-virtual-machine-in-hyper-v)
   and install Windows from an ISO. Configure TPM/Secure Boot for Windows 11.
   Sign in once with a local administrator account that has a **nonempty password**.
   [Install the PowerToys version you want to test](https://learn.microsoft.com/en-us/windows/powertoys/install),
   start it, and enable Command Palette. In the guest, run `whoami` and keep the
   returned account name for step 4. Visual Studio and the .NET SDK stay on the host.

3. **Host: create your development certificate once.** This reads the publisher
   from the repo's manifest; no maintainer certificate or manifest edit is needed.
   If you already have a matching certificate, use
   `$certificate = Get-Item 'Cert:\CurrentUser\My\YOUR_THUMBPRINT'` instead.

   ```powershell
   $packageConfig = .\eng\Get-PackageConfig.ps1
   $manifestPath = [IO.Path]::GetFullPath($packageConfig.PackageManifestPath, $packageConfig.RepositoryRoot)
   [xml] $manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8
   $certificateParameters = @{
       Type = 'Custom'
       Subject = [string] $manifest.Package.Identity.Publisher
       FriendlyName = "$($manifest.Package.Identity.Name) development"
       KeyUsage = 'DigitalSignature'
       KeyExportPolicy = 'NonExportable'
       HashAlgorithm = 'SHA256'
       CertStoreLocation = 'Cert:\CurrentUser\My'
       TextExtension = @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')
   }
   $certificate = New-SelfSignedCertificate @certificateParameters
   ```

4. **Host: save your defaults.** Replace `Windows-Test` with the VM's name in
   Hyper-V Manager and `GUESTPC\TestUser` with the account name from `whoami`.
   These commands set the current shell and persist the values for future shells.

   ```powershell
   $env:MSIX_HYPERV_CERTIFICATE_THUMBPRINT = $certificate.Thumbprint
   $env:MSIX_HYPERV_VM_NAMES = 'Windows-Test'
   $env:MSIX_HYPERV_GUEST_USERNAME = 'GUESTPC\TestUser'
   [Environment]::SetEnvironmentVariable('MSIX_HYPERV_CERTIFICATE_THUMBPRINT', $env:MSIX_HYPERV_CERTIFICATE_THUMBPRINT, 'User')
   [Environment]::SetEnvironmentVariable('MSIX_HYPERV_VM_NAMES', $env:MSIX_HYPERV_VM_NAMES, 'User')
   [Environment]::SetEnvironmentVariable('MSIX_HYPERV_GUEST_USERNAME', $env:MSIX_HYPERV_GUEST_USERNAME, 'User')
   ```

5. **Host: check the connection, then deploy.** Enter the guest account's
   password. The connection check avoids rebuilding just to discover a login
   issue. **Use `-TrustCertificate` on the first deployment** so the fresh guest
   trusts the public signing certificate before installation.

   ```powershell
   $credential = Get-Credential -UserName $env:MSIX_HYPERV_GUEST_USERNAME -Message 'Account inside the guest VM'
   $session = New-PSSession -VMName $env:MSIX_HYPERV_VM_NAMES -Credential $credential -ErrorAction Stop
   Remove-PSSession -Session $session
   .\eng\Deploy-HyperVPackage.ps1 -WhatIf -TrustCertificate
   .\eng\Deploy-HyperVPackage.ps1 -Credential $credential -TrustCertificate -Configuration Release -Aot
   ```

6. **Guest: smoke-test the extension.** While signed in as the same guest user,
   open Command Palette (`Win+Alt+Space`) and find Recent Files. Check file
   loading, opening and copying paths, Alt+V view cycling, and Alt+P details
   toggling. Restart Command Palette to check that settings persist. Logs are under
   `artifacts/hyperv/<run-id>` on the host if anything fails.

**For subsequent builds**, open an elevated PowerShell 7 in the repo root and run:

```powershell
.\eng\Deploy-HyperVPackage.ps1 -Configuration Release -Aot
```

The saved defaults are reused and credentials are prompted for. Add
`-TrustCertificate` again after restoring a clean checkpoint without the certificate.
For multiple VMs or checkpoint restores, use the [optional local configuration](#optional-local-configuration).

## Prerequisites

- Run PowerShell 7 as administrator on the local Hyper-V host, with the Hyper-V
  PowerShell module and the [existing packaging prerequisites](WapPackaging.md#build-requirements).
- Install the Windows SDK signing tools (`signtool.exe`).
- Use Windows 10 build 19041 or newer guests with PowerShell Direct enabled
  through the Hyper-V PowerShell Direct Service. Guest networking and WinRM
  are not required. Guest PowerShell 7, Visual Studio, and the .NET SDK are not required.
- Install the PowerToys version you want to test in each guest. Use a local
  administrator account that also runs Command Palette. Installation is per user,
  so supplying a different administrator account will install for that account.
- Enable sideloading of trusted packages in the guest if its policies require it.

## Configure your machines and certificate

With `MSIX_HYPERV_CERTIFICATE_THUMBPRINT` set to your certificate's thumbprint,
you can preview deployment without creating a Hyper-V configuration file:

```powershell
.\eng\Deploy-HyperVPackage.ps1 -VMName 'Windows-Test' -WhatIf
```

Or provide the target names through the environment:

```powershell
$env:MSIX_HYPERV_VM_NAMES = 'Windows-Test;Windows11-Test'
.\eng\Deploy-HyperVPackage.ps1 -WhatIf
```

Use your actual VM names. When no VM entries are configured, only the named
targets are used, with environment or repository defaults for their settings.
`PackageConfigPath` defaults to this checkout's `eng/Package.config.psd1`.

### Optional local configuration

Use a local file for per-VM platforms, usernames, or checkpoint names. Copy
the example into the ignored configuration file:

```powershell
Copy-Item .\eng\HyperV.config.example.psd1 .\eng\HyperV.config.psd1
```

Edit `eng/HyperV.config.psd1` with your VM names and signing certificate thumbprint.
Find your local VM names with `Get-VM | Select-Object Name, State`. Repeat the
example's VM entry for additional machines. Each entry can optionally specify
`Platform` (`x64` or `ARM64`), `UserName`, and `CheckpointName`. Omitted settings
can use the environment defaults described below, then the repository defaults
or credential prompt.
Shared properties are evaluated separately for each selected platform and the
requested configuration. Runtime checks, manifest identity checks, and host
artifact paths use those evaluated values.

The signing certificate must be in the host's Personal (`My`) store, have an
accessible private key, and match the package manifest's `Publisher` exactly.
`CertificateStoreLocation` selects
`CurrentUser` (default) or `LocalMachine`.

### Environment defaults

Use environment variables for defaults shared across your checkouts. Explicit
command-line parameters and nonempty local configuration values take precedence.
Per-VM settings override environment defaults for the platform and username.
Empty or whitespace-only environment values are ignored.

| Environment variable | Setting | Fallback when unset |
|:--|:--|:--|
| `MSIX_HYPERV_CONFIG_PATH` | `-ConfigPath` | Load `eng/HyperV.config.psd1` if present; otherwise use environment and repository defaults |
| `MSIX_HYPERV_CERTIFICATE_THUMBPRINT` | `CertificateThumbprint` | Required in local config |
| `MSIX_HYPERV_CERTIFICATE_STORE_LOCATION` | `CertificateStoreLocation` | `CurrentUser` |
| `MSIX_HYPERV_GUEST_PACKAGE_DIRECTORY` | `GuestPackageDirectory` | `C:\ProgramData\MSIXDeployments` |
| `MSIX_HYPERV_VM_NAMES` | `-VMName`; separate target names with semicolons | All configured VMs, or require target names when no VM entries exist |
| `MSIX_HYPERV_GUEST_USERNAME` | Per-VM `UserName`, pre-filling the credential prompt | Prompt for username |
| `MSIX_HYPERV_PLATFORM` | Per-VM `Platform` | `Package.config.psd1`'s `DefaultPlatform` |
| `MSIX_HYPERV_CONFIGURATION` | `-Configuration` | `Package.config.psd1`'s `DefaultConfiguration` |

An explicitly supplied `-ConfigPath` or `MSIX_HYPERV_CONFIG_PATH` must exist;
a typo never silently selects another file. With `VirtualMachines` entries
configured, the selected names must match those entries. Without entries,
`-VMName` or `MSIX_HYPERV_VM_NAMES` defines the target list.

For example, after creating `$certificate` in the quick start and configuring your VM
entries, leave `CertificateThumbprint` blank and omit per-VM usernames to use
these defaults in the current shell:

```powershell
$env:MSIX_HYPERV_CERTIFICATE_THUMBPRINT = $certificate.Thumbprint
$env:MSIX_HYPERV_GUEST_USERNAME = 'YourGuestAccount'
$env:MSIX_HYPERV_VM_NAMES = 'Windows-Test;Windows11-Test'
.\eng\Deploy-HyperVPackage.ps1 -WhatIf
```

For a [persistent user-level default](https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.core/about/about_environment_variables#saving-changes-to-environment-variables), use
`[Environment]::SetEnvironmentVariable('MSIX_HYPERV_GUEST_USERNAME', 'YourGuestAccount', 'User')`
and open a new terminal. Keep the `.psd1` file as static data; the deployment
script reads environment values after importing it. Credentials still use the
prompt or `-Credential`. `-TrustCertificate` and `-RestoreCheckpoint` remain
explicit switches.

### Create your own development certificate

Use the certificate creation commands in the [quick start](#quick-start-tldr).
They read the publisher from this checkout's configured manifest and create a
certificate in your host user's Personal store without changing the manifest.
Inspect the result with `$certificate | Select-Object Subject, Thumbprint, NotAfter, HasPrivateKey`.

Copy the resulting thumbprint into the optional local Hyper-V configuration or set
`MSIX_HYPERV_CERTIFICATE_THUMBPRINT`, and keep the certificate store location
as `CurrentUser`. If you selected a different `PackageConfigPath`, use that same
file for `$packageConfigPath` in the quick start. The private key
is non-exportable; deployment transfers only its public certificate. The
certificate is for local testing on guests that explicitly trust it. See
[Microsoft's package signing certificate guidance](https://learn.microsoft.com/en-us/windows/msix/package/create-certificate-package-signing).

### Use an existing development certificate

You can instead select an existing matching certificate. If it is a PFX file,
import it once on the host without putting its password on the command line:

```powershell
Import-PfxCertificate -FilePath C:\Certificates\Development.pfx `
    -CertStoreLocation Cert:\CurrentUser\My `
    -Password (Read-Host 'PFX password' -AsSecureString)

Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert |
    Select-Object Subject, Thumbprint, NotAfter, HasPrivateKey
```

Paths in `HyperV.config.psd1` are relative to that file unless absolute.
`PackageConfigPath` selects script options and the sibling `CmdPal.Extension.props`;
package paths are evaluated from that props file. Credentials
are prompted for each selected VM; passwords are never saved in configuration.
You can pass a shared `PSCredential` when all selected guests use the same login.

### Guest authentication and empty passwords

The script passes the supplied `PSCredential` to PowerShell Direct, including
an empty password. Guest logon policy determines whether that authentication
is accepted. Windows' default policy limits local accounts with blank passwords
to console logon, so use a nonempty guest password when authentication fails.
See [Microsoft's blank-password policy reference](https://learn.microsoft.com/en-us/previous-versions/windows/it-pro/windows-10/security/threat-protection/security-policy-settings/accounts-limit-local-account-use-of-blank-passwords-to-console-logon-only).

Inside the guest, run `whoami` to confirm the account name. To set a password
for the local account currently signed in, run this in the guest's terminal:

```powershell
net user "$env:USERNAME" *
```

Then test only the connection from an elevated host PowerShell, using the
guest account and its password. Replace `Windows-Test` with your VM name:

```powershell
$credential = Get-Credential -Message 'Account inside the guest VM'
$session = New-PSSession -VMName 'Windows-Test' -Credential $credential -ErrorAction Stop
Remove-PSSession -Session $session
.\eng\Deploy-HyperVPackage.ps1 -VMName 'Windows-Test' -Credential $credential
```

When a connection using an empty password fails, the deployment report retains
the original transport error and adds the blank-password policy hint. It does
not change the guest account or its logon policy.

## Deploy

Preview the configured actions without building, signing, prompting for
credentials, or contacting VMs:

```powershell
.\eng\Deploy-HyperVPackage.ps1 -WhatIf
```

On the first deployment to a guest, explicitly trust the public certificate:

```powershell
.\eng\Deploy-HyperVPackage.ps1 -TrustCertificate
```

This imports only the selected public certificate into each guest's
`LocalMachine\TrustedPeople` store. The private key stays on the host. Trust
persists until you remove the certificate or restore a checkpoint without it.
Later deployments normally omit `-TrustCertificate`.

```powershell
# Deploy to one configured VM using a prompted shared credential.
.\eng\Deploy-HyperVPackage.ps1 -VMName Windows-Test -Credential (Get-Credential)

# Explicitly select the release-validation build mode.
.\eng\Deploy-HyperVPackage.ps1 -Aot -Configuration Release
```

Release validation must use Release+AOT with trimming, matching CI and Release
packaging. Repeat VM smoke tests after changing from a managed or Debug package:
verify installation/update, Command Palette discovery and activation, recent-file
loading, opening and copying paths, and appearance settings after restarting the host.
Run ARM64 packages on an ARM64 guest or device; cross-building alone does not
validate ARM64 execution. Deployment reports record `Configuration` and `Aot`,
but successful installation alone does not establish successful activation.

For debugging only, `-Configuration Debug -Aot:$false` selects managed output.

VMs that are Off or Saved are started. Paused or transitional states are
rejected. `BootTimeoutSeconds` bounds the wait for the guest heartbeat; the
Hyper-V heartbeat integration service must be enabled. The script then opens
a PowerShell Direct session, copies the signed package and its architecture's
WAP dependency packages, and runs the guest installer in Windows PowerShell.

The installer verifies the copied package hash, guest architecture, and public
certificate. It installs or updates without uninstalling the existing package,
allows repeated development builds with the same version, and rejects a lower
version. Installation can stop processes using the package. A failed update
does not trigger an uninstall or an automatic rollback.

Checkpoint restore is optional. Set `CheckpointName` for each selected VM and
pass the explicit switch only when you want to discard its current state:

```powershell
.\eng\Deploy-HyperVPackage.ps1 -RestoreCheckpoint -TrustCertificate
```

Use `-WhatIf` with the same switches to preview the operation. Without
`-RestoreCheckpoint`, the script never restores a checkpoint. VMs remain
running after deployment. Guest payloads and logs remain in the configured
`GuestPackageDirectory` under a unique run directory.

## Results and manual checks

Host output is retained under `artifacts/hyperv/<run-id>`:

- Each architecture directory contains the signed MSIX, public certificate,
  dependency packages, installer, and deployment metadata.
- `results.json` records success or failure for every selected VM.
- `results/<vm-id>/install.log` and `result.json` contain guest installation
  diagnostics, the guest user and OS version, and the verified package identity.

If `CmdPalArtifactsPath` differs by platform, each platform's payload and guest
logs use its own artifact root with the same run ID. Each root receives the
complete `results.json` summary. The script prints all host output directories.

The script attempts the remaining guests after a guest failure and throws if
any deployment failed. Build or signing failures stop before changing VMs.
Build logs remain under `artifacts/logs`.

Success establishes package installation for the supplied user. Open or refresh
Command Palette in that guest and check discovery, recent-file loading, copying, and
settings manually. The deployment script does not launch UI or run UI tests.

Run the offline regression check with `./eng/Test-HyperVDeployment.ps1`. It
uses temporary fixtures and mocks, and is included in CI without accessing
Hyper-V, the certificate stores, or real AppX registrations.
