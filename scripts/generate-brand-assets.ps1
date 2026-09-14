[CmdletBinding()]
param(
    [string]$CompanyName = '超智能战斗轮椅有限公司',
    [string]$SourceMark = '',
    [string]$PrimaryColor = '#18212B'
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($SourceMark)) {
    $SourceMark = Join-Path $repoRoot 'info/logo-generated-source.png'
}

$sourcePath = (Resolve-Path -LiteralPath $SourceMark).Path
$infoDirectory = Join-Path $repoRoot 'info'
$adminPublicDirectory = Join-Path $repoRoot 'admin-web/public'

Add-Type -AssemblyName System.Drawing

function New-TransparentCanvas {
    param([int]$Width, [int]$Height)

    return [System.Drawing.Bitmap]::new(
        $Width,
        $Height,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
}

function Set-HighQualityGraphics {
    param([System.Drawing.Graphics]$Graphics)

    $Graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceOver
    $Graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $Graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $Graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $Graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $Graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::ClearTypeGridFit
}

function Get-VisibleBounds {
    param([System.Drawing.Bitmap]$Bitmap)

    $left = $Bitmap.Width
    $top = $Bitmap.Height
    $right = -1
    $bottom = -1

    for ($y = 0; $y -lt $Bitmap.Height; $y++) {
        for ($x = 0; $x -lt $Bitmap.Width; $x++) {
            if ($Bitmap.GetPixel($x, $y).A -gt 8) {
                if ($x -lt $left) { $left = $x }
                if ($x -gt $right) { $right = $x }
                if ($y -lt $top) { $top = $y }
                if ($y -gt $bottom) { $bottom = $y }
            }
        }
    }

    if ($right -lt $left -or $bottom -lt $top) {
        throw 'The source mark does not contain visible pixels.'
    }

    return [System.Drawing.Rectangle]::new(
        $left,
        $top,
        ($right - $left + 1),
        ($bottom - $top + 1))
}

function Save-Png {
    param([System.Drawing.Bitmap]$Bitmap, [string]$Path)

    $Bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
}

function New-MarkAsset {
    param(
        [System.Drawing.Bitmap]$Source,
        [System.Drawing.Rectangle]$SourceBounds,
        [int]$Size,
        [int]$Padding
    )

    $canvas = New-TransparentCanvas -Width $Size -Height $Size
    $graphics = [System.Drawing.Graphics]::FromImage($canvas)
    try {
        Set-HighQualityGraphics -Graphics $graphics
        $available = $Size - (2 * $Padding)
        $scale = [Math]::Min($available / $SourceBounds.Width, $available / $SourceBounds.Height)
        $width = [int][Math]::Round($SourceBounds.Width * $scale)
        $height = [int][Math]::Round($SourceBounds.Height * $scale)
        $destination = [System.Drawing.Rectangle]::new(
            [int](($Size - $width) / 2),
            [int](($Size - $height) / 2),
            $width,
            $height)
        $graphics.DrawImage($Source, $destination, $SourceBounds, [System.Drawing.GraphicsUnit]::Pixel)
    }
    finally {
        $graphics.Dispose()
    }

    return $canvas
}

function New-HorizontalAsset {
    param(
        [System.Drawing.Bitmap]$Mark,
        [string]$Text,
        [System.Drawing.Color]$TextColor
    )

    $width = 2172
    $height = 724
    $canvas = New-TransparentCanvas -Width $width -Height $height
    $graphics = [System.Drawing.Graphics]::FromImage($canvas)
    try {
        Set-HighQualityGraphics -Graphics $graphics

        $markSize = 572
        $markTop = [int](($height - $markSize) / 2)
        $graphics.DrawImage($Mark, [System.Drawing.Rectangle]::new(48, $markTop, $markSize, $markSize))

        $textLeft = 676
        $textRight = 2112
        $availableWidth = $textRight - $textLeft
        $fontSize = 132
        $font = $null
        do {
            if ($null -ne $font) { $font.Dispose() }
            $font = [System.Drawing.Font]::new(
                'Microsoft YaHei UI',
                $fontSize,
                [System.Drawing.FontStyle]::Bold,
                [System.Drawing.GraphicsUnit]::Pixel)
            $measured = $graphics.MeasureString($Text, $font)
            $fontSize -= 2
        } while ($measured.Width -gt $availableWidth -and $fontSize -ge 80)

        $brush = [System.Drawing.SolidBrush]::new($TextColor)
        $format = [System.Drawing.StringFormat]::new()
        try {
            $format.Alignment = [System.Drawing.StringAlignment]::Near
            $format.LineAlignment = [System.Drawing.StringAlignment]::Center
            $format.FormatFlags = [System.Drawing.StringFormatFlags]::NoWrap
            $textArea = [System.Drawing.RectangleF]::new(
                $textLeft,
                0,
                $availableWidth,
                $height)
            $graphics.DrawString($Text, $font, $brush, $textArea, $format)
        }
        finally {
            $format.Dispose()
            $brush.Dispose()
            $font.Dispose()
        }
    }
    finally {
        $graphics.Dispose()
    }

    return $canvas
}

function New-ScaledAsset {
    param([System.Drawing.Bitmap]$Source, [int]$Width, [int]$Height)

    $canvas = New-TransparentCanvas -Width $Width -Height $Height
    $graphics = [System.Drawing.Graphics]::FromImage($canvas)
    try {
        Set-HighQualityGraphics -Graphics $graphics
        $graphics.DrawImage($Source, [System.Drawing.Rectangle]::new(0, 0, $Width, $Height))
    }
    finally {
        $graphics.Dispose()
    }

    return $canvas
}

$source = [System.Drawing.Bitmap]::FromFile($sourcePath)
try {
    $visibleBounds = Get-VisibleBounds -Bitmap $source
    $mark = New-MarkAsset -Source $source -SourceBounds $visibleBounds -Size 1024 -Padding 72
    try {
        $horizontal = New-HorizontalAsset `
            -Mark $mark `
            -Text $CompanyName `
            -TextColor ([System.Drawing.ColorTranslator]::FromHtml($PrimaryColor))
        try {
            $legacy = New-ScaledAsset -Source $horizontal -Width 1086 -Height 362
            try {
                Save-Png -Bitmap $mark -Path (Join-Path $infoDirectory 'logo-v2-mark.png')
                Save-Png -Bitmap $horizontal -Path (Join-Path $infoDirectory 'logo-v2-horizontal.png')
                Save-Png -Bitmap $legacy -Path (Join-Path $infoDirectory 'logo.png')
                Save-Png -Bitmap $horizontal -Path (Join-Path $adminPublicDirectory 'logo.png')
                Save-Png -Bitmap $mark -Path (Join-Path $adminPublicDirectory 'favicon.png')
            }
            finally {
                $legacy.Dispose()
            }
        }
        finally {
            $horizontal.Dispose()
        }
    }
    finally {
        $mark.Dispose()
    }
}
finally {
    $source.Dispose()
}

Write-Host "Brand assets generated for $CompanyName"
