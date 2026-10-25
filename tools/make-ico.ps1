param(
    [string]$Png,
    [string]$Ico
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$code = @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Collections.Generic;
using System.Runtime.InteropServices;

public class IconTool
{
    public static void ClearWhiteCorners(string inPath, string outPath, int tol)
    {
        using (var src = new Bitmap(inPath))
        using (var bmp = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb))
        {
            using (var g = Graphics.FromImage(bmp)) { g.DrawImage(src, 0, 0, src.Width, src.Height); }
            int w = bmp.Width, h = bmp.Height;
            var rect = new Rectangle(0, 0, w, h);
            var data = bmp.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            int stride = data.Stride;
            byte[] buf = new byte[stride * h];
            Marshal.Copy(data.Scan0, buf, 0, buf.Length);
            var visited = new bool[w * h];
            var q = new Queue<int>();
            int[] seeds = { 0, w - 1, (h - 1) * w, (h - 1) * w + w - 1 };
            foreach (int s in seeds) { if (!visited[s]) { visited[s] = true; q.Enqueue(s); } }
            while (q.Count > 0)
            {
                int idx = q.Dequeue();
                int x = idx % w, y = idx / w, p = y * stride + x * 4;
                int b = buf[p], gg = buf[p + 1], r = buf[p + 2];
                int d = Math.Max(Math.Abs(255 - r), Math.Max(Math.Abs(255 - gg), Math.Abs(255 - b)));
                if (d > tol) continue;
                buf[p + 3] = 0;
                if (x > 0 && !visited[idx - 1]) { visited[idx - 1] = true; q.Enqueue(idx - 1); }
                if (x < w - 1 && !visited[idx + 1]) { visited[idx + 1] = true; q.Enqueue(idx + 1); }
                if (y > 0 && !visited[idx - w]) { visited[idx - w] = true; q.Enqueue(idx - w); }
                if (y < h - 1 && !visited[idx + w]) { visited[idx + w] = true; q.Enqueue(idx + w); }
            }
            Marshal.Copy(buf, 0, data.Scan0, buf.Length);
            bmp.UnlockBits(data);
            bmp.Save(outPath, ImageFormat.Png);
        }
    }

    public static Bitmap Resize(Image src, int size)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
            g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
            g.Clear(Color.Transparent);
            g.DrawImage(src, 0, 0, size, size);
        }
        return bmp;
    }
}
'@
Add-Type -TypeDefinition $code -ReferencedAssemblies System.Drawing

$work = Join-Path ([IO.Path]::GetTempPath()) 'portalautologin-icon'
New-Item -ItemType Directory -Force -Path $work | Out-Null
$clean = Join-Path $work 'clean.png'
[IconTool]::ClearWhiteCorners($Png, $clean, 12)

$img = [System.Drawing.Image]::FromFile($clean)
$sizes = @(256, 128, 64, 48, 32, 16)
$frames = @()
foreach ($s in $sizes) {
    $bmp = [IconTool]::Resize($img, $s)
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $frames += , @{ Size = $s; Bytes = $ms.ToArray() }
    $bmp.Dispose()
    $ms.Dispose()
}
$img.Dispose()

$fs = [System.IO.File]::Create($Ico)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([UInt16]0)
$bw.Write([UInt16]1)
$bw.Write([UInt16]$frames.Count)
$offset = 6 + 16 * $frames.Count
foreach ($f in $frames) {
    $s = $f.Size
    $b = 0
    if ($s -lt 256) { $b = $s }
    $bw.Write([Byte]$b)
    $bw.Write([Byte]$b)
    $bw.Write([Byte]0)
    $bw.Write([Byte]0)
    $bw.Write([UInt16]1)
    $bw.Write([UInt16]32)
    $bw.Write([UInt32]$f.Bytes.Length)
    $bw.Write([UInt32]$offset)
    $offset += $f.Bytes.Length
}
foreach ($f in $frames) { $bw.Write($f.Bytes) }
$bw.Flush(); $bw.Close(); $fs.Close()

$icon = New-Object System.Drawing.Icon($Ico)
Write-Output ("ICO OK: {0} bytes, {1}x{2}" -f (Get-Item $Ico).Length, $icon.Width, $icon.Height)
$icon.Dispose()
