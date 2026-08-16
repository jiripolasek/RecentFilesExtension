# Standalone WAP publishing

Use a Windows Application Packaging Project (WAP) when a Command Palette
extension must publish as MSIX while its C# application remains independently
buildable. The WAP is the packaged startup/deploy project; the application is
an ordinary SDK-style executable.

## Repository shape

```text
Directory.Build.props
Directory.Packages.props
eng/
  CmdPal.Extension.props
  Initialize-WapProject.ps1
  Test-WapProject.ps1
  Deploy-Package.ps1
  Test-Package.ps1
  Uninstall-Package.ps1
  Package.config.psd1
  templates/wap/
src/
  <Extension>/
    <Extension>.csproj
    Assets/                         # files consumed at runtime only
    Properties/PublishProfiles/
  <Extension>.Package/
    <Extension>.Package.wapproj
    Package.appxmanifest
    Assets/                         # manifest/package assets
    Strings/                        # package PRI resources, if any
```

Keep reusable automation in `eng/`. Keep the checked-in WAP template aligned
with the real WAP: make structural WAP changes in
`eng/templates/wap/Package.wapproj.template`, bump
`CmdPalPackagingTemplateVersion`, and apply the same change to existing WAPs.

## Prerequisites

- Visual Studio with MSBuild and DesktopBridge/MSIX Packaging Tools.
- A Windows SDK containing `makeappx.exe`.
- `Microsoft.Windows.SDK.BuildTools` in `Directory.Packages.props`.
- Self-contained x64 and ARM64 publish profiles.

`WindowsSdkPackageVersion` selects the app's Windows SDK projection; it is not
a `Microsoft.Windows.SDK.BuildTools` package version.

Before migrating, record the current package identity, publisher, version,
provider CLSID, COM executable/arguments, supported interfaces, capabilities,
manifest assets, Store association, target framework, minimum Windows version,
RIDs, and proven Native AOT behavior. A packaging migration must not silently
change those values.

## Migrate an existing single-project package

Preview first:

```powershell
.\eng\Initialize-WapProject.ps1 `
  -AppProjectPath .\src\ExampleExtension\ExampleExtension.csproj `
  -WhatIf
```

Run it after reviewing the paths. Add `-SupportsNativeAot` only when Native AOT
already works for the extension.

```powershell
.\eng\Initialize-WapProject.ps1 `
  -AppProjectPath .\src\ExampleExtension\ExampleExtension.csproj
```

The script creates the shared property contract and WAP, copies the manifest,
copies manifest-referenced assets and PRI strings, preserves an optional Store
association, and changes the explicit COM executable to:

```text
<AssemblyName>\<AssemblyName>.exe
```

Review the generated manifest before deleting the old one. Identity, publisher,
version, CLSIDs, arguments, interfaces, capabilities, and localization must be
unchanged.

## Wire the projects

Import the generated contract from `Directory.Build.props`, and guard C#-only
settings so they do not configure the WAP:

```xml
<Import
  Project="$(MSBuildThisFileDirectory)eng\CmdPal.Extension.props"
  Condition="'$(CmdPalExtensionProject)' == '' and Exists('$(MSBuildThisFileDirectory)eng\CmdPal.Extension.props')" />

<PropertyGroup Condition="'$(MSBuildProjectExtension)' == '.csproj'">
  <TargetFramework>$(CmdPalTargetFramework)</TargetFramework>
  <TargetPlatformMinVersion>$(CmdPalTargetPlatformMinVersion)</TargetPlatformMinVersion>
  <SupportedOSPlatformVersion>$(CmdPalTargetPlatformMinVersion)</SupportedOSPlatformVersion>
  <Nullable>enable</Nullable>
  <ImplicitUsings>enable</ImplicitUsings>
</PropertyGroup>
```

In the application project:

- map `AssemblyName`, `RuntimeIdentifiers`, and `PackageManifestPath` to the
  `CmdPal*` contract;
- keep `app.manifest` and the publish profiles;
- remove `AppxManifest`, `EnableMsixTooling`, the `Msix` project capability,
  `HasPackageAndPublishMenu`, signing, bundle, and other package-generation
  settings;
- default `PublishAot` and `PublishTrimmed` to `true` for Release in both the
  application and WAP, with `false` fallbacks for Debug and explicit overrides;
- repeat the Release settings in each RID publish profile because DesktopBridge
  evaluates the referenced application before importing that profile;
- keep `PublishSingleFile=false` because WAP publishing does not support it.

Package assets belong beside `Package.appxmanifest`. Runtime assets remain in
the application and copy to build and publish output:

```xml
<None Update="Assets\**\*">
  <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  <CopyToPublishDirectory>PreserveNewest</CopyToPublishDirectory>
