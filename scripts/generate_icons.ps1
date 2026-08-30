Add-Type -AssemblyName System.Drawing
$scriptDir = $PSScriptRoot
$projectRoot = Split-Path -Parent $scriptDir
$imgDir = Join-Path $projectRoot "GeometryTransferTool\Images"

if (-not (Test-Path $imgDir)) {
    New-Item -ItemType Directory -Path $imgDir -Force | Out-Null
}

function Create-AddInIcon([string]$path, [int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    # Source Polygon (Blue)
    $brush1 = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(200, 33, 150, 243))
    $pen1 = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 21, 101, 192), [Math]::Max(1.0, [double]$size / 16.0))
    $pts1 = @(
        New-Object System.Drawing.PointF([float]($size * 0.1), [float]($size * 0.2)),
        New-Object System.Drawing.PointF([float]($size * 0.55), [float]($size * 0.1)),
        New-Object System.Drawing.PointF([float]($size * 0.45), [float]($size * 0.7)),
        New-Object System.Drawing.PointF([float]($size * 0.15), [float]($size * 0.6))
    )
    $g.FillPolygon($brush1, $pts1)
    $g.DrawPolygon($pen1, $pts1)

    # Target Polygon (Green)
    $brush2 = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(200, 76, 175, 80))
    $pen2 = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 46, 125, 50), [Math]::Max(1.0, [double]$size / 16.0))
    $pts2 = @(
        New-Object System.Drawing.PointF([float]($size * 0.4), [float]($size * 0.35)),
        New-Object System.Drawing.PointF([float]($size * 0.85), [float]($size * 0.25)),
        New-Object System.Drawing.PointF([float]($size * 0.9), [float]($size * 0.85)),
        New-Object System.Drawing.PointF([float]($size * 0.45), [float]($size * 0.75))
    )
    $g.FillPolygon($brush2, $pts2)
    $g.DrawPolygon($pen2, $pts2)

    # Arrow
    $penArrow = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 255, 152, 0), [Math]::Max(1.5, [double]$size / 10.0))
    $penArrow.EndCap = [System.Drawing.Drawing2D.LineCap]::ArrowAnchor
    $g.DrawLine($penArrow, [float]($size * 0.25), [float]($size * 0.45), [float]($size * 0.65), [float]($size * 0.55))

    $g.Dispose()
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

Create-AddInIcon (Join-Path $imgDir "GeometryTransfer16.png") 16
Create-AddInIcon (Join-Path $imgDir "GeometryTransfer32.png") 32
Create-AddInIcon (Join-Path $imgDir "GeometryTransferDockPane16.png") 16
Create-AddInIcon (Join-Path $imgDir "GeometryTransferDockPane32.png") 32
Write-Host "Icons generated successfully in: $imgDir" -ForegroundColor Green
