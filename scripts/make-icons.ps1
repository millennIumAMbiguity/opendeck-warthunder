Add-Type -AssemblyName System.Drawing

# Generates the plugin and action icons, plus docs/icon.png (512 px) for the plugin catalogue.
# Keys show live text on top, so backgrounds stay dark. Run after changing the drawing code; output is committed.
$wt = Join-Path $PSScriptRoot '..\plugin\io.github.millenniumambiguity.warthunder.sdPlugin\assets'
New-Item -ItemType Directory -Force $wt | Out-Null
foreach ($size in 72, 144, 512) {
    $s = $size / 72.0
    $suffix = if ($size -eq 72) { '' } else { '@2x' }

    # Plugin/category icon: simple aircraft silhouette.
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::FromArgb(24, 24, 28))
    $brush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(230, 160, 40))
    $pts = @(
        (New-Object System.Drawing.PointF (36 * $s), (8 * $s)),
        (New-Object System.Drawing.PointF (40 * $s), (28 * $s)),
        (New-Object System.Drawing.PointF (66 * $s), (40 * $s)),
        (New-Object System.Drawing.PointF (40 * $s), (40 * $s)),
        (New-Object System.Drawing.PointF (39 * $s), (56 * $s)),
        (New-Object System.Drawing.PointF (48 * $s), (64 * $s)),
        (New-Object System.Drawing.PointF (24 * $s), (64 * $s)),
        (New-Object System.Drawing.PointF (33 * $s), (56 * $s)),
        (New-Object System.Drawing.PointF (32 * $s), (40 * $s)),
        (New-Object System.Drawing.PointF (6 * $s), (40 * $s)),
        (New-Object System.Drawing.PointF (32 * $s), (28 * $s))
    )
    $g.FillPolygon($brush, [System.Drawing.PointF[]]$pts)
    $g.Dispose()
    if ($size -eq 512) {
        New-Item -ItemType Directory -Force (Join-Path $PSScriptRoot '..\docs') | Out-Null
        $bmp.Save((Join-Path $PSScriptRoot '..\docs\icon.png'), [System.Drawing.Imaging.ImageFormat]::Png)
        $bmp.Dispose()
        continue
    }
    $bmp.Save((Join-Path $wt "icon$suffix.png"), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()

    # Flight data: dim gauge arc.
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::FromArgb(16, 16, 20))
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(70, 70, 80)), (4 * $s)
    $g.DrawArc($pen, 10 * $s, 10 * $s, 52 * $s, 52 * $s, 135, 270)
    $g.Dispose()
    $bmp.Save((Join-Path $wt "flightdata$suffix.png"), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()

    # Indicator: unlit lamp outline (dark, so a Multi Panel LED starts off).
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Black)
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(90, 255, 176, 0)), (3 * $s)
    $g.DrawRectangle($pen, 4 * $s, 4 * $s, 64 * $s, 64 * $s)
    $g.Dispose()
    $bmp.Save((Join-Path $wt "indicator$suffix.png"), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

Copy-Item (Join-Path $wt 'indicator.png') (Join-Path $wt 'sendkey.png')
Copy-Item (Join-Path $wt 'indicator@2x.png') (Join-Path $wt 'sendkey@2x.png')