</None>
```

Duplicate an asset across the projects only when it has two verified consumers,
for example a manifest logo also loaded by application code. Remove unused
source artwork before adopting the wildcard.

Add the WAP to `.slnx` and move deploy ownership from the app to the WAP:

```xml
<Project
  Path="src/ExampleExtension.Package/ExampleExtension.Package.wapproj"
  Type="c7167f0d-bc9f-4e6e-afe1-012c56b48db5">
  <Platform Solution="*|ARM64" Project="ARM64" />
  <Platform Solution="*|x64" Project="x64" />
  <Deploy />
</Project>
```

Finally, update `eng/Package.config.psd1` with the app project, WAP, manifest,
artifacts directory, packaged executable, and RID mappings.

## Set up a new extension

Create the application and provider class first, then generate a manifest and
WAP together:

```powershell
.\eng\Initialize-WapProject.ps1 `
  -AppProjectPath .\src\ExampleExtension\ExampleExtension.csproj `
  -CreateManifest `
  -PackageIdentityName Contoso.ExampleForCommandPalette `
  -Publisher 'CN=00000000-0000-0000-0000-000000000000' `
  -PublisherDisplayName Contoso `
  -DisplayName 'Example for Command Palette' `
  -ProviderClassId 00000000-0000-0000-0000-000000000001
```

Replace every sample identity/GUID, add all manifest assets, add only required
capabilities, and configure signing through the Visual Studio publishing wizard
or CI. Do not commit certificate thumbprints or private keys to the reusable WAP.

## Validate and publish

Run static validation before building:

```powershell
.\eng\Test-WapProject.ps1 -OutputFormat Json
```

Then build the application independently and exercise the package workflow:

```powershell
dotnet build .\src\ExampleExtension\ExampleExtension.csproj -c Release -p:Platform=x64
.\eng\Deploy-Package.ps1 -Configuration Release -Platform x64
.\eng\Test-Package.ps1 -Configuration Release -Platform x64
```

Repeat for ARM64. Use `-Aot` only when `CmdPalSupportsNativeAot` is true.

For distributable packages, select the WAP and use Visual Studio's **Publish >
Create App Packages** wizard with the Release configuration. Release WAP builds
and both RID publish profiles enable Native AOT and trimming by default, for both
of the usual outputs:

- choose sideloading and a self-signed certificate for internal VM testing;
  export only its public `.cer` and install it under **Local Machine > Trusted
  People** on each VM before installing the MSIX;
- choose Microsoft Store upload for the final `.msixupload` submission.

Signing and optimization are independent: the wizard chooses the signing and
distribution path, while the checked-in Release settings choose AOT and
trimming. `Deploy-Package.ps1` is a local unpack-and-register workflow, not a
release artifact publisher; it deliberately stays managed and untrimmed unless
`-Aot` is passed. Keep the certificate private key only on authorized publishing
machines or in the CI secret store.

Before release, inspect the unpacked MSIX and confirm:

- package identity and capabilities match the source manifest;
- application and COM server executables match;
- the configured executable exists in the payload;
- runtime assets and PRI resources are present;
- managed output contains the managed runtime; Native AOT output does not
  contain `coreclr.dll`, `hostfxr.dll`, or the application `.deps.json`.

## Updating an existing standalone WAP

1. Change package identity, capabilities, localization, and package assets only
   in the package project.
2. Change shared framework, platform, RID, executable, and artifacts values in
   `eng/CmdPal.Extension.props`.
3. Change reusable WAP behavior in the template first, bump the template
   version, and mirror it into existing WAPs.
4. Re-run `Test-WapProject.ps1`, both platform builds, payload inspection, and
   activation through Command Palette.

Common failure signals: `NETSDK1004` under `obj\wappublish` means the redirected
RID-specific restore is missing; an installed but undiscovered extension usually
means the app-extension name, CLSIDs, supported interfaces, or executable paths
do not agree; missing files usually indicate package/runtime asset ownership was
assigned to the wrong project.
