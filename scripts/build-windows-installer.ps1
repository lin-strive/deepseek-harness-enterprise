[CmdletBinding()]
param(
    [string] $Version,
    [string] $RuntimeDirectory = 'artifacts/windows-harness-runtime',
    [string] $OutputDirectory = 'artifacts/installer',
    [string] $InnoCompiler,
    [switch] $StageOnly
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts'))
$stageRoot = [IO.Path]::GetFullPath((Join-Path $artifactsRoot 'windows-installer-stage'))
$publishRoot = Join-Path $stageRoot 'app'
$runtimeRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot $RuntimeDirectory))
$outputRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot $OutputDirectory))
$projectPath = Join-Path $repoRoot 'src/CompanyHarness.Desktop/CompanyHarness.Desktop.csproj'
$dotnet = Join-Path $repoRoot '.tools/dotnet/dotnet.exe'
$manifestPath = Join-Path $repoRoot 'runtime/runtime-manifest.json'
$moduleResolverPath = Join-Path $repoRoot 'runtime/company-module-resolver.mjs'
$installerScript = Join-Path $repoRoot 'installer/CompanyHarness.iss'
$iconPath = Join-Path $stageRoot 'CompanyHarness.ico'
$bootstrapperPath = Join-Path $stageRoot 'MicrosoftEdgeWebview2Setup.exe'

function Assert-PathInsideArtifacts([string] $PathToCheck) {
    $resolved = [IO.Path]::GetFullPath($PathToCheck)
    $prefix = $artifactsRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $resolved.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Build path must stay inside the repository artifacts directory: $resolved"
    }
}

function New-ApplicationIcon([string] $SourcePng, [string] $DestinationIco) {
    Add-Type -AssemblyName System.Drawing
    $sizes = @(16, 24, 32, 48, 64, 256)
    $images = [Collections.Generic.List[byte[]]]::new()
    $source = [Drawing.Image]::FromFile($SourcePng)
    try {
        foreach ($size in $sizes) {
            $bitmap = [Drawing.Bitmap]::new($size, $size, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
            try {
                $graphics = [Drawing.Graphics]::FromImage($bitmap)
                try {
                    $graphics.Clear([Drawing.Color]::Transparent)
                    $graphics.CompositingMode = [Drawing.Drawing2D.CompositingMode]::SourceCopy
                    $graphics.CompositingQuality = [Drawing.Drawing2D.CompositingQuality]::HighQuality
                    $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                    $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::HighQuality
                    $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
                    $graphics.DrawImage($source, 0, 0, $size, $size)
                }
                finally {
                    $graphics.Dispose()
                }

                $stream = [IO.MemoryStream]::new()
                try {
                    $bitmap.Save($stream, [Drawing.Imaging.ImageFormat]::Png)
                    $images.Add($stream.ToArray())
                }
                finally {
                    $stream.Dispose()
                }
            }
            finally {
                $bitmap.Dispose()
            }
        }
    }
    finally {
        $source.Dispose()
    }

    $file = [IO.File]::Open($DestinationIco, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::None)
    $writer = [IO.BinaryWriter]::new($file)
    try {
        $writer.Write([uint16] 0)
        $writer.Write([uint16] 1)
        $writer.Write([uint16] $sizes.Count)
        $offset = 6 + (16 * $sizes.Count)
        for ($index = 0; $index -lt $sizes.Count; $index++) {
            $size = $sizes[$index]
            $bytes = $images[$index]
            $writer.Write([byte] $(if ($size -eq 256) { 0 } else { $size }))
            $writer.Write([byte] $(if ($size -eq 256) { 0 } else { $size }))
            $writer.Write([byte] 0)
            $writer.Write([byte] 0)
            $writer.Write([uint16] 1)
            $writer.Write([uint16] 32)
            $writer.Write([uint32] $bytes.Length)
            $writer.Write([uint32] $offset)
            $offset += $bytes.Length
        }

        foreach ($bytes in $images) {
            $writer.Write($bytes)
        }
    }
    finally {
        $writer.Dispose()
        $file.Dispose()
    }
}

function Resolve-InnoCompiler([string] $ConfiguredPath) {
    if (-not [string]::IsNullOrWhiteSpace($ConfiguredPath)) {
        $candidate = [IO.Path]::GetFullPath($ConfiguredPath)
        if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
            throw "Inno Setup compiler was not found: $candidate"
        }

        return $candidate
    }

    $candidates = @(
        (Join-Path $repoRoot '.tools/inno-setup-7/ISCC.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs/Inno Setup 7/ISCC.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs/Inno Setup 6/ISCC.exe'),
        (Join-Path ${env:ProgramFiles} 'Inno Setup 7/ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 7/ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6/ISCC.exe')
    )
    return $candidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
}

