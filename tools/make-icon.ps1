# Tạo app.ico (nhiều kích thước, nén PNG) cho PinDisplay.exe
# Chạy: powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1
param([string]$Out = "$PSScriptRoot\..\app.ico")

Add-Type -AssemblyName System.Drawing

function RoundRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = [Math]::Min($r * 2, [Math]::Min($w, $h))
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function Render([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)

    # Nền: ô vuông bo góc, gradient xanh dương
    $m = [float]($s * 0.04)
    $bg = RoundRect $m $m ($s - 2 * $m) ($s - 2 * $m) ($s * 0.22)
    $rect = New-Object System.Drawing.RectangleF 0, 0, $s, $s
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, `
        ([System.Drawing.Color]::FromArgb(255, 74, 168, 255)), ([System.Drawing.Color]::FromArgb(255, 0, 95, 184)), 60.0
    $g.FillPath($grad, $bg)

    # Cục pin trắng
    $stroke = [float][Math]::Max(1.5, $s * 0.065)
    $bw = $s * 0.56; $bh = $s * 0.34
    $bx = ($s - $bw) / 2 - $s * 0.03; $by = ($s - $bh) / 2
    $body = RoundRect $bx $by $bw $bh ($s * 0.07)
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), $stroke
    $g.DrawPath($pen, $body)
    $nubW = $s * 0.06
    $nub = RoundRect ($bx + $bw + $stroke / 2 + $s * 0.01) ($by + $bh * 0.3) $nubW ($bh * 0.4) ($nubW / 2)
    $g.FillPath([System.Drawing.Brushes]::White, $nub)

    # Ruột pin xanh lá ~70%
    $gap = $stroke + [Math]::Max(1.0, $s * 0.035)
    $iw = ($bw - 2 * $gap) * 0.7; $ih = $bh - 2 * $gap
    if ($ih -gt 0.5) {
        $fill = RoundRect ($bx + $gap) ($by + $gap) $iw $ih ($s * 0.03)
        $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 108, 230, 120))), $fill)
    }
    $g.Dispose()
    return $bmp
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 256
# 256px lưu dạng PNG; các cỡ nhỏ lưu dạng DIB 32-bit (tương thích tốt nhất)
function ToDib([System.Drawing.Bitmap]$bmp) {
    $s = $bmp.Width
    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter $ms
    $maskRow = [int]([Math]::Ceiling($s / 32.0) * 4)
    $bw.Write([UInt32]40); $bw.Write([Int32]$s); $bw.Write([Int32]($s * 2))
    $bw.Write([UInt16]1); $bw.Write([UInt16]32); $bw.Write([UInt32]0)
    $bw.Write([UInt32]($s * $s * 4 + $maskRow * $s)); $bw.Write([Int32]0); $bw.Write([Int32]0)
    $bw.Write([UInt32]0); $bw.Write([UInt32]0)
    for ($y = $s - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $s; $x++) {
            $c = $bmp.GetPixel($x, $y)
            $bw.Write([byte]$c.B); $bw.Write([byte]$c.G); $bw.Write([byte]$c.R); $bw.Write([byte]$c.A)
        }
    }
    $bw.Write((New-Object byte[] ($maskRow * $s)))
    $bw.Flush()
    return , $ms.ToArray()
}

$pngs = foreach ($s in $sizes) {
    $bmp = Render $s
    if ($s -ge 256) {
        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $data = $ms.ToArray()
    } else {
        $data = ToDib $bmp
    }
    $bmp.Dispose()
    , $data
}

$fs = [System.IO.File]::Create([System.IO.Path]::GetFullPath($Out))
$w = New-Object System.IO.BinaryWriter $fs
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $len = $pngs[$i].Length
    $dim = if ($s -ge 256) { 0 } else { $s }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([UInt16]1); $w.Write([UInt16]32); $w.Write([UInt32]$len); $w.Write([UInt32]$offset)
    $offset += $len
}
foreach ($p in $pngs) { $w.Write($p) }
$w.Close()
Write-Host "Wrote $Out"
