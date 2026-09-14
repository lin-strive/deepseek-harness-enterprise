[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$brandPath = Join-Path $repoRoot 'branding/brand-template.json'
$brand = Get-Content -LiteralPath $brandPath -Raw | ConvertFrom-Json
$failures = [Collections.Generic.List[string]]::new()

function Assert-FileContains {
    param([string]$RelativePath, [string]$Expected)

    $fullPath = Join-Path $repoRoot $RelativePath
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        $failures.Add("Missing file: $RelativePath")
        return
    }

    $content = Get-Content -LiteralPath $fullPath -Raw
    if (-not $content.Contains($Expected)) {
        $failures.Add("$RelativePath does not contain expected brand value: $Expected")
    }
}

Assert-FileContains 'Directory.Build.props' $brand.companyNameZh
Assert-FileContains 'Directory.Build.props' $brand.productNameZh
Assert-FileContains 'installer/CompanyHarness.iss' $brand.installerAppId
Assert-FileContains 'installer/CompanyHarness.iss' $brand.windowsInstallDirectory
Assert-FileContains 'src/CompanyHarness.Desktop/App.xaml.cs' $brand.technicalBrandId
Assert-FileContains 'src/CompanyHarness.Desktop/MainWindow.xaml' $brand.companyNameZh
Assert-FileContains 'admin-web/src/App.vue' $brand.companyNameZh
Assert-FileContains 'services/control-plane/pom.xml' ($brand.javaBasePackage -replace '\.harness$', '')
Assert-FileContains 'services/control-plane/src/main/java/com/smartwheelchair/harness/admin/AdminService.java' ($brand.activationCodePrefix + '-')

$legacyPackageSegment = 'hua' + 'xin'
$legacyJavaDirectory = Join-Path $repoRoot ("services/control-plane/src/main/java/com/$legacyPackageSegment")
if (Test-Path -LiteralPath $legacyJavaDirectory) {
    $failures.Add("Legacy Java package directory still exists: services/control-plane/src/main/java/com/$legacyPackageSegment")
}

$bannedPatterns = @(
    ('北京' + '华' + '信'),
    ('华' + '信'),
    ('Beijing' + 'Hua' + 'xin'),
    ('com.' + 'hua' + 'xin'),
    ('/opt/ds-harness-' + 'h' + 'x'),
    ('"H' + 'X-')
)
$excluded = @(
    'runtime/harness.pnpm-lock.yaml',
    'admin-web/package-lock.json',
    'scripts/verify-brand-template.ps1'
)
$textExtensions = @('.md','.cs','.xaml','.csproj','.props','.iss','.java','.xml','.yaml','.yml','.json','.js','.ts','.vue','.ps1','.sh','.env','.example','.html')
foreach ($relativePath in (git -C $repoRoot ls-files -co --exclude-standard)) {
    if ($excluded -contains $relativePath) { continue }
    if ($textExtensions -notcontains [IO.Path]::GetExtension($relativePath)) { continue }

    $candidatePath = Join-Path $repoRoot $relativePath
    if (-not (Test-Path -LiteralPath $candidatePath -PathType Leaf)) { continue }
    $content = Get-Content -LiteralPath $candidatePath -Raw
    foreach ($pattern in $bannedPatterns) {
        if ($content.Contains($pattern)) {
            $failures.Add("Legacy brand token '$pattern' found in $relativePath")
        }
    }
}

Add-Type -AssemblyName System.Drawing
$expectedImages = @{
    'info/logo-v2-mark.png' = @(1024, 1024)
    'info/logo-v2-horizontal.png' = @(2172, 724)
    'admin-web/public/favicon.png' = @(1024, 1024)
    'admin-web/public/logo.png' = @(2172, 724)
}
foreach ($entry in $expectedImages.GetEnumerator()) {
    $path = Join-Path $repoRoot $entry.Key
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        $failures.Add("Missing brand image: $($entry.Key)")
        continue
    }

    $image = [System.Drawing.Bitmap]::FromFile($path)
    try {
        if ($image.Width -ne $entry.Value[0] -or $image.Height -ne $entry.Value[1]) {
            $failures.Add("Unexpected dimensions for $($entry.Key): $($image.Width)x$($image.Height)")
        }
        if (($image.GetPixel(0, 0).A) -ne 0) {
            $failures.Add("Brand image must preserve transparent padding: $($entry.Key)")
        }
    }
    finally {
        $image.Dispose()
    }
}

if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Error $_ }
    throw "Brand template verification failed with $($failures.Count) issue(s)."
}

Write-Output "Brand template verified: $($brand.companyNameZh) / $($brand.productNameZh)"
