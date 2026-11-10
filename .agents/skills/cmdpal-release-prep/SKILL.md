---
name: cmdpal-release-prep
description: Check Recent Files release readiness before tagging, including the manifest version, changelog, x64 and ARM64 packages, and artifacts for Partner Center and WinGet.
---

# Check release readiness

Run from the repository root in PowerShell 7 on Windows. Record the candidate commit and any uncommitted changes before using results from a hosted build.

## Version and release notes

Derive the proposed tag from the configured manifest and read its section of `CHANGELOG.md`.

```powershell
$config = .\eng\Get-WorkflowConfig.ps1
[xml] $manifest = Get-Content -LiteralPath $config.PackageManifestPath -Raw
$packageVersion = [string] $manifest.Package.Identity.Version
if ($packageVersion -notmatch '^[0-9]+\.[0-9]+\.[0-9]+\.0$') { throw 'Expected MAJOR.MINOR.PATCH.0 in the manifest.' }
$version = .\eng\Get-ReleaseVersion.ps1 -Tag "v$packageVersion"
$tag = "v$($version.ToString(3))"
.\eng\Get-ReleaseNotes.ps1 -Tag $tag
```

For a new release, check the exact remote tag with `git ls-remote --refs --tags origin "refs/tags/$tag"`. If the tag exists, **Tag release** stops before dispatching **Release**. If the lookup fails, report tag availability as unknown.

## Build and smoke checks

- Run the [managed tests](../../../docs/dev/README.md#build-and-test) on each available x64 and ARM64 host. Record which architectures ran. Run [WAP validation](../../../docs/dev/README.md#script-checks) for each entry in `$config.Matrix.include` using its project path, configuration, and platform.
- Verify that a local build or CI run for the candidate produced a trimmed Release Native AOT `.msixupload` for each configured architecture. Record static validation and completed package builds separately.
- For a clean-machine check, preview the configured targets with `.\eng\Deploy-HyperVPackage.ps1 -Configuration Release -Aot -WhatIf`. For a requested VM deployment, verify installation in the guest report, then exercise discovery, file loading, and copying in Command Palette on the target architecture. [Hyper-V smoke tests](../../../docs/dev/HyperVDeployment.md#results-and-manual-checks)

## Release artifacts

- **Partner Center** takes the unsigned `.msixupload` files from the `unsigned-store-packages-<tag>-<platform>` artifacts of the same Release run. The release owner uploads those files manually. [Store upload guide](../../../docs/dev/GitHubActions.md#manual-store-upload)
- **WinGet** requires a published stable GitHub release with one Store-signed `.msix` installer per configured architecture. The GitHub draft starts with changelog notes only. [WinGet asset checks](../../../docs/dev/GitHubActions.md#winget-validation-contract)

Finish with the candidate tag, checks that passed or failed, and anything still unverified. Creating tags or pull requests, pushing, dispatching workflows, and publishing require the user's explicit instruction.
