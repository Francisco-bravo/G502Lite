$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, WindowsBase
$frames = @()
foreach ($dimension in @(16, 32, 48, 256)) {
    $visual = New-Object System.Windows.Media.DrawingVisual
    $drawing = $visual.RenderOpen()
    $drawing.PushTransform((New-Object System.Windows.Media.ScaleTransform ($dimension / 64.0), ($dimension / 64.0)))
    $background = [System.Windows.Media.BrushConverter]::new().ConvertFromString('#0B1020')
    $body = [System.Windows.Media.BrushConverter]::new().ConvertFromString('#183147')
    $cyan = [System.Windows.Media.BrushConverter]::new().ConvertFromString('#61E8F0')
    $drawing.DrawRoundedRectangle($background, $null, [System.Windows.Rect]::new(0, 0, 64, 64), 13, 13)
    $drawing.DrawRoundedRectangle($body, [System.Windows.Media.Pen]::new($cyan, 3), [System.Windows.Rect]::new(17, 7, 30, 50), 15, 15)
    $drawing.DrawLine([System.Windows.Media.Pen]::new($cyan, 2), [System.Windows.Point]::new(32, 9), [System.Windows.Point]::new(32, 30))
    $drawing.DrawRoundedRectangle([System.Windows.Media.Brushes]::White, $null, [System.Windows.Rect]::new(29, 15, 6, 10), 3, 3)
    $drawing.Pop()
    $drawing.Close()
    $bitmap = [System.Windows.Media.Imaging.RenderTargetBitmap]::new($dimension, $dimension, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = [System.Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $memory = [System.IO.MemoryStream]::new()
    $encoder.Save($memory)
    $frames += [pscustomobject]@{ Size = $dimension; Bytes = $memory.ToArray() }
    $memory.Dispose()
}
$output = [System.IO.File]::Create((Join-Path $PSScriptRoot 'Assets\G502Lite.ico'))
$writer = [System.IO.BinaryWriter]::new($output)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$frames.Count)
    $offset = 6 + 16 * $frames.Count
    foreach ($frame in $frames) {
        $sizeByte = if ($frame.Size -eq 256) { 0 } else { $frame.Size }
        $writer.Write([byte]$sizeByte); $writer.Write([byte]$sizeByte)
        $writer.Write([byte]0); $writer.Write([byte]0); $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$frame.Bytes.Length); $writer.Write([uint32]$offset)
        $offset += $frame.Bytes.Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame.Bytes) }
} finally { $writer.Dispose(); $output.Dispose() }
