# Regenerates app.ico (and optionally a 256px PNG preview): two gauges for the two taskbar rows.
param([string]$Out = (Join-Path $PSScriptRoot '../app.ico'), [string]$Preview = '')
Add-Type -AssemblyName System.Drawing
$sizes = 16, 20, 24, 32, 40, 48, 64, 96, 128, 256
$pngs = @()
foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'
    $k = $s / 256.0
    # Rounded dark tile
    $r = 56 * $k; $pad = 8 * $k; $w = $s - 2 * $pad
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($pad, $pad, 2*$r, 2*$r, 180, 90)
    $path.AddArc($pad + $w - 2*$r, $pad, 2*$r, 2*$r, 270, 90)
    $path.AddArc($pad + $w - 2*$r, $pad + $w - 2*$r, 2*$r, 2*$r, 0, 90)
    $path.AddArc($pad, $pad + $w - 2*$r, 2*$r, 2*$r, 90, 90)
    $path.CloseFigure()
    $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.PointF 0, 0), (New-Object System.Drawing.PointF $s, $s), ([System.Drawing.Color]::FromArgb(255, 38, 44, 62)), ([System.Drawing.Color]::FromArgb(255, 16, 19, 30))
    $g.FillPath($bg, $path)
    # Two concentric gauges = the two taskbar rows
    $c = $s / 2.0; $cy = $c + 10 * $k
    function Arc($radius, $thick, $fraction, $c1, $c2) {
        $rect = New-Object System.Drawing.RectangleF ($c - $radius), ($cy - $radius), (2 * $radius), (2 * $radius)
        $track = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(60, 255, 255, 255)), $thick
        $track.StartCap = 'Round'; $track.EndCap = 'Round'
        $g.DrawArc($track, $rect, 135, 270)
        $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, $c1, $c2, 0.0
        $pen = New-Object System.Drawing.Pen $grad, $thick
        $pen.StartCap = 'Round'; $pen.EndCap = 'Round'
        $g.DrawArc($pen, $rect, 135, 270 * $fraction)
    }
    $outerThick = [Math]::Max(2.0, 26 * $k); $innerThick = [Math]::Max(2.0, 22 * $k)
    Arc (86 * $k) $outerThick 0.72 ([System.Drawing.Color]::FromArgb(255, 45, 212, 191)) ([System.Drawing.Color]::FromArgb(255, 132, 225, 120))
    if ($s -ge 24) { Arc (50 * $k) $innerThick 0.45 ([System.Drawing.Color]::FromArgb(255, 99, 150, 255)) ([System.Drawing.Color]::FromArgb(255, 168, 132, 255)) }
    # Center dot
    $d = [Math]::Max(2.0, 18 * $k)
    $g.FillEllipse([System.Drawing.Brushes]::White, $c - $d/2, $cy - $d/2, $d, $d)
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    if ($Preview -and $s -eq 256) { $bmp.Save($Preview) }
    $bmp.Dispose()
    $pngs += ,@($s, $ms.ToArray())
}
$fs = [System.IO.File]::Create($Out); $bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$pngs.Count)
$offset = 6 + 16 * $pngs.Count
foreach ($p in $pngs) {
    $dim = if ($p[0] -ge 256) { 0 } else { $p[0] }
    $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32); $bw.Write([uint32]$p[1].Length); $bw.Write([uint32]$offset)
    $offset += $p[1].Length
}
foreach ($p in $pngs) { $bw.Write($p[1]) }
$bw.Close()
