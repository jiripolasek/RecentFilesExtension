<#
.SYNOPSIS
Checks generated WAP paths and manifests on Windows without building or deploying.
#>

[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) "O'Brien's WAP-$([guid]::NewGuid().ToString('N'))"

try {
    $caseIndex = 0
    foreach ($name in @('Plain', "O'Brien's", '100%25', '$(Foo)', '@(Foo)', 'one;two', 'Jiří & (tools)')) {
        $repositoryRoot = Join-Path $testRoot $caseIndex
        $appDirectory = Join-Path $repositoryRoot "src/$name"
        New-Item -ItemType Directory -Path $appDirectory -Force | Out-Null
        $appPath = Join-Path $appDirectory 'Example.csproj'
        Set-Content -LiteralPath $appPath -Value '<Project><PropertyGroup><TargetFramework>net11.0-windows10.0.22621.0</TargetFramework><TargetPlatformMinVersion>10.0.19041.0</TargetPlatformMinVersion></PropertyGroup></Project>'
        $artifactsPath = "output/$name"
        $generated = & (Join-Path $PSScriptRoot 'Initialize-WapProject.ps1') -RepositoryRoot $repositoryRoot -AppProjectPath $appPath `
            -ExtensionPropsPath (Join-Path $repositoryRoot "eng/$name/CmdPal.Extension.props") -ArtifactsPath $artifactsPath `
            -CreateManifest -PackageIdentityName Contoso.Example -Publisher 'CN=Example' -PublisherDisplayName $name `
            -DisplayName $name -ProviderClassId 00000000-0000-0000-0000-000000000001 -WindowsSdkBuildToolsVersion 10.0.22621.2428 -WarningAction SilentlyContinue

        [xml] $wap = Get-Content -LiteralPath $generated.PackageProjectPath -Raw
        $sharedImport = $wap.SelectSingleNode("//*[local-name()='Import' and contains(@Project, 'CmdPal.Extension.props')]")
        $referenceGroup = $wap.SelectSingleNode("//*[local-name()='ItemGroup'][*[local-name()='ProjectReference']]")
        # Evaluate the generated import and references without loading Windows packaging targets.
        $probePath = Join-Path (Split-Path -Parent $generated.PackageProjectPath) 'Evaluate.proj'
        foreach ($node in @($wap.DocumentElement.ChildNodes)) {
            if ($node -ne $sharedImport -and $node -ne $referenceGroup) { [void] $wap.DocumentElement.RemoveChild($node) }
        }
        $wap.Save($probePath)
        $expectedPaths = @{
            CmdPalRepositoryRoot = $repositoryRoot
            CmdPalExtensionProject = $appPath
            CmdPalPackageProject = $generated.PackageProjectPath
            CmdPalPackageManifest = $generated.PackageManifestPath
            CmdPalArtifactsPath = Join-Path $repositoryRoot $artifactsPath
        }
        $propertyNames = @($expectedPaths.Keys) + 'CmdPalPackageExecutable'
        $evaluation = & (Join-Path $PSScriptRoot 'Get-MSBuildEvaluation.ps1') -Path $probePath -Property $propertyNames -Item ProjectReference
        foreach ($property in $expectedPaths.Keys) {
            $actualPath = [IO.Path]::GetFullPath($evaluation.Properties[$property]).TrimEnd('\', '/')
            if ($actualPath -cne $expectedPaths[$property]) {
                throw "Generated $property for '$name' resolved to '$actualPath', expected '$($expectedPaths[$property])'."
            }
        }
        $references = @($evaluation.Items.ProjectReference)
        if ($references.Count -ne 1 -or $references[0].FullPath -cne $appPath) {
            throw "Generated WAP must reference exactly the original application project for '$name'."
        }
        [xml] $manifest = Get-Content -LiteralPath $generated.PackageManifestPath -Raw
        $executable = $manifest.SelectSingleNode("//*[local-name()='ExeServer']").GetAttribute('Executable')
        if ($evaluation.Properties.CmdPalPackageExecutable -cne 'Example/Example.exe' -or $executable -cne 'Example\Example.exe' -or
            $manifest.Package.Properties.DisplayName -cne $name -or $manifest.Package.Properties.PublisherDisplayName -cne $name) {
            throw "Generated manifest values must remain literal XML values for '$name'."
        }
        $caseIndex++
    }
    Write-Output "Passed $caseIndex WAP initializer cases covering generated paths, imports, project references, and manifest values."
}
finally {
    $temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    $resolvedTestRoot = [IO.Path]::GetFullPath($testRoot)
    if (-not $resolvedTestRoot.StartsWith($temporaryRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove test data outside '$temporaryRoot'."
    }
    if (Test-Path -LiteralPath $resolvedTestRoot) { Remove-Item -LiteralPath $resolvedTestRoot -Recurse -Force }
}
