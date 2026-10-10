# Original vector artwork, rendered at every Windows icon size. No external assets.
Add-Type -AssemblyName PresentationCore, WindowsBase
$assetDirectory = Join-Path (Split-Path -Parent $PSScriptRoot) 'src\mStatusBar\Assets'
New-Item -ItemType Directory -Force -Path $assetDirectory | Out-Null
$frames = @()
foreach ($size in @(16, 24, 32, 48, 64, 128, 256)) {
    $visual = New-Object System.Windows.Media.DrawingVisual
    $drawing = $visual.RenderOpen()
    $drawing.PushTransform((New-Object System.Windows.Media.ScaleTransform ($size / 256.0), ($size / 256.0)))
    $background = [System.Windows.Media.BrushConverter]::new().ConvertFromString('#172532')
    $mint = [System.Windows.Media.BrushConverter]::new().ConvertFromString('#65DAB7')
    $light = [System.Windows.Media.BrushConverter]::new().ConvertFromString('#EAF8F4')
    $drawing.DrawRoundedRectangle($background, $null, [System.Windows.Rect]::new(8, 8, 240, 240), 50, 50)
    $drawing.DrawGeometry($mint, $null, [System.Windows.Media.Geometry]::Parse('M 83,155 L 165,73 Q 173,65 181,73 L 193,85 Q 201,93 193,101 L 111,183 Z'))
    $drawing.DrawGeometry($light, $null, [System.Windows.Media.Geometry]::Parse('M 78,162 L 104,188 L 64,201 Z'))
    $drawing.DrawGeometry($background, $null, [System.Windows.Media.Geometry]::Parse('M 65,195 L 70,186 L 79,195 Z'))
    $drawing.DrawGeometry($light, $null, [System.Windows.Media.Geometry]::Parse('M 174,64 L 187,51 Q 193,45 199,51 L 215,67 Q 221,73 215,79 L 202,92 Z'))
    $drawing.Pop(); $drawing.Close()
    $bitmap = [System.Windows.Media.Imaging.RenderTargetBitmap]::new($size, $size, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = [System.Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $memory = [IO.MemoryStream]::new(); $encoder.Save($memory)
    $frames += ,$memory.ToArray(); $memory.Dispose()
    if ($size -eq 256) {
        [IO.File]::WriteAllBytes((Join-Path $assetDirectory 'mStatusBar.png'), $frames[-1])
    }
}
$file = [IO.File]::Create((Join-Path $assetDirectory 'mStatusBar.ico'))
$writer = [IO.BinaryWriter]::new($file)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$frames.Count)
    $offset = 6 + 16 * $frames.Count; $index = 0
    foreach ($size in @(16, 24, 32, 48, 64, 128, 256)) {
        $dimension = if ($size -eq 256) { 0 } else { $size }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
        $writer.Write([byte]0); $writer.Write([byte]0); $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$frames[$index].Length); $writer.Write([uint32]$offset)
        $offset += $frames[$index].Length; $index++
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame) }
} finally { $writer.Dispose(); $file.Dispose() }
