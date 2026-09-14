[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $RepositoryRoot).Path

$textExtensions = @(
    '.cs', '.csproj', '.editorconfig', '.html', '.iss', '.java', '.js', '.json',
    '.md', '.mjs', '.properties', '.ps1', '.py', '.sh', '.sql', '.ts', '.vue',
    '.xml', '.yaml', '.yml'
)
$textNames = @('.env.example', '.gitignore', 'Dockerfile', 'LICENSE', 'NOTICE')

Push-Location $root
try {
    $candidatePaths = @(& git ls-files --cached --others --exclude-standard 2>$null)
    if ($LASTEXITCODE -ne 0) {
        throw '无法获取 Git 文件列表。请在 Git 工作区中运行此脚本。'
    }

    $files = $candidatePaths |
        Where-Object { $_ -and (Test-Path -LiteralPath $_ -PathType Leaf) } |
        Sort-Object -Unique

    $forbiddenFragments = @(
        @{ Name = '旧公司中文全称'; Value = ('北京' + '华' + '信' + '创银' + '科技有限公司') },
        @{ Name = '旧公司英文标识'; Value = ('Bei' + 'jing' + 'Hua' + 'xin') },
        @{ Name = '旧 Java 包名'; Value = ('com.' + 'hua' + 'xin') },
        @{ Name = '旧公司缩写'; Value = ('H' + 'X-') },
        @{ Name = '真实测试服务器 IP'; Value = ('106.12.' + '166.125') },
        @{ Name = 'root SSH 目标'; Value = ('root' + '@') }
    )

    $failures = [System.Collections.Generic.List[string]]::new()

    foreach ($path in $files) {
        $leaf = Split-Path -Leaf $path
        $extension = [IO.Path]::GetExtension($path).ToLowerInvariant()
        $isText = $textExtensions -contains $extension -or $textNames -contains $leaf

        foreach ($item in $forbiddenFragments) {
            if ($path.IndexOf($item.Value, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
                $failures.Add("$($item.Name)：文件名 $path")
            }
        }

        if (-not $isText) {
            continue
        }

        $content = Get-Content -LiteralPath $path -Raw
        foreach ($item in $forbiddenFragments) {
            if ($content.IndexOf($item.Value, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
                $failures.Add("$($item.Name)：$path")
            }
        }

        if ($content -match '-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----') {
            $failures.Add("私钥内容：$path")
        }
        if ($content -match '(?i)\bsk-[a-z0-9_-]{20,}\b') {
            $failures.Add("疑似 API Key：$path")
        }
    }

    $forbiddenExtensions = @('.exe', '.msi', '.msix', '.pfx', '.p12', '.key', '.dump', '.bak')
    foreach ($path in $files) {
        if ($forbiddenExtensions -contains [IO.Path]::GetExtension($path).ToLowerInvariant()) {
            $failures.Add("不应公开的构建产物或敏感文件：$path")
        }
    }

    if ($failures.Count -gt 0) {
        Write-Error ("公开发布扫描失败：`n- " + (($failures | Sort-Object -Unique) -join "`n- "))
    }

    Write-Host "公开发布扫描通过：已检查 $($files.Count) 个文件。"
}
finally {
    Pop-Location
}
