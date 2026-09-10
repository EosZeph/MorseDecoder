[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$root = (Resolve-Path (Split-Path -Parent $PSScriptRoot)).Path
$assetDirectory = Join-Path $root "src\MorseDecoder.App\Assets"
$iconPath = Join-Path $assetDirectory "MorseDecoder.ico"
$previewPath = Join-Path $assetDirectory "MorseDecoder.png"

New-Item -ItemType Directory -Force -Path $assetDirectory | Out-Null

function New-RoundedRectanglePath {
    param(
        [float]$X,
        [float]$Y,
        [float]$Width,
        [float]$Height,
        [float]$Radius
    )

    $diameter = $Radius * 2
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($X, $Y, $diameter, $diameter, 180, 90)
    $path.AddArc($X + $Width - $diameter, $Y, $diameter, $diameter, 270, 90)
    $path.AddArc(
        $X + $Width - $diameter,
        $Y + $Height - $diameter,
        $diameter,
        $diameter,
        0,
        90)
    $path.AddArc($X, $Y + $Height - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-IconBitmap {
    param([int]$Size)

    $bitmap = New-Object `
        -TypeName System.Drawing.Bitmap `
        -ArgumentList $Size, $Size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)

    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $graphics.Clear([System.Drawing.Color]::Transparent)

        $scale = $Size / 1024.0
        $graphics.ScaleTransform($scale, $scale)

        $backgroundPath = New-RoundedRectanglePath -X 28 -Y 28 -Width 968 -Height 968 -Radius 188
        $backgroundBrush = New-Object `
            -TypeName System.Drawing.SolidBrush `
            -ArgumentList ([System.Drawing.Color]::FromArgb(255, 8, 24, 36))
        $backgroundPen = New-Object `
            -TypeName System.Drawing.Pen `
            -ArgumentList ([System.Drawing.Color]::FromArgb(255, 42, 80, 103)), 18
        try {
            $graphics.FillPath($backgroundBrush, $backgroundPath)
            $graphics.DrawPath($backgroundPen, $backgroundPath)
        }
        finally {
            $backgroundBrush.Dispose()
            $backgroundPen.Dispose()
            $backgroundPath.Dispose()
        }

        $signalPen = New-Object `
            -TypeName System.Drawing.Pen `
            -ArgumentList ([System.Drawing.Color]::FromArgb(255, 43, 210, 226)), 42
        $signalPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $signalPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $signalPen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round

        $wavePath = New-Object System.Drawing.Drawing2D.GraphicsPath
        try {
            for ($index = 0; $index -le 96; $index++) {
                $x = 150 + (($index / 96.0) * 724)
                $phase = (($index / 96.0) * [Math]::PI * 4.0) - ([Math]::PI / 2.0)
                $y = 730 + ([Math]::Sin($phase) * 58)

                if ($index -eq 0) {
                    $wavePath.AddLine($x, $y, $x + 0.1, $y)
                }
                else {
                    $wavePath.AddLine($x - (724 / 96.0), $previousY, $x, $y)
                }

                $previousY = $y
            }

            $graphics.DrawPath($signalPen, $wavePath)
        }
        finally {
            $wavePath.Dispose()
            $signalPen.Dispose()
        }

        $dotBrush = New-Object `
            -TypeName System.Drawing.SolidBrush `
            -ArgumentList ([System.Drawing.Color]::FromArgb(255, 243, 249, 252))
        $dashBrush = New-Object `
            -TypeName System.Drawing.SolidBrush `
            -ArgumentList ([System.Drawing.Color]::FromArgb(255, 236, 182, 67))
        try {
            $graphics.FillEllipse($dotBrush, 176, 388, 146, 146)
            $graphics.FillEllipse($dotBrush, 700, 388, 146, 146)

            $dashPath = New-RoundedRectanglePath -X 370 -Y 407 -Width 284 -Height 108 -Radius 54
            try {
                $graphics.FillPath($dashBrush, $dashPath)
            }
            finally {
                $dashPath.Dispose()
            }
        }
        finally {
            $dotBrush.Dispose()
            $dashBrush.Dispose()
        }
    }
    finally {
        $graphics.Dispose()
    }

    return $bitmap
}

function Convert-BitmapToPngBytes {
    param([System.Drawing.Bitmap]$Bitmap)

    $stream = New-Object System.IO.MemoryStream
    try {
        $Bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
        $bytes = $stream.ToArray()
        return ,$bytes
    }
    finally {
        $stream.Dispose()
    }
}

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$images = @()

foreach ($size in $sizes) {
    $bitmap = New-IconBitmap -Size $size
    try {
        $images += [PSCustomObject]@{
            Size = $size
            Bytes = Convert-BitmapToPngBytes -Bitmap $bitmap
        }
    }
    finally {
        $bitmap.Dispose()
    }
}

$previewBitmap = New-IconBitmap -Size 512
try {
    $previewBitmap.Save($previewPath, [System.Drawing.Imaging.ImageFormat]::Png)
}
finally {
    $previewBitmap.Dispose()
}

$stream = New-Object System.IO.MemoryStream
$writer = New-Object -TypeName System.IO.BinaryWriter -ArgumentList $stream
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$images.Count)

    $offset = 6 + (16 * $images.Count)
    foreach ($image in $images) {
        $dimension = if ($image.Size -eq 256) { 0 } else { $image.Size }
        $writer.Write([byte]$dimension)
        $writer.Write([byte]$dimension)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$image.Bytes.Length)
        $writer.Write([uint32]$offset)
        $offset += $image.Bytes.Length
    }

    foreach ($image in $images) {
        $bytes = [byte[]]$image.Bytes
        $writer.Write($bytes, 0, $bytes.Length)
    }

    $writer.Flush()
    [System.IO.File]::WriteAllBytes($iconPath, $stream.ToArray())
}
finally {
    $writer.Dispose()
    $stream.Dispose()
}

Write-Host "Icon generated:"
Write-Host "  ICO     : $iconPath"
Write-Host "  Preview : $previewPath"
