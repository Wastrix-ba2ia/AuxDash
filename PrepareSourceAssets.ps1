Add-Type -AssemblyName System.Drawing
$assetDirectory=Join-Path $PSScriptRoot 'assets'
New-Item -ItemType Directory -Force $assetDirectory | Out-Null
if (!(Test-Path "$assetDirectory/reference.png")) {
    $bitmap=New-Object System.Drawing.Bitmap 1110,314
    $graphics=[Drawing.Graphics]::FromImage($bitmap)
    $graphics.Clear([Drawing.Color]::FromArgb(24,28,40))
    $graphics.Dispose()
    $bitmap.Save("$assetDirectory/reference.png")
    $bitmap.Dispose()
}
if (!(Test-Path "$assetDirectory/robot-app.ico")) {
    $stream=[IO.File]::Create("$assetDirectory/robot-app.ico")
    try { [Drawing.SystemIcons]::Application.Save($stream) } finally { $stream.Dispose() }
}
