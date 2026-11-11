$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$images = @()
foreach ($size in @(16, 32, 48, 64, 128, 256)) {
    $bitmap = New-Object Drawing.Bitmap $size, $size
    $g = [Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.ScaleTransform($size / 32.0, $size / 32.0)
    $g.Clear([Drawing.Color]::FromArgb(15, 35, 38))
    $accent = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(94, 201, 180))
    $pen = New-Object Drawing.Pen ([Drawing.Color]::FromArgb(193, 237, 225)), 2.2
    $pen.StartCap = [Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [Drawing.Drawing2D.LineCap]::Round
    $g.DrawEllipse($pen, 6, 5, 16, 16)
    $g.DrawLine($pen, 20, 19, 27, 26)
    $g.FillRectangle($accent, 10, 9, 3, 3)
    $g.FillRectangle($accent, 15, 9, 3, 3)
    $g.FillRectangle($accent, 10, 14, 3, 3)
    $g.FillRectangle($accent, 15, 14, 3, 3)
    $stream = New-Object IO.MemoryStream
    $bitmap.Save($stream, [Drawing.Imaging.ImageFormat]::Png)
    $images += [pscustomobject]@{ Size=$size; Data=$stream.ToArray() }
    $g.Dispose(); $bitmap.Dispose(); $pen.Dispose(); $accent.Dispose(); $stream.Dispose()
}
$file = [IO.File]::Create((Join-Path $PSScriptRoot '..\src\PixelTrace.Desktop\Assets\pixeltrace.ico'))
$writer = New-Object IO.BinaryWriter $file
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$images.Count)
    $offset = 6 + 16 * $images.Count
    foreach ($entry in $images) {
        $side = if ($entry.Size -eq 256) { 0 } else { $entry.Size }
        $writer.Write([byte]$side); $writer.Write([byte]$side); $writer.Write([uint16]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$entry.Data.Length); $writer.Write([uint32]$offset)
        $offset += $entry.Data.Length
    }
    foreach ($entry in $images) { $writer.Write([byte[]]$entry.Data) }
} finally { $writer.Dispose() }
