# GitHub Actions reference

[Development guide](README.md)

[`Get-PackageConfig.ps1`](../../eng/Get-PackageConfig.ps1) combines script options
from [`eng/Package.config.psd1`](../../eng/Package.config.psd1) with properties
evaluated by MSBuild from the sibling
[`CmdPal.Extension.props`](../../eng/CmdPal.Extension.props).
[`Get-WorkflowConfig.ps1`](../../eng/Get-WorkflowConfig.ps1) validates those
settings and adds the architecture matrix, manifest identity, and solution tests.
Each matrix entry contains settings evaluated for its own platform.
The packaging project follows the [WAP contract](WapPackaging.md).

## Workflows

| Workflow | Trigger | Output |
|:--|:--|:--|
| [`ci.yml`](../../.github/workflows/ci.yml) | Branch push, pull request, manual dispatch, or reusable workflow call | Unsigned Store upload packages, validation results, and x64 test reports |
| [`tag-release.yml`](../../.github/workflows/tag-release.yml) | Manual dispatch on a branch | Creates a tag from the package manifest version and starts Release; skips existing tags |
| [`release.yml`](../../.github/workflows/release.yml) | `v*` tag push or manual dispatch on an existing release tag | Unsigned packages for manual Store upload and a GitHub draft release with changelog notes |
| [`winget.yml`](../../.github/workflows/winget.yml) | Release `released` event or manual dispatch with `release_tag` | WinGet manifest PR and its URL in the workflow summary |

## Configuration

| Setting | Purpose |
|:--|:--|
| `CmdPalRepositoryRoot` (props) | Absolute base directory for configured paths |
| `CmdPalExtensionProject` (props) | SDK-style application project |
| `CmdPalPackageProject` (props) | Windows Application Packaging project |
| `CmdPalPackageManifest` (props) | Source of package identity, publisher, and version for tagging |
| `CmdPalArtifactsPath` (props) | Package and test output directory |
| `DefaultConfiguration` | Build configuration; currently `Release` |
| `DefaultPlatform` | Default local deployment platform; currently `x64` |
| `CmdPalRuntimeIdentifiers` (props) | Semicolon-separated runtime list; `win-x64` and `win-arm64` produce the build matrix |
| `Workflow.DotNetVersion` | .NET SDK version supplied to `setup-dotnet` |
| `Workflow.WindowsSdkVersion` | Packaging SDK supplied as `CmdPalTargetPlatformVersion` |
| `Workflow.WingetPackageIdentifier` | Package identifier supplied to the WinGet action |

Only script and workflow options live in the PSD1. Shared paths, executable,
and runtimes must be changed in props; duplicate PSD1 entries are rejected.
The reader uses `dotnet msbuild -getProperty` without running targets, restoring,
or importing the WAP project. Conditions, imports, and property functions are
evaluated by MSBuild. Keep the shared contract independently evaluable with
portable paths and imports so metadata can be read on Windows, Linux, and macOS.
Properties defined only in consuming projects are outside this evaluation context.

Workflow settings use `DefaultConfiguration`. The reader evaluates both supported
platforms, x64 and ARM64, and includes a platform when its evaluated runtime list
contains its own runtime identifier. Each entry's `config` contains that platform's
validated paths and manifest identity. Top-level settings use `DefaultPlatform`
for callers such as tagging. Required shared properties must remain nonempty
in each evaluation context.

The reader requires a .NET SDK with MSBuild 17.8 or later. CI, tagging, and WinGet
use [`Get-DotNetVersion.ps1`](../../eng/Get-DotNetVersion.ps1) to validate
`Workflow.DotNetVersion` in the PSD1 and install .NET before
evaluating props. This avoids requiring MSBuild to discover its own SDK version.

Project, manifest, test, and artifact paths must remain inside the repository.
Application, WAP, manifest, and test project files must exist. The repository root
must contain exactly one `.slnx` solution. Projects in that solution whose filenames
end in `Tests.csproj` are discovered automatically and returned as `TestProjects`;
there is no separate list to maintain in the configuration. A solution without
matching projects returns an empty array. Configurations with discovered test
projects must include x64 because the CI test host is x64.

The Windows jobs use `windows-2025-vs2026`; packaging selects MSBuild
18.x. The current configuration selects .NET `11.0.x`, packaging SDK
`10.0.26100.0`, x64, and ARM64. The packaging SDK does not change the manifest's
minimum Windows version.

## CI execution

The `settings` job validates configuration, WAP wiring, and publishing scripts.
Its `config` output contains the architecture matrix and normalized paths.
WAP validation checks every matrix entry with its configuration and platform.
CI build steps and Release packaging reuse the entry's cached JSON settings for
restore, build, tests, versioning, and artifact uploads without reevaluating props.
The `portable-settings` job also reads this repository's configuration and runs
the configuration, tag, and WinGet fixture checks on Linux.

The dependent `build-and-test` job builds and packages each configured
architecture with the same unsigned Store packaging settings as Release and
`SkipKillRunningExecutable=true`. Packages use Release with Native AOT and
trimming. Unit tests run as managed code on x64 and stop at the first
failing project. TRX reports are uploaded with seven-day retention. ARM64 is
built but its tests are not executed on the x64 runner.

After successful builds and tests, each architecture uploads its `.msixupload`
as `ci-store-packages-<commit SHA>-<platform>` with seven-day retention.
Download these packages from the CI run's Artifacts section for manual Store
submission. CI keeps the checked-in manifest version and requires no signing
or Partner Center credentials. It does not submit packages to the Store.
CI artifact names are separate from those used by Release.

## Create a release tag

