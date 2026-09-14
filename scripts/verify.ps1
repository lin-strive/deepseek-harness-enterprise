[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$workspace = Split-Path -Parent $PSScriptRoot
$dotnet = Join-Path $workspace '.tools\dotnet\dotnet.exe'

if (-not (Test-Path -LiteralPath $dotnet)) {
    throw 'Local .NET SDK is missing. Run scripts/bootstrap-dotnet.ps1 first.'
}

& $dotnet restore (Join-Path $workspace 'CompanyHarness.sln')
if ($LASTEXITCODE -ne 0) {
    throw "dotnet restore failed with exit code $LASTEXITCODE."
}
& $dotnet build (Join-Path $workspace 'CompanyHarness.sln') --configuration Debug --no-restore -m:1
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build failed with exit code $LASTEXITCODE."
}
& $dotnet test (Join-Path $workspace 'CompanyHarness.sln') --configuration Debug --no-build
if ($LASTEXITCODE -ne 0) {
    throw "dotnet test failed with exit code $LASTEXITCODE."
}

Write-Host 'Build and tests passed.'
