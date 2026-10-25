param(
    [string]$Out
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

if (-not $Out) {
    $Out = Join-Path (Split-Path -Parent $PSScriptRoot) 'assets\icon-source.png'
}

$size = 1024
$bmp = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
$g.Clear([System.Drawing.Color]::Transparent)

# Background
$bg = [System.Drawing.Drawing2D.GraphicsPath]::new()
$bg.AddArc(32, 32, 440, 440, 180, 90)
$bg.AddArc(552, 32, 440, 440, 270, 90)
$bg.AddArc(552, 552, 440, 440, 0, 90)
$bg.AddArc(32, 552, 440, 440, 90, 90)
$bg.CloseFigure()
$c1 = [System.Drawing.Color]::FromArgb(255, 15, 118, 110)
$c2 = [System.Drawing.Color]::FromArgb(255, 17, 78, 74)
$brush = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
    [System.Drawing.Point]::new(64, 64),
    [System.Drawing.Point]::new(960, 960),
    $c1, $c2)
$g.FillPath($brush, $bg)

# Portal frame
$outer = [System.Drawing.Drawing2D.GraphicsPath]::new()
$outer.AddArc(280, 240, 280, 280, 180, 90)
$outer.AddArc(464, 240, 280, 280, 270, 90)
$outer.AddArc(464, 504, 280, 280, 0, 90)
$outer.AddArc(280, 504, 280, 280, 90, 90)
$outer.CloseFigure()

$inner = [System.Drawing.Drawing2D.GraphicsPath]::new()
$inner.AddArc(376, 336, 140, 140, 180, 90)
$inner.AddArc(508, 336, 140, 140, 270, 90)
$inner.AddArc(508, 548, 140, 140, 0, 90)
$inner.AddArc(376, 548, 140, 140, 90, 90)
$inner.CloseFigure()

$frame = [System.Drawing.Drawing2D.GraphicsPath]::new()
$frame.FillMode = [System.Drawing.Drawing2D.FillMode]::Alternate
$frame.AddPath($outer, $true)
$frame.AddPath($inner, $true)
$g.FillPath([System.Drawing.Brushes]::White, $frame)

# Arrow entering the portal
$arrow = [System.Drawing.Drawing2D.GraphicsPath]::new()
$arrow.AddPolygon(@(
    [System.Drawing.PointF]::new(170, 480),
    [System.Drawing.PointF]::new(470, 480),
    [System.Drawing.PointF]::new(470, 416),
    [System.Drawing.PointF]::new(620, 512),
    [System.Drawing.PointF]::new(470, 608),
    [System.Drawing.PointF]::new(470, 544),
    [System.Drawing.PointF]::new(170, 544)
))
$outline = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(255, 19, 78, 74), 22)
$outline.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
$g.DrawPath($outline, $arrow)
$mint = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 153, 246, 228))
$g.FillPath($mint, $arrow)

$dir = Split-Path -Parent $Out
if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)

$outline.Dispose(); $mint.Dispose(); $arrow.Dispose(); $frame.Dispose()
$outer.Dispose(); $inner.Dispose(); $bg.Dispose(); $brush.Dispose()
$g.Dispose(); $bmp.Dispose()

Write-Output ("PNG OK: {0} ({1:N0} bytes)" -f $Out, (Get-Item $Out).Length)
