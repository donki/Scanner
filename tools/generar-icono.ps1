<#
.SYNOPSIS
    Dibuja el icono de sOC Scanner (mismas medidas que assets/appicon.svg): appicon.ico con 16, 20,
    24, 32, 40, 48, 64, 128 y 256 px y Assets/logo.png (256). Fondo indigo redondeado, una hoja en
    blanco con renglones y la linea de luz del escaner cruzandola.
.EXAMPLE
    pwsh -File tools\generar-icono.ps1
#>
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$app = Join-Path (Split-Path -Parent $PSScriptRoot) 'src\Scanner.App'

function RoundRect($x, $y, $w, $h, $r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $p.AddArc($x, $y, 2*$r, 2*$r, 180, 90); $p.AddArc($x+$w-2*$r, $y, 2*$r, 2*$r, 270, 90)
    $p.AddArc($x+$w-2*$r, $y+$h-2*$r, 2*$r, 2*$r, 0, 90); $p.AddArc($x, $y+$h-2*$r, 2*$r, 2*$r, 90, 90); $p.CloseFigure()
    return $p
}

function Draw([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)
    $s = $size / 256.0
    $bg = RoundRect 0 0 ($size - 1) ($size - 1) (56 * $s)
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush ([System.Drawing.PointF]::new(0,0)), ([System.Drawing.PointF]::new($size,$size)), ([System.Drawing.Color]::FromArgb(0x63,0x5B,0xF2)), ([System.Drawing.Color]::FromArgb(0x2A,0x1C,0xB8))
    $g.FillPath($brush, $bg)
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), ([Math]::Max(1.5, 13*$s))
    $pen.LineJoin = 'Round'; $pen.StartCap = 'Round'; $pen.EndCap = 'Round'
    # La hoja.
    $g.DrawPath($pen, (RoundRect (70*$s) (40*$s) (116*$s) (176*$s) (14*$s)))
    # Renglones (solo si se ven: en 16 px serian una mancha).
    if ($size -ge 32) {
        $thin = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(0xC3,0xC0,0xFF)), ([Math]::Max(1, 9*$s))
        $thin.StartCap = 'Round'; $thin.EndCap = 'Round'
        foreach ($y in 82, 108, 160, 186) { $g.DrawLine($thin, [float](98*$s), [float]($y*$s), [float](158*$s), [float]($y*$s)) }
    }
    # La linea de luz del escaner, mas ancha que la hoja.
    $beam = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(0x7D,0xF0,0xC8)), ([Math]::Max(2, 16*$s))
    $beam.StartCap = 'Round'; $beam.EndCap = 'Round'
    $g.DrawLine($beam, [float](44*$s), [float](134*$s), [float](212*$s), [float](134*$s))
    $g.Dispose()
    return $bmp
}

function Png($bmp) { $ms = New-Object System.IO.MemoryStream; $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose(); return ,$ms.ToArray() }

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$pngs = @(); foreach ($z in $sizes) { $pngs += ,(Png (Draw $z)) }
New-Item -ItemType Directory -Force (Join-Path $app 'Assets') | Out-Null
[IO.File]::WriteAllBytes((Join-Path $app 'Assets\logo.png'), (Png (Draw 256)))

$fs = [IO.File]::Create((Join-Path $app 'appicon.ico')); $bw = New-Object IO.BinaryWriter $fs
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $z = $sizes[$i]; $b = if ($z -ge 256) { 0 } else { $z }
    $bw.Write([byte]$b); $bw.Write([byte]$b); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32); $bw.Write([UInt32]$pngs[$i].Length); $bw.Write([UInt32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $bw.Write($p) }
$bw.Close()
"appicon.ico y Assets\logo.png generados en $app"
