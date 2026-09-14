param(
    [string] $OutputDirectory = 'artifacts/windows-harness-runtime'
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$manifestPath = Join-Path $repoRoot 'runtime/runtime-manifest.json'
$manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
$packageTemplatePath = Join-Path $repoRoot 'runtime/harness-package.json'
$lockfilePath = Join-Path $repoRoot 'runtime/harness.pnpm-lock.yaml'
$companyPatchPath = Join-Path $repoRoot $manifest.companyProfile.path
$moduleResolverPath = Join-Path $repoRoot 'runtime/company-module-resolver.mjs'
$artifactsRoot = Join-Path $repoRoot 'artifacts'
$stageRoot = Join-Path $artifactsRoot ".runtime-stage-$([Guid]::NewGuid().ToString('N'))"
$resolvedOutput = [IO.Path]::GetFullPath((Join-Path $repoRoot $OutputDirectory))
$resolvedArtifacts = [IO.Path]::GetFullPath($artifactsRoot) + [IO.Path]::DirectorySeparatorChar
$outputCreated = $false
$succeeded = $false

if (Test-Path -LiteralPath $resolvedOutput) {
    throw "Runtime output already exists: $resolvedOutput"
}
if (-not ($resolvedOutput + [IO.Path]::DirectorySeparatorChar).StartsWith($resolvedArtifacts, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Runtime output must be inside the repository artifacts directory: $resolvedArtifacts"
}

$packageTemplate = Get-Content $packageTemplatePath -Raw | ConvertFrom-Json
$templateVersion = $packageTemplate.dependencies.PSObject.Properties[$manifest.harness.package].Value
if ($templateVersion -ne $manifest.harness.version) {
    throw "Harness package template and runtime manifest versions differ."
}
$lockfile = Get-Content $lockfilePath -Raw
if (-not $lockfile.Contains("'$($manifest.harness.package)@$($manifest.harness.version)':") -or
    -not $lockfile.Contains("integrity: $($manifest.harness.integrity)")) {
    throw "Harness lockfile does not contain the manifest version and integrity."
}
$companyPatchHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $companyPatchPath).Hash.ToLowerInvariant()
if ($companyPatchHash -ne $manifest.companyProfile.sha256.ToLowerInvariant()) {
    throw "Company profile hash mismatch. Expected $($manifest.companyProfile.sha256), got $companyPatchHash."
}

New-Item -ItemType Directory -Force -Path $stageRoot | Out-Null
try {
    $archivePath = Join-Path $stageRoot 'node.zip'
    Invoke-WebRequest -UseBasicParsing -Uri $manifest.node.archiveUrl -OutFile $archivePath
    $actualHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $archivePath).Hash.ToLowerInvariant()
    if ($actualHash -ne $manifest.node.sha256.ToLowerInvariant()) {
        throw "Node archive hash mismatch. Expected $($manifest.node.sha256), got $actualHash."
    }

    $expandedNode = Join-Path $stageRoot 'node-expanded'
    Expand-Archive -LiteralPath $archivePath -DestinationPath $expandedNode
    $nodeSources = @(Get-ChildItem -LiteralPath $expandedNode -Directory)
    if ($nodeSources.Count -ne 1) {
        throw "Expected one Node directory in the archive, found $($nodeSources.Count)."
    }
    $nodeSource = $nodeSources[0]

    # pnpm creates absolute Windows Junction targets. Install directly in the
    # final directory so moving a staging tree cannot invalidate node_modules.
    $runtimeRoot = $resolvedOutput
    $nodeRoot = Join-Path $runtimeRoot 'node'
    $harnessRoot = Join-Path $runtimeRoot 'harness'
    New-Item -ItemType Directory -Force -Path $runtimeRoot, $harnessRoot | Out-Null
    $outputCreated = $true
    Move-Item -LiteralPath $nodeSource.FullName -Destination $nodeRoot
    Copy-Item -LiteralPath $packageTemplatePath -Destination (Join-Path $harnessRoot 'package.json')
    Copy-Item -LiteralPath $lockfilePath -Destination (Join-Path $harnessRoot 'pnpm-lock.yaml')
    Copy-Item -LiteralPath (Join-Path $repoRoot 'runtime/pnpm-workspace.yaml') -Destination $harnessRoot

    $pnpmVersion = (& pnpm --version).Trim()
    if ($pnpmVersion -ne $manifest.builder.pnpmVersion) {
        throw "pnpm $($manifest.builder.pnpmVersion) is required; found $pnpmVersion."
    }

    & pnpm install --dir $harnessRoot --frozen-lockfile
    if ($LASTEXITCODE -ne 0) {
        throw "pnpm failed with exit code $LASTEXITCODE."
    }

    $entryPoint = Join-Path $harnessRoot ($manifest.harness.entryPoint -replace '/', [IO.Path]::DirectorySeparatorChar)
    if (-not (Test-Path -LiteralPath $entryPoint -PathType Leaf)) {
        throw "Harness entry point was not installed: $entryPoint"
    }

    $reportedVersion = (& (Join-Path $nodeRoot 'node.exe') $entryPoint --version).Trim()
    if ($reportedVersion -ne $manifest.harness.version) {
        throw "Harness version mismatch. Expected $($manifest.harness.version), got $reportedVersion."
    }

    Copy-Item -LiteralPath $companyPatchPath -Destination $runtimeRoot
    Copy-Item -LiteralPath $moduleResolverPath -Destination $runtimeRoot
    Copy-Item -LiteralPath $manifestPath -Destination $runtimeRoot
    $succeeded = $true
    Write-Output "Prepared Harness $reportedVersion with Node $($manifest.node.version) at $resolvedOutput"
}
finally {
    $resolvedStage = [IO.Path]::GetFullPath($stageRoot)
    if (($resolvedStage.StartsWith($resolvedArtifacts, [StringComparison]::OrdinalIgnoreCase)) -and
        (Test-Path -LiteralPath $resolvedStage)) {
        Remove-Item -LiteralPath $resolvedStage -Recurse -Force
    }
    if ($outputCreated -and -not $succeeded -and
        ($resolvedOutput + [IO.Path]::DirectorySeparatorChar).StartsWith($resolvedArtifacts, [StringComparison]::OrdinalIgnoreCase) -and
        (Test-Path -LiteralPath $resolvedOutput)) {
        Remove-Item -LiteralPath $resolvedOutput -Recurse -Force
    }
}
