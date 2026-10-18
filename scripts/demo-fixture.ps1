# Creates a folder tree with mock files for screenshots and manual testing.
# Usage:  powershell -File scripts/demo-fixture.ps1 [-Root C:\RiftDemo] [-Remove]
# Then:   dotnet run --project src/Rift.App -- --left C:\RiftDemo\Downloads --left C:\RiftDemo\Projects\rift `
#                                              --right C:\RiftDemo\Backup --right C:\RiftDemo\Media
param(
    [string]$Root = "C:\RiftDemo",
    [switch]$Remove
)

$ErrorActionPreference = "Stop"

if ($Remove) {
    if (Test-Path $Root) { Remove-Item $Root -Recurse -Force }
    return
}

function Add-DemoDir([string]$Path, [string]$Date) {
    $d = New-Item -ItemType Directory -Force -Path (Join-Path $Root $Path)
    $d.LastWriteTime = [datetime]::ParseExact($Date, "dd.MM.yyyy HH:mm", $null)
}

function Add-DemoFile([string]$Path, [long]$Bytes, [string]$Date) {
    $full = Join-Path $Root $Path
    New-Item -ItemType Directory -Force -Path (Split-Path $full) | Out-Null
    $fs = [System.IO.File]::Create($full)
    try { $fs.SetLength($Bytes) } finally { $fs.Dispose() }
    (Get-Item $full).LastWriteTime = [datetime]::ParseExact($Date, "dd.MM.yyyy HH:mm", $null)
}

$KB = 1KB; $MB = 1MB

Add-DemoFile "Downloads\avalonia-docs.pdf"        (4.2 * $MB) "16.10.2026 11:20"
Add-DemoFile "Downloads\wallpaper-4k.png"         (8.7 * $MB) "15.10.2026 22:41"
Add-DemoFile "Downloads\demo-reel.mp4"            (24 * $MB)  "12.10.2026 18:05"
Add-DemoFile "Downloads\dotnet-sdk-10.0.300.exe"  (18 * $MB)  "11.10.2026 09:12"
Add-DemoFile "Downloads\fonts.zip"                (2.3 * $MB) "09.10.2026 14:30"
Add-DemoFile "Downloads\notes.md"                 (4.1 * $KB) "18.10.2026 10:02"
Add-DemoFile "Downloads\track-01.flac"            (9.6 * $MB) "03.10.2026 20:47"
Add-DemoFile "Downloads\invoice-2026-10.pdf"      (182 * $KB) "01.10.2026 08:15"
Add-DemoFile "Downloads\palette.json"             (2.4 * $KB) "17.10.2026 16:33"
Add-DemoFile "Downloads\screenshots\rift-v0.png"  (1.2 * $MB) "17.10.2026 19:10"
Add-DemoFile "Downloads\screenshots\rift-v1.png"  (1.4 * $MB) "18.10.2026 12:44"
Add-DemoFile "Downloads\installers\7z2501-x64.exe" (1.6 * $MB) "14.10.2026 13:00"
Add-DemoDir  "Downloads\installers"  "14.10.2026 13:00"
Add-DemoDir  "Downloads\screenshots" "18.10.2026 12:44"

Add-DemoFile "Projects\rift\Rift.slnx"              (612)        "18.10.2026 17:02"
Add-DemoFile "Projects\rift\Directory.Build.props"  (284)        "18.10.2026 17:02"
Add-DemoFile "Projects\rift\README.md"              (3.4 * $KB)  "18.10.2026 17:40"
Add-DemoFile "Projects\rift\LICENSE"                (1.1 * $KB)  "18.10.2026 15:12"
Add-DemoFile "Projects\rift\src\Rift.App\App.axaml" (1.2 * $KB)  "18.10.2026 17:30"
Add-DemoFile "Projects\rift\tests\CoreTests.cs"     (4.8 * $KB)  "18.10.2026 17:35"
Add-DemoFile "Projects\rift\docs\logo.png"          (6.2 * $KB)  "18.10.2026 17:38"
Add-DemoDir  "Projects\rift\src"   "18.10.2026 17:30"
Add-DemoDir  "Projects\rift\tests" "18.10.2026 17:35"
Add-DemoDir  "Projects\rift\docs"  "18.10.2026 17:38"

