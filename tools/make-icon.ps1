# Draws src\PhotoFrame.ico: two prints on a pile, the front one a small landscape.
# Every size is drawn on its own rather than scaled, so 16 px stays legible.
# Run from anywhere: powershell -File tools\make-icon.ps1

Add-Type -AssemblyName System.Drawing
$out = Join-Path $PSScriptRoot '..\src\PhotoFrame.ico'
$sizes = 16, 20, 24, 32, 40, 48, 64, 256

function Draw-Print($g, [float]$s, [float]$cx, [float]$cy, [float]$angle, [scriptblock]$picture) {
    $w = 0.76 * $s; $h = 0.60 * $s
    $b = [Math]::Max(1.0, [Math]::Round(0.065 * $s))
    $state = $g.Save()
    $g.TranslateTransform($cx, $cy)
    $g.RotateTransform($angle)
    if ($s -ge 32) {
        $shadow = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(70, 0, 0, 0))
        $g.FillRectangle($shadow, -$w / 2 + 0.02 * $s, -$h / 2 + 0.035 * $s, $w, $h)
    }
    $g.FillRectangle((New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(245, 242, 234))), -$w / 2, -$h / 2, $w, $h)
    $inner = New-Object Drawing.RectangleF (-$w / 2 + $b), (-$h / 2 + $b), ($w - 2 * $b), ($h - 2 * $b)
    & $picture $g $inner
    $pen = New-Object Drawing.Pen ([Drawing.Color]::FromArgb(150, 40, 40, 40)), ([Math]::Max(1.0, $s / 64))
    $g.DrawRectangle($pen, -$w / 2, -$h / 2, $w, $h)
    $g.Restore($state)
}

$warm = {
    param($g, $r)
    $br = New-Object Drawing.Drawing2D.LinearGradientBrush $r, ([Drawing.Color]::FromArgb(242, 160, 90)), ([Drawing.Color]::FromArgb(170, 70, 90)), 90
    $g.FillRectangle($br, $r)
}
$landscape = {
    param($g, $r)
    $sky = New-Object Drawing.Drawing2D.LinearGradientBrush $r, ([Drawing.Color]::FromArgb(70, 140, 220)), ([Drawing.Color]::FromArgb(170, 210, 245)), 90
    $g.FillRectangle($sky, $r)
    $clip = $g.Clip
    $g.SetClip($r)
    $sun = 0.22 * $r.Height
    $g.FillEllipse((New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(255, 214, 90))), $r.Right - 0.32 * $r.Width, $r.Top + 0.14 * $r.Height, $sun, $sun)
    $hill = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(70, 150, 80))
    $g.FillEllipse($hill, $r.Left - 0.25 * $r.Width, $r.Top + 0.55 * $r.Height, 0.95 * $r.Width, 0.9 * $r.Height)
    $hill2 = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(45, 115, 65))
    $g.FillEllipse($hill2, $r.Left + 0.3 * $r.Width, $r.Top + 0.65 * $r.Height, 0.95 * $r.Width, 0.8 * $r.Height)
    $g.Clip = $clip
}

$pngs = foreach ($s in $sizes) {
    $bmp = New-Object Drawing.Bitmap $s, $s
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    Draw-Print $g $s (0.45 * $s) (0.40 * $s) -14 $warm
    Draw-Print $g $s (0.56 * $s) (0.60 * $s) 8 $landscape
    $g.Dispose()
    $ms = New-Object IO.MemoryStream
    if ($s -ge 256) {
        # PNG only at 256: older readers (System.Drawing among them) take PNG
        # entries for nothing smaller.
        $bmp.Save($ms, [Drawing.Imaging.ImageFormat]::Png)
    } else {
        # 32-bit DIB: header with doubled height, BGRA rows bottom-up, then an
        # empty AND mask (alpha does the masking), rows padded to 4 bytes.
        $bw = New-Object IO.BinaryWriter $ms
        $bw.Write([UInt32]40); $bw.Write([Int32]$s); $bw.Write([Int32](2 * $s))
        $bw.Write([UInt16]1); $bw.Write([UInt16]32); $bw.Write([UInt32]0)
        $bw.Write([UInt32]0); $bw.Write([Int32]0); $bw.Write([Int32]0); $bw.Write([UInt32]0); $bw.Write([UInt32]0)
        $data = $bmp.LockBits((New-Object Drawing.Rectangle 0, 0, $s, $s), 'ReadOnly', 'Format32bppArgb')
        $row = New-Object byte[] ($s * 4)
        for ($y = $s - 1; $y -ge 0; $y--) {
            [Runtime.InteropServices.Marshal]::Copy([IntPtr]($data.Scan0.ToInt64() + $y * $data.Stride), $row, 0, $s * 4)
            $bw.Write($row)
        }
        $bmp.UnlockBits($data)
        $bw.Write((New-Object byte[] ([Math]::Ceiling($s / 32) * 4 * $s)))
        $bw.Flush()
    }
    $bmp.Dispose()
    , $ms.ToArray()
}

# ICO: a 6-byte header, a 16-byte entry per size, then each PNG.
$file = New-Object IO.MemoryStream
$w = New-Object IO.BinaryWriter $file
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $w.Write([byte]($s % 256)); $w.Write([byte]($s % 256))
    $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([UInt16]1); $w.Write([UInt16]32)
    $w.Write([UInt32]$pngs[$i].Length); $w.Write([UInt32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $w.Write($p) }
$w.Flush()
[IO.File]::WriteAllBytes((Resolve-Path (Split-Path $out)).Path + '\PhotoFrame.ico', $file.ToArray())
"wrote $out"
