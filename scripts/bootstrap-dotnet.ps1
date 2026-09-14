[CmdletBinding()]
param(
    [string]$InstallDirectory = (Join-Path $PSScriptRoot '..\.tools\dotnet'),
    [string]$Channel = '8.0'
)

$ErrorActionPreference = 'Stop'
$resolvedInstallDirectory = [System.IO.Path]::GetFullPath($InstallDirectory)
$installer = Join-Path ([System.IO.Path]::GetTempPath()) 'dotnet-install-company-harness.ps1'

Invoke-WebRequest -UseBasicParsing 'https://dot.net/v1/dotnet-install.ps1' -OutFile $installer
& $installer -Channel $Channel -Quality GA -InstallDir $resolvedInstallDirectory -NoPath

if (-not $?) {
    throw 'dotnet-install failed'
}

& (Join-Path $resolvedInstallDirectory 'dotnet.exe') --info