Assert-PathInsideArtifacts $stageRoot
Assert-PathInsideArtifacts $outputRoot
if (-not (Test-Path -LiteralPath $dotnet -PathType Leaf)) {
    throw 'Local .NET SDK is missing. Run scripts/bootstrap-dotnet.ps1 first.'
}
if (-not (Test-Path -LiteralPath $runtimeRoot -PathType Container)) {
    throw "Harness Runtime is missing: $runtimeRoot. Run scripts/prepare-harness-runtime.ps1 first."
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    [xml] $buildProperties = Get-Content -LiteralPath (Join-Path $repoRoot 'Directory.Build.props') -Raw
    $Version = [string] $buildProperties.Project.PropertyGroup.VersionPrefix
}
if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw 'Version must use the numeric major.minor.patch format, for example 0.1.0.'
}
$compiler = $null
if (-not $StageOnly) {
    $compiler = Resolve-InnoCompiler $InnoCompiler
    if ([string]::IsNullOrWhiteSpace($compiler)) {
        throw 'Inno Setup compiler is missing. Install a company-licensed Inno Setup compiler or pass -InnoCompiler, then rerun this command.'
    }
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$runtimeManifestPath = Join-Path $runtimeRoot 'runtime-manifest.json'
$nodePath = Join-Path $runtimeRoot 'node/node.exe'
$harnessEntryPoint = Join-Path (Join-Path $runtimeRoot 'harness') ($manifest.harness.entryPoint -replace '/', [IO.Path]::DirectorySeparatorChar)
$companyProfile = Join-Path $runtimeRoot 'company.cordis.yml'
foreach ($requiredPath in @($runtimeManifestPath, $nodePath, $harnessEntryPoint, $companyProfile)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Runtime is incomplete; required file is missing: $requiredPath"
    }
}

$reportedHarnessVersion = (& $nodePath $harnessEntryPoint --version).Trim()
if ($reportedHarnessVersion -ne $manifest.harness.version) {
    throw "Harness version mismatch. Expected $($manifest.harness.version), got $reportedHarnessVersion."
}
$profileHash = (Get-FileHash -LiteralPath $companyProfile -Algorithm SHA256).Hash.ToLowerInvariant()
if ($profileHash -ne $manifest.companyProfile.sha256.ToLowerInvariant()) {
    throw "Company profile hash mismatch. Expected $($manifest.companyProfile.sha256), got $profileHash."
}

if (Test-Path -LiteralPath $stageRoot) {
    Assert-PathInsideArtifacts $stageRoot
    Remove-Item -LiteralPath $stageRoot -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $publishRoot, $outputRoot | Out-Null
New-ApplicationIcon (Join-Path $repoRoot 'info/logo-v2-mark.png') $iconPath

& $dotnet publish $projectPath `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --output $publishRoot `
    -p:Version=$Version `
    -p:ApplicationIcon=$iconPath `
    -p:DebugSymbols=false `
    -p:DebugType=None `
    -p:PublishReadyToRun=false `
    -p:PublishSingleFile=false
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$packagedRuntimeDirectoryName = "runtime-$Version"
$packagedRuntimeRoot = Join-Path $publishRoot $packagedRuntimeDirectoryName
New-Item -ItemType Directory -Force -Path $packagedRuntimeRoot | Out-Null
foreach ($runtimeDirectoryName in @('node', 'harness')) {
    Copy-Item `
        -LiteralPath (Join-Path $runtimeRoot $runtimeDirectoryName) `
        -Destination (Join-Path $packagedRuntimeRoot $runtimeDirectoryName) `
        -Recurse
}
foreach ($runtimeFileName in @('company.cordis.yml', 'runtime-manifest.json')) {
    Copy-Item `
        -LiteralPath (Join-Path $runtimeRoot $runtimeFileName) `
        -Destination (Join-Path $packagedRuntimeRoot $runtimeFileName)
}
Copy-Item -LiteralPath $moduleResolverPath -Destination $packagedRuntimeRoot
$nodePtyPrebuilds = Join-Path $packagedRuntimeRoot 'harness/node_modules/node-pty/prebuilds'
foreach ($unsupportedPrebuild in @('darwin-arm64', 'darwin-x64', 'linux-arm64', 'linux-x64', 'win32-arm64')) {
    $unsupportedPrebuildPath = Join-Path $nodePtyPrebuilds $unsupportedPrebuild
    if (Test-Path -LiteralPath $unsupportedPrebuildPath) {
        Assert-PathInsideArtifacts $unsupportedPrebuildPath
        Remove-Item -LiteralPath $unsupportedPrebuildPath -Recurse -Force
    }
}
Get-ChildItem -LiteralPath (Join-Path $nodePtyPrebuilds 'win32-x64') -Filter '*.pdb' -File -ErrorAction SilentlyContinue |
    ForEach-Object {
        Assert-PathInsideArtifacts $_.FullName
        Remove-Item -LiteralPath $_.FullName -Force
    }
