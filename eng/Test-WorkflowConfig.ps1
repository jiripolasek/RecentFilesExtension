<#
.SYNOPSIS
Checks portable workflow configuration, matrices, paths, release versions, and manual Store artifacts.
#>

[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Test-PublishingFixture.ps1')
$testRoot = Join-Path ([IO.Path]::GetTempPath()) "Workflow Settings-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $testRoot | Out-Null
$readConfig = Join-Path $PSScriptRoot 'Get-WorkflowConfig.ps1'
$readVersion = Join-Path $PSScriptRoot 'Get-ReleaseVersion.ps1'
$readNotes = Join-Path $PSScriptRoot 'Get-ReleaseNotes.ps1'
$readSdkVersion = Join-Path $PSScriptRoot 'Get-DotNetVersion.ps1'
$evaluate = Join-Path $PSScriptRoot 'Get-MSBuildEvaluation.ps1'
$validateWap = Join-Path $PSScriptRoot 'Test-WapProject.ps1'
$wapPath = Join-Path $testRoot 'Example.Package.wapproj'

function Assert-Rejected([scriptblock] $Action, [string] $ExpectedMessage) {
    $failure = $null
    try { & $Action | Out-Null }
    catch { $failure = $_.Exception.Message }
    if ($null -eq $failure -or $failure -notlike "*$ExpectedMessage*") {
        throw "Expected '$ExpectedMessage', received '$failure'."
    }
}

try {
    $configPath = New-TestWorkflowRepository -Directory $testRoot
    if ((& $readSdkVersion -ConfigPath $configPath) -ne '9.0.x') { throw 'Bootstrap SDK version was not read.' }
    $optionsText = Get-Content -LiteralPath $configPath -Raw
    foreach ($invalidOptions in @('@{}', '@{ Workflow = @{} }', "@{ Workflow = @{ DotNetVersion = ' ' } }")) {
        Set-Content -LiteralPath $configPath -Value $invalidOptions
        Assert-Rejected { & $readSdkVersion -ConfigPath $configPath } "nonempty 'DotNetVersion'"
        Assert-Rejected { & $readConfig -ConfigPath $configPath } "nonempty 'DotNetVersion'"
    }
    Set-Content -LiteralPath $configPath -Value $optionsText
    $config = & $readConfig -ConfigPath $configPath
    if ($config.AppProjectPath -ne 'Example Extension.csproj' -or $config.ArtifactsPath -ne 'output packages' -or
        $config.DotNetVersion -ne '9.0.x' -or $config.WingetPackageIdentifier -ne 'Contoso.ExampleExtension' -or
        $config.TestProjects.Count -ne 1 -or $config.TestProjects[0] -ne 'Example.Tests.csproj' -or $config.Matrix.include.Count -ne 2) {
        throw 'Repository-specific settings were not preserved.'
    }
    $roundTrip = $config | ConvertTo-Json -Depth 10 -Compress | ConvertFrom-Json
    if ($roundTrip.Matrix.include.Count -ne 2 -or $roundTrip.Architectures.Count -ne 2 -or
        $roundTrip.TestProjects -isnot [array] -or $roundTrip.TestProjects.Count -ne 1) { throw 'Invalid workflow configuration JSON.' }

    $configPath = New-TestWorkflowRepository -Directory $testRoot -Architectures @('x64')
    $config = & $readConfig -ConfigPath $configPath
    if ($config.Matrix.include.Count -ne 1 -or $config.Architectures[0] -ne 'x64') { throw 'Single architecture matrix failed.' }
    $singleJson = $config | ConvertTo-Json -Depth 10 -Compress | ConvertFrom-Json
    if ($singleJson.Matrix.include -isnot [array] -or $singleJson.Matrix.include.Count -ne 1) { throw 'Single architecture matrix was not a JSON array.' }

    $propsPath = Join-Path $testRoot 'CmdPal.Extension.props'
    $propsText = Get-Content -LiteralPath $propsPath -Raw
    $solutionPath = Join-Path $testRoot 'Example.slnx'
    $solutionText = Get-Content -LiteralPath $solutionPath -Raw
    Set-Content -LiteralPath (Join-Path $testRoot 'Unlisted.Tests.csproj') -Value '<Project />'
    Set-Content -LiteralPath (Join-Path $testRoot 'Extra.UnitTests.csproj') -Value '<Project />'
    $solutionText.Replace('</Solution>', '<Project Path="Extra.UnitTests.csproj" /></Solution>') | Set-Content -LiteralPath $solutionPath
    if ((& $readConfig -ConfigPath $configPath).TestProjects -join ',' -ne 'Example.Tests.csproj,Extra.UnitTests.csproj') {
        throw 'Test discovery must use the solution and return sorted test projects only.'
    }
    $solutionText.Replace('<Project Path="Example.Tests.csproj" />', '') | Set-Content -LiteralPath $solutionPath
    $withoutTests = & $readConfig -ConfigPath $configPath | ConvertTo-Json -Depth 10 | ConvertFrom-Json
    if ($withoutTests.TestProjects -isnot [array] -or $withoutTests.TestProjects.Count -ne 0) { throw 'Solutions without tests must return an empty array.' }

    $solutionText.Replace('Example.Tests.csproj', '../outside.Tests.csproj') | Set-Content -LiteralPath $solutionPath
    Assert-Rejected { & $readConfig -ConfigPath $configPath } 'inside the repository'
    $solutionText.Replace('Example.Tests.csproj', 'Missing.Tests.csproj') | Set-Content -LiteralPath $solutionPath
    Assert-Rejected { & $readConfig -ConfigPath $configPath } 'not found'
    Remove-Item -LiteralPath $solutionPath
    Assert-Rejected { & $readConfig -ConfigPath $configPath } 'exactly one .slnx solution'
    Set-Content -LiteralPath $solutionPath -Value $solutionText
    $otherSolutionPath = Join-Path $testRoot 'Other.slnx'
    Set-Content -LiteralPath $otherSolutionPath -Value '<Solution />'
    Assert-Rejected { & $readConfig -ConfigPath $configPath } 'exactly one .slnx solution'
    Remove-Item -LiteralPath $otherSolutionPath

    $propsText.Replace('Example Extension.csproj', '../outside.csproj') | Set-Content -LiteralPath $propsPath
    Assert-Rejected { & $readConfig -ConfigPath $configPath } 'inside the repository'
    $propsText.Replace('Example Extension.csproj', 'missing.csproj') | Set-Content -LiteralPath $propsPath
    Assert-Rejected { & $readConfig -ConfigPath $configPath } 'not found'
    $propsText.Replace('win-x64', 'win-x86') | Set-Content -LiteralPath $propsPath
    Assert-Rejected { & $readConfig -ConfigPath $configPath } 'Unsupported platform'
    $propsText.Replace('win-x64', ' win-x64 ; win-arm64;win-x64; ') | Set-Content -LiteralPath $propsPath
    if ((& $readConfig -ConfigPath $configPath).Matrix.include.Count -ne 2) { throw 'Runtime lists must trim whitespace and ignore duplicate entries.' }
    $propsText.Replace('win-x64', '') | Set-Content -LiteralPath $propsPath
    Assert-Rejected { & $readConfig -ConfigPath $configPath } 'missing or empty'
    Remove-Item -LiteralPath $propsPath
    Assert-Rejected { & $readConfig -ConfigPath $configPath } 'Shared properties were not found'
    Set-Content -LiteralPath $propsPath -Value $propsText

    @'
<Project>
  <PropertyGroup>
    <CmdPalArtifactsPath Condition="'$(Configuration)' == 'Debug' and '$(Platform)' == 'ARM64'">$([MSBuild]::NormalizeDirectory('$(CmdPalRepositoryRoot)', 'debug arm64'))</CmdPalArtifactsPath>
    <CmdPalArtifactsPath Condition="'$(Configuration)' == 'Release'">$([MSBuild]::NormalizeDirectory('$(CmdPalRepositoryRoot)', 'release output', '$(Platform)'))</CmdPalArtifactsPath>
  </PropertyGroup>
  <Target Name="MustNotRun"><Error Text="Configuration reads must not execute targets." /></Target>
  <Target Name="Restore" DependsOnTargets="MustNotRun" />
  <Target Name="Build" DependsOnTargets="MustNotRun" />
</Project>
'@ | Set-Content -LiteralPath (Join-Path $testRoot 'Overrides.props')
    $importedProps = $propsText.Replace('<Project>', '<Project DefaultTargets="MustNotRun">').Replace('</Project>', '<Import Project="Overrides.props" /></Project>')
    Set-Content -LiteralPath $propsPath -Value $importedProps
    if ((& $readConfig -ConfigPath $configPath).ArtifactsPath -ne 'release output/x64') {
        throw 'Shared settings must honor imports, conditions, property functions, and script defaults.'
    }
    $singleProperty = & $evaluate -Path $propsPath -Property CmdPalArtifactsPath -Configuration Debug -Platform ARM64
    if ($singleProperty.Properties.CmdPalArtifactsPath.TrimEnd('\', '/') -ne (Join-Path $testRoot 'debug arm64')) {
        throw 'Single-property evaluation must preserve the requested configuration and platform.'
    }
    $emptyProperty = & $evaluate -Path $propsPath -Property MissingProperty
    if (-not $emptyProperty.Properties.ContainsKey('MissingProperty') -or $emptyProperty.Properties.MissingProperty -cne '') {
        throw 'An undefined single property must be returned as an empty string.'
    }
    $emptyItems = & $evaluate -Path $propsPath -Item MissingItem
    if ($emptyItems.Items.MissingItem -isnot [array] -or $emptyItems.Items.MissingItem.Count -ne 0) {
        throw 'Item-only evaluation must preserve an empty item array.'
    }
    Assert-Rejected { & $evaluate -Path $propsPath } 'at least one MSBuild property or item'
    $importedProps.Replace('<Import Project="Overrides.props" />', '<Import Project="Overrides.props" /><Import Project="Overrides.props" />') |
        Set-Content -LiteralPath $propsPath
    $configWarnings = @()
    $warningConfig = & $readConfig -ConfigPath $configPath -WarningVariable configWarnings -WarningAction SilentlyContinue
    if ($warningConfig.ArtifactsPath -ne 'release output/x64' -or @($configWarnings | Where-Object Message -Like '*MSB4011*').Count -ne 2) {
        throw 'MSBuild evaluation warnings must be visible without corrupting the property JSON.'
    }
    $warningReport = & $validateWap -RepositoryRoot $testRoot -WapProjectPath $wapPath -ExtensionPropsPath $propsPath -OutputFormat Json -WarningAction Stop | ConvertFrom-Json
    if (@($warningReport.Checks | Where-Object { $_.Status -eq 'Warning' -and $_.Check -eq 'MSBuild evaluation' -and $_.Message -like '*MSB4011*' }).Count -ne 1) {
        throw 'The WAP validator must include evaluation warnings inside its JSON report.'
    }
    $debugConfig = & (Join-Path $PSScriptRoot 'Get-PackageConfig.ps1') -ConfigPath $configPath -Configuration Debug -Platform ARM64
    if ([IO.Path]::GetRelativePath($debugConfig.RepositoryRoot, $debugConfig.ArtifactsPath).TrimEnd('\', '/') -ne 'debug arm64') {
        throw 'Property evaluation must receive the requested Configuration and Platform.'
    }
    $importedProps.Replace('Overrides.props', 'Missing.props') | Set-Content -LiteralPath $propsPath
    & {
        $PSNativeCommandUseErrorActionPreference = $true
        Assert-Rejected { & $readConfig -ConfigPath $configPath } 'MSBuild evaluation failed*MSB4019*Missing.props'
    }
    Set-Content -LiteralPath $propsPath -Value $propsText
    $optionsText = Get-Content -LiteralPath $configPath -Raw
    $optionsText.Replace("DefaultPlatform = 'x64'", "DefaultPlatform = 'x64'`n    AppProjectPath = 'duplicate.csproj'") | Set-Content -LiteralPath $configPath
    Assert-Rejected { & $readConfig -ConfigPath $configPath } "Move 'AppProjectPath'"
    $configPath = New-TestWorkflowRepository -Directory $testRoot -Architectures @('ARM64')
    if ((& $readConfig -ConfigPath $configPath -TestProjectsOnly) -ne 'Example.Tests.csproj') {
        throw 'Local test discovery must accept an ARM64-only project.'
    }
    Assert-Rejected { & $readConfig -ConfigPath $configPath } 'requires the x64 runtime'

    $conditionalRoot = Join-Path $testRoot 'conditional settings'
    New-Item -ItemType Directory -Path $conditionalRoot | Out-Null
    $conditionalConfigPath = New-TestWorkflowRepository -Directory $conditionalRoot
    $conditionalPropsPath = Join-Path $conditionalRoot 'CmdPal.Extension.props'
    $conditionalProps = Get-Content -LiteralPath $conditionalPropsPath -Raw
    foreach ($fileName in @('Example Extension.csproj', 'Example.Package.wapproj', 'Package.appxmanifest')) {
        $conditionalProps = $conditionalProps.Replace("$fileName</", "`$(Configuration)/`$(Platform)/$fileName</")
    }
    $conditionalProps.Replace('win-x64;win-arm64', 'win-$(Platform.ToLowerInvariant())').
        Replace("'output packages'", "'output packages', '`$(Configuration)', '`$(Platform)'") |
        Set-Content -LiteralPath $conditionalPropsPath
    (Get-Content -LiteralPath $conditionalConfigPath -Raw).Replace("'Release'", "'Debug'").Replace("'x64'", "'ARM64'") |
        Set-Content -LiteralPath $conditionalConfigPath
    foreach ($configuration in @('Debug', 'Release')) {
        foreach ($platform in @('x64', 'ARM64')) {
            $directory = Join-Path $conditionalRoot "$configuration/$platform"
            New-Item -ItemType Directory -Path $directory -Force | Out-Null
            foreach ($fileName in @('Example Extension.csproj', 'Example.Package.wapproj', 'Package.appxmanifest')) {
                Copy-Item -LiteralPath (Join-Path $conditionalRoot $fileName) -Destination $directory
            }
        }
    }
    $conditionalConfig = & $readConfig -ConfigPath $conditionalConfigPath | ConvertTo-Json -Depth 10 | ConvertFrom-Json
    if ($conditionalConfig.Matrix.include.Count -ne 2 -or $conditionalConfig.ArtifactsPath -ne 'output packages/Debug/ARM64') {
        throw 'Matrix discovery must evaluate each platform, even when the default platform enables only its own runtime.'
    }
    foreach ($entry in $conditionalConfig.Matrix.include) {
        if ($entry.runtime -cne "win-$($entry.platform.ToLowerInvariant())" -or
            $entry.config.DefaultConfiguration -ne 'Debug' -or
            $entry.config.AppProjectPath -ne "Debug/$($entry.platform)/Example Extension.csproj" -or
            $entry.config.WapProjectPath -ne "Debug/$($entry.platform)/Example.Package.wapproj" -or
            $entry.config.PackageManifestPath -ne "Debug/$($entry.platform)/Package.appxmanifest" -or
            $entry.config.ArtifactsPath -ne "output packages/Debug/$($entry.platform)") {
            throw 'Matrix settings were not evaluated and serialized for their own configuration and platform.'
        }
    }
    foreach ($case in @(
        @{ Configuration = 'Debug'; Platform = 'ARM64'; Arguments = @{} },
        @{ Configuration = 'Release'; Platform = 'x64'; Arguments = @{ Configuration = 'Release'; Platform = 'x64' } }
    )) {
        $arguments = $case.Arguments
        $directory = Join-Path $conditionalRoot "$($case.Configuration)/$($case.Platform)"
        $report = & $validateWap -RepositoryRoot $conditionalRoot -WapProjectPath (Join-Path $directory 'Example.Package.wapproj') -ExtensionPropsPath $conditionalPropsPath -OutputFormat Json @arguments | ConvertFrom-Json
        if (@($report.Checks | Where-Object { $_.Check -in @('Application project', 'Package project contract', 'Package manifest') -and $_.Status -eq 'Pass' }).Count -ne 3 -or
            ($report.Checks | Where-Object Check -EQ 'Application project').Message -ne "Resolved '$(Join-Path $directory 'Example Extension.csproj')'.") {
            throw 'WAP validation must use package defaults and honor explicit Configuration and Platform overrides.'
        }
    }

    & {
        $environmentNames = @('WORKFLOW_CONFIG', 'TARGET_PLATFORM', 'TARGET_RUNTIME', 'GITHUB_WORKSPACE', 'RELEASE_VERSION')
        $originalEnvironment = @{}
        foreach ($name in $environmentNames) { $originalEnvironment[$name] = [Environment]::GetEnvironmentVariable($name) }
        $calls = [Collections.Generic.List[object]]::new()
        function dotnet { $calls.Add(@{ Command = 'dotnet'; Arguments = $args }); $global:LASTEXITCODE = 0 }
        function msbuild { $calls.Add(@{ Command = 'msbuild'; Arguments = $args }); $global:LASTEXITCODE = 0 }
        function Get-StepScript([string] $Workflow, [string] $Name) {
            $pattern = '(?ms)^      - name: ' + [regex]::Escape($Name) + '\r?\n(?:(?!^      - ).)*?^        run: \|\r?\n(?<script>(?:^          [^\r\n]*(?:\r?\n|\z)|^\r?\n)+)'
            $match = [regex]::Match($Workflow, $pattern)
            if (-not $match.Success) { throw "Workflow step was not found: '$Name'." }
            [scriptblock]::Create(($match.Groups['script'].Value -replace '(?m)^          ', ''))
        }
        try {
            $env:GITHUB_WORKSPACE = $conditionalRoot
            $env:RELEASE_VERSION = '1.2.3.0'
            foreach ($workflowName in @('ci', 'release')) {
                $workflow = Get-Content -LiteralPath (Join-Path $PSScriptRoot "../.github/workflows/$workflowName.yml") -Raw
                $jobName = if ($workflowName -eq 'ci') { 'build-and-test' } else { 'package' }
                $job = [regex]::Match($workflow, "(?ms)^  ${jobName}:\r?\n(?<job>.*?)(?=^  [\w-]+:|\z)").Groups['job'].Value
                if ($job -match 'Get-WorkflowConfig.ps1' -or
                    -not $job.Contains('WORKFLOW_CONFIG: ${{ toJSON(matrix.config) }}') -or
                    -not $job.Contains('path: ${{ matrix.config.ArtifactsPath }}/*.msixupload') -or
                    ($workflowName -eq 'ci' -and -not $job.Contains('path: ${{ matrix.config.ArtifactsPath }}/test-results/**/*.trx'))) {
                    throw 'Build jobs must reuse matrix settings for every step and artifact upload.'
                }
                foreach ($entry in $conditionalConfig.Matrix.include) {
                    $env:WORKFLOW_CONFIG = $entry.config | ConvertTo-Json -Depth 10 -Compress
                    $env:TARGET_PLATFORM = $entry.platform
                    $env:TARGET_RUNTIME = $entry.runtime
                    $calls.Clear()
                    & (Get-StepScript $workflow 'Restore WAP publish assets')
                    & (Get-StepScript $workflow 'Build unsigned Store upload packages')
                    if ($calls.Count -ne 2 -or $calls[0].Command -ne 'dotnet' -or $calls[1].Command -ne 'msbuild' -or
                        $calls[0].Arguments[1] -ne $entry.config.AppProjectPath -or $calls[0].Arguments -notcontains $entry.runtime -or
                        $calls[0].Arguments -notcontains '-p:Configuration=Debug' -or $calls[0].Arguments -notcontains "-p:Platform=$($entry.platform)" -or
                        $calls[1].Arguments[0] -ne $entry.config.WapProjectPath -or $calls[1].Arguments -notcontains '/p:Configuration=Debug' -or
                        $calls[1].Arguments -notcontains "/p:Platform=$($entry.platform)" -or
                        $calls[1].Arguments -notcontains "/p:AppxPackageDir=$conditionalRoot/$($entry.config.ArtifactsPath)/") {
                        throw 'Restore and packaging steps did not consume the selected matrix configuration.'
                    }
                    if ($workflowName -eq 'ci' -and $entry.platform -eq 'x64') {
                        & (Get-StepScript $workflow 'Test extension')
                        if ($calls.Count -ne 3 -or $calls[2].Arguments[1] -ne 'Example.Tests.csproj' -or
                            $calls[2].Arguments -notcontains 'Debug' -or $calls[2].Arguments -notcontains "$($entry.config.ArtifactsPath)/test-results/0") {
                            throw 'Tests did not consume the cached configuration and artifact path.'
                        }
                    }
                    elseif ($workflowName -eq 'release') {
                        & (Get-StepScript $workflow 'Set package version')
                        [xml] $versionedManifest = Get-Content -LiteralPath (Join-Path $conditionalRoot $entry.config.PackageManifestPath) -Raw
                        if ($versionedManifest.Package.Identity.Version -ne $env:RELEASE_VERSION) {
                            throw 'Release versioning did not update the manifest selected by the matrix.'
                        }
                    }
                }
            }
        }
        finally {
            foreach ($name in $environmentNames) { [Environment]::SetEnvironmentVariable($name, $originalEnvironment[$name]) }
        }
    }

    & {
        $runner = Join-Path $testRoot 'Test.ps1'
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot '../Test.ps1') -Destination $runner
        $fixtureEng = New-Item -ItemType Directory -Path (Join-Path $testRoot 'eng')
        "'First.Tests.csproj', 'Second.Tests.csproj'" |
            Set-Content -LiteralPath (Join-Path $fixtureEng.FullName 'Get-WorkflowConfig.ps1')
        @'
param($Configuration, $Platform)
@{
    DefaultConfiguration = 'Release'
    RuntimeIdentifiers = @{ x64 = 'win-x64'; ARM64 = 'win-arm64' }
    RepositoryRoot = Split-Path -Parent $PSScriptRoot
    ArtifactsPath = "output/$Configuration/$Platform"
}
'@ | Set-Content -LiteralPath (Join-Path $fixtureEng.FullName 'Get-PackageConfig.ps1')
        $calls = [Collections.Generic.List[object]]::new()
        function dotnet { $calls.Add($args); $global:LASTEXITCODE = 23 }

        $hostPlatform = if ([Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq 'Arm64') { 'ARM64' } else { 'x64' }
        & $runner
        if ($LASTEXITCODE -ne 23 -or $calls.Count -ne 1 -or $calls[0][1] -ne (Join-Path $testRoot 'First.Tests.csproj')) {
            throw 'The local test runner must stop on the first failing project and return its exit code.'
        }
        if ($calls[0] -cnotcontains "win-$($hostPlatform.ToLowerInvariant())" -or $calls[0] -cnotcontains "-p:Platform=$hostPlatform") {
            throw 'Tests must default to the native host platform and its runtime.'
        }
        $calls.Clear()
        & $runner -Platform x64 -Configuration Debug -BinLog
        $expectedLogs = Join-Path $testRoot 'output/Debug/x64/logs'
        if ($calls[0] -notcontains 'win-x64' -or $calls[0] -notcontains '-p:Platform=x64' -or
            $calls[0] -notcontains 'Debug' -or $calls[0] -notcontains $expectedLogs -or !(Test-Path -LiteralPath $expectedLogs -PathType Container)) {
            throw 'The requested platform and configuration must select the test runtime and log directory.'
        }
        if ($hostPlatform -eq 'x64') {
            Assert-Rejected { & $runner -Platform ARM64 } 'ARM64 tests require Windows on ARM64'
        }
    }

    foreach ($property in @('CmdPalExtensionProject', 'CmdPalPackageProject', 'CmdPalPackageManifest')) {
        [xml] $emptyProps = $propsText
        $emptyProps.SelectSingleNode("/Project/PropertyGroup/$property").InnerText = ''
        $emptyProps.Save($propsPath)
        $summary = & $validateWap -RepositoryRoot $testRoot -WapProjectPath $wapPath -ExtensionPropsPath $propsPath -OutputFormat Json | ConvertFrom-Json
        if ($LASTEXITCODE -ne 1 -or $summary.Status -ne 'Fail' -or
            @($summary.Checks | Where-Object { $_.Status -eq 'Fail' -and $_.Message -eq "Required property '$property' is missing or empty." }).Count -ne 1) {
            throw "An empty $property must produce a structured validation failure."
        }
    }
    foreach ($invalidProps in @('<Project><Import Project="Missing.props" /></Project>', '<Project>')) {
        Set-Content -LiteralPath $propsPath -Value $invalidProps
        foreach ($format in @('Json', 'Text')) {
            $report = & $validateWap -RepositoryRoot $testRoot -WapProjectPath $wapPath -ExtensionPropsPath $propsPath -OutputFormat $format
            if ($LASTEXITCODE -ne 1) { throw 'Invalid shared properties must fail validation.' }
            if ($format -eq 'Json') {
                $summary = $report | ConvertFrom-Json
                if ($summary.FailureCount -ne 1 -or $summary.Checks[0].Check -ne 'Shared properties' -or
                    $summary.Checks[0].Message -notlike '*MSBuild evaluation failed*MSB*') {
                    throw 'Evaluation failure must report its MSBuild diagnostic and skip dependent checks.'
                }
            }
            elseif (($report -join "`n") -notlike '*[[]FAIL[]] Shared properties:*MSBuild evaluation failed*') {
                throw 'Text validation must report evaluation failures.'
            }
        }
    }

    $quotedRoot = Join-Path $testRoot "O'Brien's extension"
    $propsDirectory = Join-Path $quotedRoot 'build eng'
    New-Item -ItemType Directory -Path $propsDirectory -Force | Out-Null
    $generatedPropsPath = Join-Path $propsDirectory 'CmdPal.Extension.props'
    $propsTemplate = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'templates/wap/CmdPal.Extension.props.template') -Raw
    $propsTemplate.Replace('__REPOSITORY_ROOT_RELATIVE_PATH__', '..').Replace('__ARTIFACTS_RELATIVE_PATH__', 'Jiri%27s packages') |
        Set-Content -LiteralPath $generatedPropsPath
    $quotedPaths = (& $evaluate -Path $generatedPropsPath -Property CmdPalRepositoryRoot,CmdPalArtifactsPath).Properties
    if ($quotedPaths.CmdPalRepositoryRoot.TrimEnd('\', '/') -ne $quotedRoot -or
        $quotedPaths.CmdPalArtifactsPath.TrimEnd('\', '/') -ne (Join-Path $quotedRoot "Jiri's packages")) {
        throw 'Generated repository and artifact paths must preserve apostrophes.'
    }

    foreach ($tag in @('v0.4.0', 'v0.4.0.0')) {
        if ((& $readVersion -Tag $tag).ToString() -ne '0.4.0.0') { throw 'Release version normalization failed.' }
    }
    foreach ($tag in @('master', 'v0.4', 'v0.4.0.1', 'v0.4.0-beta')) {
        Assert-Rejected { & $readVersion -Tag $tag } 'Select a vMAJOR'
    }
    foreach ($tag in @('v65536.0.0', 'v0.65536.0', 'v0.0.65536')) {
        Assert-Rejected { & $readVersion -Tag $tag } 'between 0 and 65535'
    }

    $changelogPath = Join-Path $testRoot 'CHANGELOG.md'
    $changelog = "# Changelog`n`n## [Unreleased]`n`nFuture notes.`n`n## [1.2.3] - 2026-11-01`n`nCurrent notes.`n`n### Fixed`n`n- A fix.`n`n## [1.2.2] - 2026-10-01`n`nOlder notes.`n"
    foreach ($newLine in @("`n", "`r`n")) {
        Set-Content -LiteralPath $changelogPath -Value $changelog.Replace("`n", $newLine)
        foreach ($tag in @('v1.2.3', 'v1.2.3.0')) {
            $notes = & $readNotes -Tag $tag -ChangelogPath $changelogPath
            if ($notes -cne "Current notes.$newLine$newLine### Fixed$newLine$newLine- A fix.") { throw 'Release notes included the wrong changelog section.' }
        }
        if ((& $readNotes -Tag v1.2.2 -ChangelogPath $changelogPath) -cne 'Older notes.') { throw 'The final changelog section was not read.' }
    }
    Assert-Rejected { & $readNotes -Tag v9.9.9 -ChangelogPath $changelogPath } 'No release notes found'
    Set-Content -LiteralPath $changelogPath -Value "## [1.2.3]`n`n## [1.2.2]`nOlder notes."
    Assert-Rejected { & $readNotes -Tag v1.2.3 -ChangelogPath $changelogPath } 'No release notes found'

    $releaseWorkflow = Get-Content -LiteralPath (Join-Path $PSScriptRoot '../.github/workflows/release.yml') -Raw
    $packageJob = [regex]::Match($releaseWorkflow, '(?ms)^  package:\r?\n(?<job>.*?)(?=^  [\w-]+:|\z)').Groups['job'].Value
    if ($packageJob -notmatch 'uses: actions/upload-artifact@' -or $packageJob -notmatch 'path: [^\r\n]*\*\.msixupload') {
        throw 'Release must retain downloadable Store upload packages as GitHub artifacts.'
    }
    foreach ($workflow in Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot '../.github/workflows') -Filter '*.yml' -File) {
        if ((Get-Content -LiteralPath $workflow.FullName -Raw) -match 'PARTNER_CENTER_|(?:upload|submit)_to_store|Submit-StorePackages|manage\.devcenter\.microsoft\.com') {
            throw "Store publishing must remain manual: $($workflow.Name)."
        }
    }
    Write-Output 'Evaluated properties, imports, conditions, configuration overrides, solution test discovery, matrix JSON, release versions, changelog notes, and manual Store artifact checks passed.'
}
finally {
    $temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    $resolvedTestRoot = [IO.Path]::GetFullPath($testRoot)
    if (-not $resolvedTestRoot.StartsWith($temporaryRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove test data outside '$temporaryRoot'."
    }
    Remove-Item -LiteralPath $resolvedTestRoot -Recurse -Force
}