Add-DemoFile "Backup\2026-09\settings.json"   (12 * $KB)  "30.09.2026 23:00"
Add-DemoFile "Backup\2026-10\settings.json"   (12 * $KB)  "15.10.2026 23:00"
Add-DemoFile "Backup\photos\IMG_0412.jpg"     (3.1 * $MB) "01.08.2026 12:00"
Add-DemoFile "Backup\system-image.zip"        (31 * $MB)  "01.10.2026 03:00"
Add-DemoFile "Backup\backup-log.txt"          (2.0 * $KB) "15.10.2026 23:01"
Add-DemoDir  "Backup\2026-09" "30.09.2026 23:00"
Add-DemoDir  "Backup\2026-10" "15.10.2026 23:00"
Add-DemoDir  "Backup\photos"  "01.08.2026 12:00"

Add-DemoFile "Media\movies\trailer.mkv"  (15 * $MB)  "10.10.2026 21:30"
Add-DemoFile "Media\music\album.flac"    (11 * $MB)  "05.09.2026 18:00"
Add-DemoFile "Media\cover.jpg"           (640 * $KB) "05.09.2026 18:02"
# Real pictures, so the thumbnail view and the preview pane have something to show.
Add-Type -AssemblyName System.Drawing
function Add-DemoPhoto([string]$Path, [int]$W, [int]$H, [string]$From, [string]$To, [string]$Date) {
    $full = Join-Path $Root $Path
    New-Item -ItemType Directory -Force -Path (Split-Path $full) | Out-Null
    $bmp = New-Object System.Drawing.Bitmap $W, $H
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $rect = New-Object System.Drawing.Rectangle 0, 0, $W, $H
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.ColorTranslator]::FromHtml($From)), ([System.Drawing.ColorTranslator]::FromHtml($To)), 90
    $g.FillRectangle($brush, $rect)
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(200, 255, 255, 255)), ([Math]::Max(3, $W / 250))
    for ($i = 0; $i -lt 5; $i++) {
        $r = [int]([Math]::Min($W, $H) * (0.08 + 0.04 * $i))
        $g.DrawEllipse($pen, [int]($W * (0.2 + 0.15 * $i)) - $r, [int]($H * (0.3 + 0.1 * [Math]::Sin($i))) - $r, 2 * $r, 2 * $r)
    }
    $g.Dispose()
    $bmp.Save($full, [System.Drawing.Imaging.ImageFormat]::Jpeg)
    $bmp.Dispose()
    (Get-Item $full).LastWriteTime = [datetime]::ParseExact($Date, "dd.MM.yyyy HH:mm", $null)
}

Add-DemoPhoto "Media\photos\coast.jpg"    1600 1000 "#FF7850" "#50289F" "02.10.2026 19:40"
Add-DemoPhoto "Media\photos\lagoon.jpg"   900  1200 "#28B4DC" "#0A285A" "03.10.2026 08:12"
Add-DemoPhoto "Media\photos\dunes.jpg"    1600 1000 "#FAC83C" "#C83C28" "04.10.2026 17:05"
Add-DemoPhoto "Media\photos\forest.jpg"   900  1200 "#5AC878" "#14463C" "06.10.2026 10:30"
Add-DemoPhoto "Media\photos\bloom.jpg"    1600 1000 "#E65AA0" "#3C1E6E" "07.10.2026 21:15"
Add-DemoPhoto "Media\photos\night.jpg"    900  1200 "#788CFF" "#14143C" "09.10.2026 23:50"
Add-DemoDir  "Media\photos" "09.10.2026 23:50"

Add-DemoDir  "Media\movies" "10.10.2026 21:30"
Add-DemoDir  "Media\music"  "05.09.2026 18:00"
Add-DemoDir  "Media\series" "22.08.2026 19:45"

Write-Host "Demo files created in $Root"
