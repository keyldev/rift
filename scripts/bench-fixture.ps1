# Creates a folder with 100,000 empty files (and some subfolders) to check that big folders open without stutter.
# Usage:  powershell -File scripts/bench-fixture.ps1 [-Root $env:TEMP\Rift100k] [-Files 100000] [-Remove]
# Then:   set RIFT_PERF=1 and open the folder; timings go to %TEMP%\rift-perf.log
param(
    [string]$Root = (Join-Path $env:TEMP "Rift100k"),
    [int]$Files = 100000,
    [int]$Folders = 500,
    [switch]$Remove
)

$ErrorActionPreference = "Stop"

if ($Remove) {
    if (Test-Path $Root) { [System.IO.Directory]::Delete($Root, $true) }
    return
}

[System.IO.Directory]::CreateDirectory($Root) | Out-Null
$extensions = "jpg", "png", "txt", "cs", "pdf", "mp4", "zip", "docx", "mp3", "json"
$prefixes = "IMG_", "report ", "Track ", "build-", "photo", "data_", "Note ", "scan"
$start = [datetime]::new(2024, 1, 1)

for ($i = 0; $i -lt $Folders; $i++) {
    [System.IO.Directory]::CreateDirectory((Join-Path $Root ("Folder {0}" -f $i))) | Out-Null
}
for ($i = 0; $i -lt $Files; $i++) {
    $name = "{0}{1}.{2}" -f $prefixes[$i % $prefixes.Length], $i, $extensions[($i * 7) % $extensions.Length]
    $path = Join-Path $Root $name
    $fs = [System.IO.File]::Create($path)
    try { $fs.SetLength($i % 5000) } finally { $fs.Dispose() }
    [System.IO.File]::SetLastWriteTime($path, $start.AddMinutes($i * 3))
}
"Created $Files files and $Folders folders in $Root"
