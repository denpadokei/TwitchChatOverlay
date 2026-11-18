#Requires -Version 5.1
<#
.SYNOPSIS
    MSIX のタイル / ストアロゴ画像を icon.ico から生成します。

.DESCRIPTION
    packaging/Assets 配下の PNG はこのスクリプトで生成します。
    アイコンを差し替えたときだけ実行してください（生成物はリポジトリにコミットします）。

.EXAMPLE
    pwsh -File build/New-MsixAssets.ps1
#>
[CmdletBinding()]
param(
    [string]$IconPath = (Join-Path $PSScriptRoot '..\TwitchChatOverlay\icon.ico'),
    [string]$OutputDir = (Join-Path $PSScriptRoot '..\packaging\Assets'),
    [string]$BackgroundColor = 'Transparent'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

if (-not (Test-Path -LiteralPath $IconPath)) {
    throw "アイコンファイルが見つかりません: $IconPath"
}
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

$iconPath = (Resolve-Path -LiteralPath $IconPath).ProviderPath
$source = New-Object System.Drawing.Icon($iconPath, 256, 256)
$sourceBitmap = $source.ToBitmap()

function New-CanvasImage {
    param(
        [int]$Width,
        [int]$Height,
        [string]$FileName,
        [double]$ContentRatio
    )

    $bitmap = New-Object System.Drawing.Bitmap($Width, $Height, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality

        if ($BackgroundColor -ne 'Transparent') {
            $background = [System.Drawing.ColorTranslator]::FromHtml($BackgroundColor)
            $graphics.Clear($background)
        }
        else {
            $graphics.Clear([System.Drawing.Color]::Transparent)
        }

        # 余白を均等に取り、縦横比を保って中央に配置する
        $available = [Math]::Min($Width, $Height) * $ContentRatio
        $scale = [Math]::Min($available / $sourceBitmap.Width, $available / $sourceBitmap.Height)
        $drawWidth = [int][Math]::Round($sourceBitmap.Width * $scale)
        $drawHeight = [int][Math]::Round($sourceBitmap.Height * $scale)
        $x = [int][Math]::Round(($Width - $drawWidth) / 2)
        $y = [int][Math]::Round(($Height - $drawHeight) / 2)

        $graphics.DrawImage($sourceBitmap, $x, $y, $drawWidth, $drawHeight)

        $path = Join-Path $OutputDir $FileName
        $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
        Write-Host ("  {0} ({1}x{2})" -f $FileName, $Width, $Height)
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

Write-Host "MSIX 用画像を生成します (source=$iconPath):"
# 正方形タイルは余白を広めに取り、Windows のタイル表示で見切れないようにする
New-CanvasImage -Width 44 -Height 44 -FileName 'Square44x44Logo.png' -ContentRatio 0.98
New-CanvasImage -Width 50 -Height 50 -FileName 'StoreLogo.png' -ContentRatio 0.90
New-CanvasImage -Width 150 -Height 150 -FileName 'Square150x150Logo.png' -ContentRatio 0.70
New-CanvasImage -Width 310 -Height 310 -FileName 'Square310x310Logo.png' -ContentRatio 0.70
New-CanvasImage -Width 310 -Height 150 -FileName 'Wide310x150Logo.png' -ContentRatio 0.65

$sourceBitmap.Dispose()
$source.Dispose()

Write-Host "完了: $OutputDir"
