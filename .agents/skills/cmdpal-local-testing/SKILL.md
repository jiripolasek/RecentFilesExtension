---
name: cmdpal-local-testing
description: Use this repository's eng scripts to build the Recent Files MSIX, deploy it for a local smoke test in PowerToys Command Palette, or run its automated tests.
---

# Local Command Palette testing

Run the `eng/` PowerShell scripts from the repository root in PowerShell 7 on Windows. Build and host defaults live in `eng/Package.config.psd1`, and project paths come from `eng/CmdPal.Extension.props`.

## Try a change in Command Palette

For a live check on a machine with PowerToys installed and Windows Developer Mode enabled, deploy a Debug build.

```powershell
.\eng\Test-Package.ps1 -Configuration Debug
```

On Windows on ARM64, add `-Platform ARM64` for a native deployment. The script builds and registers the unpacked extension for the current Windows user, then requests a Command Palette reload. If the host still shows the old build, add `-AfterDeploy Restart` to stop and relaunch Command Palette.

Open Recent Files and exercise the change. For example, check recent-file loading, opening and copying paths, Alt+V view cycling, and Alt+P details toggling. Report what you observed in the host separately from whether deployment succeeded.

## Other local checks

- **Build an unsigned MSIX** with `.\Build.ps1`. It returns the package file. Use `-Platform ARM64 -Aot` to cross-build the ARM64 Native AOT package on x64.
- **Run the managed tests** with `.\Test.ps1`. It defaults to the Windows host architecture. Run ARM64 tests on Windows on ARM64 with the ARM64 .NET SDK. Add `-BinLog` to either script to save MSBuild logs. The test script also saves TRX results with that switch. [Build and test](../../../docs/dev/README.md#build-and-test)
- **Validate packaging changes** with `.\eng\Test-WapProject.ps1`. The [script checks](../../../docs/dev/README.md#script-checks) show how to validate each configured platform.
