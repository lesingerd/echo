# Generates assets/echo.ico -- the ">|<" Echo mark.
# Run with Windows PowerShell:
#   powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools\generate-icon.ps1
# The geometry here mirrors src/TrayIconFactory.cs, which renders the live tray icon.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$sizes = @(16, 20, 24, 32, 48, 64, 128, 256)
$outPath = Join-Path (Split-Path -Parent $PSScriptRoot) 'assets\echo.ico'

function New-EchoBitmap {
    param([int]$Size)

    $bmp = New-Object System.Drawing.Bitmap -ArgumentList $Size, $Size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    $s = $Size / 32.0
    $stroke = [Math]::Max(1.3, 2.6 * $s)
    $blue = [System.Drawing.Color]::FromArgb(255, 33, 150, 243)

    $pen = New-Object System.Drawing.Pen -ArgumentList $blue, ([float]$stroke)
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round

    $left = [System.Drawing.PointF[]]@(
        [System.Drawing.PointF]::new([float](4.5 * $s), [float](9.0 * $s)),
        [System.Drawing.PointF]::new([float](9.5 * $s), [float](16.0 * $s)),
        [System.Drawing.PointF]::new([float](4.5 * $s), [float](23.0 * $s)))
    $right = [System.Drawing.PointF[]]@(
        [System.Drawing.PointF]::new([float](27.5 * $s), [float](9.0 * $s)),
        [System.Drawing.PointF]::new([float](22.5 * $s), [float](16.0 * $s)),
        [System.Drawing.PointF]::new([float](27.5 * $s), [float](23.0 * $s)))

    $g.DrawLines($pen, $left)
    $g.DrawLine($pen,
        [System.Drawing.PointF]::new([float](16.0 * $s), [float](5.5 * $s)),
        [System.Drawing.PointF]::new([float](16.0 * $s), [float](26.5 * $s)))
    $g.DrawLines($pen, $right)

    $pen.Dispose()
    $g.Dispose()
    return $bmp
}

function Get-DibBytes {
    param([System.Drawing.Bitmap]$Bitmap)

    $w = $Bitmap.Width
    $h = $Bitmap.Height
    $rect = New-Object System.Drawing.Rectangle -ArgumentList 0, 0, $w, $h
    $data = $Bitmap.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $stride = $data.Stride
    $pixels = New-Object byte[] ($stride * $h)
    [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $pixels, 0, $pixels.Length)
    $Bitmap.UnlockBits($data)

    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter -ArgumentList $ms

    # BITMAPINFOHEADER -- height is doubled to cover the (unused) AND mask.
    $bw.Write([int]40)
    $bw.Write([int]$w)
    $bw.Write([int]($h * 2))
    $bw.Write([int16]1)
    $bw.Write([int16]32)
    $bw.Write([int]0)
    $bw.Write([int]($w * $h * 4))
    $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0)

    # XOR bitmap: bottom-up BGRA rows.
    for ($y = $h - 1; $y -ge 0; $y--) { $bw.Write($pixels, $y * $stride, $w * 4) }

    # AND mask: all zero, rows padded to 4 bytes. The alpha channel carries the shape.
    $maskRow = [int]([Math]::Floor(($w + 31) / 32) * 4)
    $zeros = New-Object byte[] ($maskRow * $h)
    $bw.Write($zeros, 0, $zeros.Length)

    $bw.Flush()
    $bytes = $ms.ToArray()
    $bw.Dispose(); $ms.Dispose()
    return ,$bytes
}

function Get-PngBytes {
    param([System.Drawing.Bitmap]$Bitmap)
    $ms = New-Object System.IO.MemoryStream
    $Bitmap.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bytes = $ms.ToArray()
    $ms.Dispose()
    return ,$bytes
}

$images = @()
foreach ($size in $sizes) {
    $bmp = New-EchoBitmap -Size $size
    # Classic DIB entries for the small sizes, PNG for the large ones (256 must be PNG).
    if ($size -ge 128) { $bytes = Get-PngBytes -Bitmap $bmp } else { $bytes = Get-DibBytes -Bitmap $bmp }
    $images += [pscustomobject]@{ Size = $size; Bytes = $bytes }
    $bmp.Dispose()
}

$icoStream = New-Object System.IO.MemoryStream
$w2 = New-Object System.IO.BinaryWriter -ArgumentList $icoStream
$w2.Write([int16]0)
$w2.Write([int16]1)
$w2.Write([int16]$images.Count)

$offset = 6 + (16 * $images.Count)
foreach ($img in $images) {
    $dim = if ($img.Size -ge 256) { 0 } else { $img.Size }
    $w2.Write([byte]$dim); $w2.Write([byte]$dim)
    $w2.Write([byte]0); $w2.Write([byte]0)
    $w2.Write([int16]1); $w2.Write([int16]32)
    $w2.Write([int]$img.Bytes.Length)
    $w2.Write([int]$offset)
    $offset += $img.Bytes.Length
}
foreach ($img in $images) { $w2.Write($img.Bytes, 0, $img.Bytes.Length) }
$w2.Flush()

$dir = Split-Path -Parent $outPath
if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir | Out-Null }
[System.IO.File]::WriteAllBytes($outPath, $icoStream.ToArray())
$w2.Dispose(); $icoStream.Dispose()

Write-Host ("Wrote {0} ({1} bytes, {2} sizes)" -f $outPath, (Get-Item $outPath).Length, $images.Count)
