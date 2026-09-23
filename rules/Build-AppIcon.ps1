# 从仓库根目录的品牌素材生成多尺寸应用图标（ICO 容器内嵌 PNG 负载）
#
#   pwsh -NoProfile -File ./rules/Build-AppIcon.ps1
#
# 产物：src/SysSuite.UI/Assets/App.ico（16/24/32/48/64/128/256）
#       SysSuite.UI 通过 <ApplicationIcon> 嵌入 exe；Watchdog 以嵌入资源复用为托盘图标。
# 前提：系统有 System.Drawing（Windows PowerShell 5.1 / pwsh 均可）。
# 换素材后重跑本脚本即可，无需改 csproj。
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$repoRoot = Split-Path -Parent $PSScriptRoot
$source   = Join-Path $repoRoot 'winlogo.png'
$outDir   = Join-Path $repoRoot 'src/SysSuite.UI/Assets'
$outIco   = Join-Path $outDir 'App.ico'
$report   = Join-Path $repoRoot 'obj/appicon_report.txt'
$sizes    = @(16, 24, 32, 48, 64, 128, 256)

if (-not (Test-Path $source)) { throw "source not found: $source" }
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir -Force | Out-Null }

$src = [System.Drawing.Image]::FromFile($source)
try {
    # 中心裁剪为正方形并留出 8% 内边距，避免小尺寸下主体被边缘吞掉
    $side   = [Math]::Min($src.Width, $src.Height)
    $crop   = [int]($side * 0.92)
    $offset = [int](($side - $crop) / 2)

    $payloads = @()
    foreach ($s in $sizes) {
        $bmp = New-Object System.Drawing.Bitmap($s, $s, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $g   = [System.Drawing.Graphics]::FromImage($bmp)
        try {
            $g.InterpolationMode  = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $g.PixelOffsetMode    = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $g.SmoothingMode      = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
            $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
            $dest    = New-Object System.Drawing.Rectangle(0, 0, $s, $s)
            $srcRect = New-Object System.Drawing.Rectangle($offset, $offset, $crop, $crop)
            $g.DrawImage($src, $dest, $srcRect, [System.Drawing.GraphicsUnit]::Pixel)
        } finally { $g.Dispose() }

        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $bmp.Dispose()
        $payloads += ,@{ Size = $s; Bytes = $ms.ToArray() }
        $ms.Dispose()
    }

    # 组装 ICO：ICONDIR(6) + ICONDIRENTRY(16*n) + 各图像数据
    $fs = [System.IO.File]::Create($outIco)
    $bw = New-Object System.IO.BinaryWriter($fs)
    try {
        $bw.Write([UInt16]0)                 # reserved
        $bw.Write([UInt16]1)                 # type: icon
        $bw.Write([UInt16]$payloads.Count)   # count

        $dataOffset = 6 + (16 * $payloads.Count)
        foreach ($p in $payloads) {
            $dim = if ($p.Size -ge 256) { 0 } else { $p.Size }   # 0 表示 256
            $bw.Write([Byte]$dim)            # width
            $bw.Write([Byte]$dim)            # height
            $bw.Write([Byte]0)               # palette count
            $bw.Write([Byte]0)               # reserved
            $bw.Write([UInt16]1)             # color planes
            $bw.Write([UInt16]32)            # bits per pixel
            $bw.Write([UInt32]$p.Bytes.Length)
            $bw.Write([UInt32]$dataOffset)
            $dataOffset += $p.Bytes.Length
        }
        foreach ($p in $payloads) { $bw.Write($p.Bytes) }
    } finally {
        $bw.Dispose()
        $fs.Dispose()
    }
} finally { $src.Dispose() }

$info = Get-Item $outIco
Set-Content -Path $report -Encoding UTF8 -Value @"
OK
target = $outIco
bytes  = $($info.Length)
sizes  = $($sizes -join ', ')
source = $source
"@
Write-Output "icon written: $outIco ($($info.Length) bytes)"
