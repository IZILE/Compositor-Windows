param(
    [string]$SourceDirectory = (Join-Path $PSScriptRoot 'icon-source'),
    [string]$AssetsDirectory = (Join-Path $PSScriptRoot '../src/Compositor.Desktop/Assets'),
    [string]$FrameDirectory
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$iconSourcePath = (Resolve-Path -LiteralPath (Join-Path $SourceDirectory 'app-icon-1024.png')).Path
if (-not $FrameDirectory) { throw 'Supply a frame directory under work/ for verification.' }
New-Item -ItemType Directory -Path $FrameDirectory -Force | Out-Null
$iconSource = [Drawing.Bitmap]::new($iconSourcePath)
try {
    $left = $iconSource.Width; $top = $iconSource.Height; $right = -1; $bottom = -1
    for ($y = 0; $y -lt $iconSource.Height; $y++) {
        for ($x = 0; $x -lt $iconSource.Width; $x++) {
            if ($iconSource.GetPixel($x, $y).A -eq 0) { continue }
            $left = [Math]::Min($left, $x); $top = [Math]::Min($top, $y)
            $right = [Math]::Max($right, $x); $bottom = [Math]::Max($bottom, $y)
        }
    }
    $sourceRect = [Drawing.Rectangle]::new($left, $top, $right - $left + 1, $bottom - $top + 1)
    $frames = @()
    foreach ($size in @(16,20,24,32,40,48,64,128,256)) {
        # Center the complete original silhouette (including its shadow) inside equal transparent margins.
        $contentSize = [int][Math]::Round($size * 0.875)
        if (($size - $contentSize) % 2 -ne 0) { $contentSize-- }
        $offset = [int](($size - $contentSize) / 2)
        $bitmap = [Drawing.Bitmap]::new($size, $size, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.Clear([Drawing.Color]::Transparent)
            $graphics.CompositingMode = [Drawing.Drawing2D.CompositingMode]::SourceCopy
            $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $graphics.DrawImage($iconSource, [Drawing.Rectangle]::new($offset, $offset, $contentSize, $contentSize), $sourceRect, [Drawing.GraphicsUnit]::Pixel)
            $framePath = Join-Path $FrameDirectory ('icon-' + $size + '.png')
            $bitmap.Save($framePath, [Drawing.Imaging.ImageFormat]::Png)
            $frames += [pscustomobject]@{ Size = $size; Bytes = [IO.File]::ReadAllBytes($framePath) }
            if ($size -eq 256) { $bitmap.Save((Join-Path $AssetsDirectory 'Compositor.png'), [Drawing.Imaging.ImageFormat]::Png) }
        } finally { $graphics.Dispose(); $bitmap.Dispose() }
    }
    $buffer = [IO.MemoryStream]::new(); $writer = [IO.BinaryWriter]::new($buffer)
    try {
        $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$frames.Count)
        $dataOffset = 6 + 16 * $frames.Count
        foreach ($frame in $frames) {
            $sizeByte = if ($frame.Size -eq 256) { 0 } else { $frame.Size }
            $writer.Write([byte]$sizeByte); $writer.Write([byte]$sizeByte); $writer.Write([byte]0); $writer.Write([byte]0)
            $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$frame.Bytes.Length); $writer.Write([uint32]$dataOffset)
            $dataOffset += $frame.Bytes.Length
        }
        foreach ($frame in $frames) { $writer.Write([byte[]]$frame.Bytes) }
        [IO.File]::WriteAllBytes((Join-Path $AssetsDirectory 'Compositor.ico'), $buffer.ToArray())
    } finally { $writer.Dispose(); $buffer.Dispose() }
    $sourceHash = (Get-FileHash -LiteralPath $iconSourcePath -Algorithm SHA256).Hash
    @"
Artwork: original Compositor Mac icon, commit af30c45c10b1cddc2b9fe8401c137f250d50abb6.
Source SHA256: $sourceHash
Windows packaging: the full alpha silhouette is centered with symmetric margins at 16, 20, 24, 32, 40, 48, 64, 128 and 256 pixels.
The gradient and rounded-square artwork are retained. Windows icon canvases are adapted, not byte-identical Mac PNGs.
Rebuild: windows/tools/PrepareAppIcon.ps1; requires System.Drawing on Windows and a scratch frame directory.
The repository MIT license accompanies this distribution.
"@ | Set-Content -LiteralPath (Join-Path $AssetsDirectory 'ICON-SOURCE.md') -Encoding utf8
    [pscustomobject]@{ Source = $iconSourcePath; SourceBounds = $sourceRect.ToString(); Sizes = $frames.Size } | ConvertTo-Json
} finally { $iconSource.Dispose() }