Open **Actions > Tag release > Run workflow**, select the branch to release,
and run it.
The workflow must first exist on the default branch for the manual
trigger to appear. It reads `Identity.Version` from the manifest configured by
`PackageManifestPath`, currently
[`src/JPSoftworks.RecentFilesExtension.Package/Package.appxmanifest`](../../src/JPSoftworks.RecentFilesExtension.Package/Package.appxmanifest).
A version such as `0.9.0.0` becomes `v0.9.0`. The manifest must use
`MAJOR.MINOR.PATCH.0`, with each component at most 65535.

The tag points to the selected branch's commit captured when the workflow was
started. If the exact tag already exists, the workflow reports that and stops
without changing it or starting Release. Commit a version bump before creating
the next release tag. A tag creation failure also stops the workflow.

After creating a tag, the workflow explicitly dispatches `release.yml` on that
tag. It builds packages for manual Store upload and prepares a draft
GitHub release from the matching `CHANGELOG.md` section. No extra token is
needed: GitHub's built-in `GITHUB_TOKEN` can
[dispatch workflows, but its tag pushes do not trigger another workflow](https://docs.github.com/en/actions/how-tos/write-workflows/choose-when-workflows-run/trigger-a-workflow).
If dispatch fails after the tag was created, run **Actions > Release > Run
workflow** on that tag. Rerunning Tag release
leaves the existing tag alone.

## Release execution

1. `version` validates the tag using `Get-ReleaseVersion.ps1`.
2. `validate` runs the reusable CI workflow.
3. `package` updates the runner's manifest version and builds each architecture.
4. `draft-release` creates a draft after packaging succeeds, using the matching
   `CHANGELOG.md` section read by
   [`Get-ReleaseNotes.ps1`](../../eng/Get-ReleaseNotes.ps1). Missing or empty
   release notes fail the job. An existing release is left unchanged on reruns.

Accepted tag formats are `vMAJOR.MINOR.PATCH` and `vMAJOR.MINOR.PATCH.0`.
Both produce `MAJOR.MINOR.PATCH.0`; each component must be at most 65535.
Branch refs and prerelease suffixes are rejected. Version changes apply only
to the runner's checkout.

Tag pushes and manual runs build packages and prepare the GitHub draft.
Open **Actions > Release > Run workflow** and select an existing release tag
to rebuild its artifacts. Store upload, certification, and publication are
performed manually in Partner Center; the workflows need no Store credentials.

Tags use the workflow stored in their target commit. This behavior applies to
tags pointing to commits containing this change; historical tags retain their
original workflow behavior.

CI and Release set `PublishAot=true` and `PublishTrimmed=true` during both
redirected application restore and WAP packaging. The repository configuration
selects Release. Packaging uses self-contained native output with
`UapAppxPackageBuildMode=StoreUpload`, `AppxBundle=Never`, and
`AppxPackageSigningEnabled=false`.

Managed unit tests do not exercise the native executable. Before release,
repeat the [VM smoke tests](HyperVDeployment.md) with Release+AOT packages.
Earlier managed or Debug VM results do not validate the native release payload.

Each architecture produces an unsigned `.msixupload` Actions artifact with
30-day retention. These artifacts are not attached as release installers.
The subsequent WinGet workflow requires signed `.msix` assets on a published
stable GitHub release; the release workflow does not attach those assets.

## Manual Store upload

1. Open the successful **Release** run and find its **Artifacts** section.
2. Download every `unsigned-store-packages-<tag>-<platform>` artifact and
   extract the `.msixupload` files from the downloaded ZIPs. The current
   configuration produces x64 and ARM64 packages.
3. In Partner Center, open the product's submission and upload those
   `.msixupload` files in its **Packages** section. Use all architectures from
   the same release run.
4. Review the submission and submit it for certification in Partner Center.

The `.msixupload` files are Store submission packages, not signed installers.
The GitHub draft contains changelog notes only. After Store processing, signed
installers can be attached to the draft manually before publishing the GitHub
release and using the separate WinGet workflow.

## WinGet validation contract

`Get-WinGetRelease.ps1` rejects drafts and prereleases. It requires one `.msix`
asset per configured architecture and checks identity, publisher, version,
architecture coverage, and signature presence. Asset matching is case
insensitive; architecture is read from the package rather than its filename.
Signature presence does not verify certificate trust or Store provenance.

The validated identifier, version, tag, and release URL are passed to
`vedantmgoyal9/winget-releaser` at commit
`b7370d4d152efdce803a26cc6ca9eee58cb2ad54`. Its
[composite action](https://github.com/vedantmgoyal9/winget-releaser/blob/b7370d4d152efdce803a26cc6ca9eee58cb2ad54/action.yml)
uses `cargo-bins/cargo-binstall@main` and the latest `komac`, so the wrapper pin
does not lock those transitive dependencies.

## Credentials and concurrency

| Repository setting | Type | Use |
|:--|:--|:--|
| `WINGET_TOKEN` | Secret | WinGet manifest submission credential |

The built-in repository token is read-only except in `draft-release`, which
has `contents: write`, and the Tag release workflow, which has `contents: write`
and `actions: write`. WinGet validation receives a read-only token and a
publishing-token availability flag. Only the submission action receives
`WINGET_TOKEN`. Checkouts disable persisted credentials.

CI runs for the same workflow/ref cancel older runs. Tag creation runs are
serialized across branches; Release runs for the same ref are serialized.
WinGet submissions use a separate serialized group.

## Local validation

Use the [local script checks in the development guide](README.md#script-checks).

The WinGet tests use local package fixtures and mocked network calls.
The configuration test checks changelog extraction, downloadable release
artifacts, and the absence of Store publishing in workflows.
The tagging test reads fixture manifests and mocks GitHub CLI calls for new
tags, release dispatch, existing tags, invalid versions, and API failures.