$publishedExe = Join-Path $publishRoot 'CompanyHarness.exe'
$webViewLoader = Join-Path $publishRoot 'runtimes/win-x64/native/WebView2Loader.dll'
foreach ($requiredPath in @(
    $publishedExe,
    $webViewLoader,
    (Join-Path $packagedRuntimeRoot 'node/node.exe'),
    (Join-Path $packagedRuntimeRoot 'company-module-resolver.mjs'))) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Published application is incomplete; required file is missing: $requiredPath"
    }
}

$bootstrapperCache = Join-Path $repoRoot '.tools/webview2/MicrosoftEdgeWebview2Setup.exe'
if (-not (Test-Path -LiteralPath $bootstrapperCache -PathType Leaf)) {
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $bootstrapperCache) | Out-Null
    Invoke-WebRequest -UseBasicParsing -Uri 'https://go.microsoft.com/fwlink/p/?LinkId=2124703' -OutFile $bootstrapperCache
}
$bootstrapperSignature = Get-AuthenticodeSignature -LiteralPath $bootstrapperCache
if ($bootstrapperSignature.Status -ne 'Valid' -or
    $bootstrapperSignature.SignerCertificate.Subject -notmatch 'Microsoft Corporation') {
    throw "WebView2 bootstrapper signature is not valid Microsoft code: $($bootstrapperSignature.Status)"
}
Copy-Item -LiteralPath $bootstrapperCache -Destination $bootstrapperPath

$appFiles = Get-ChildItem -LiteralPath $publishRoot -File -Recurse
$runtimeFiles = Get-ChildItem -LiteralPath $packagedRuntimeRoot -File -Recurse
$layoutMetadata = [ordered]@{
    schemaVersion = 1
    productVersion = $Version
    runtimeIdentifier = 'win-x64'
    runtimeDirectory = $packagedRuntimeDirectoryName
    selfContainedDotNet = $true
    harnessVersion = $manifest.harness.version
    nodeVersion = $manifest.node.version
    appFileCount = @($appFiles).Count
    appSizeBytes = [long] ($appFiles | Measure-Object -Property Length -Sum).Sum
    runtimeFileCount = @($runtimeFiles).Count
    runtimeSizeBytes = [long] ($runtimeFiles | Measure-Object -Property Length -Sum).Sum
    webView2BootstrapperSigner = $bootstrapperSignature.SignerCertificate.Subject
    stagedAt = [DateTimeOffset]::UtcNow.ToString('O')
}
$layoutMetadata | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $stageRoot 'package-layout.json') -Encoding utf8

if ($StageOnly) {
    Write-Output "Staged Company Harness $Version at $stageRoot"
    return
}

& $compiler '/Qp' "/DAppVersion=$Version" $installerScript
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup failed with exit code $LASTEXITCODE."
}

$setupPath = Join-Path $outputRoot "CompanyHarness-Setup-$Version-win-x64.exe"
if (-not (Test-Path -LiteralPath $setupPath -PathType Leaf)) {
    throw "Installer compiler completed without the expected output: $setupPath"
}
$releaseMetadata = [ordered]@{
    schemaVersion = 1
    productVersion = $Version
    fileName = Split-Path -Leaf $setupPath
    sizeBytes = [long] (Get-Item -LiteralPath $setupPath).Length
    sha256 = (Get-FileHash -LiteralPath $setupPath -Algorithm SHA256).Hash.ToLowerInvariant()
    signed = (Get-AuthenticodeSignature -LiteralPath $setupPath).Status -eq 'Valid'
    generatedAt = [DateTimeOffset]::UtcNow.ToString('O')
}
$releaseMetadata | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $outputRoot "CompanyHarness-Setup-$Version-win-x64.json") -Encoding utf8
Write-Output "Built installer: $setupPath"
