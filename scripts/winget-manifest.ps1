# Writes the winget manifests for a published release, ready for a pull request to microsoft/winget-pkgs.
# Usage:  powershell -File scripts/winget-manifest.ps1 -Version 0.9.0 [-Out winget]
# Then copy winget\manifests\k\keyldev\Rift\<version> into a fork of winget-pkgs, check it with
# `winget validate <folder>` and `winget install --manifest <folder>`, and open the pull request.
# Later versions can also go through `wingetcreate update keyldev.Rift -u <setup url> -v <version> --submit`.
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$Out = "winget"
)

$ErrorActionPreference = "Stop"
$id = "keyldev.Rift"
$url = "https://github.com/keyldev/rift/releases/download/v$Version/Rift-win-Setup.exe"
$dir = Join-Path $Out "manifests\k\keyldev\Rift\$Version"
New-Item -ItemType Directory -Force $dir | Out-Null

# The hash has to be of the exact file on the release, so take it from there.
$setup = Join-Path ([System.IO.Path]::GetTempPath()) "Rift-$Version-Setup.exe"
Invoke-WebRequest $url -OutFile $setup -UseBasicParsing
$sha = (Get-FileHash $setup -Algorithm SHA256).Hash
Remove-Item $setup

$schema = "1.9.0"
$utf8 = New-Object System.Text.UTF8Encoding $false

[System.IO.File]::WriteAllText((Join-Path $dir "$id.yaml"), @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.version.$schema.schema.json
PackageIdentifier: $id
PackageVersion: $Version
DefaultLocale: en-US
ManifestType: version
ManifestVersion: $schema
"@, $utf8)

# Velopack's setup installs per user into %LocalAppData%\Rift and registers the uninstaller under the id "Rift".
[System.IO.File]::WriteAllText((Join-Path $dir "$id.installer.yaml"), @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.installer.$schema.schema.json
PackageIdentifier: $id
PackageVersion: $Version
InstallerType: exe
Scope: user
InstallModes:
- interactive
- silent
- silentWithProgress
InstallerSwitches:
  Silent: --silent
  SilentWithProgress: --silent
UpgradeBehavior: install
ProductCode: Rift
Installers:
- Architecture: x64
  InstallerUrl: $url
  InstallerSha256: $sha
ManifestType: installer
ManifestVersion: $schema
"@, $utf8)

[System.IO.File]::WriteAllText((Join-Path $dir "$id.locale.en-US.yaml"), @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.defaultLocale.$schema.schema.json
PackageIdentifier: $id
PackageVersion: $Version
PackageLocale: en-US
Publisher: Vasiliy Zalatov
PublisherUrl: https://github.com/keyldev
PackageName: Rift
PackageUrl: https://github.com/keyldev/rift
License: MIT
LicenseUrl: https://github.com/keyldev/rift/blob/main/LICENSE
ShortDescription: A fast file manager for Windows with tabs and side-by-side panes.
Description: Rift is a file manager for Windows with any number of panes and tabs, copy and move between panes, Explorer's own context menu, previews, recursive search and a command palette.
Tags:
- file-manager
- explorer
- tabs
- dual-pane
ReleaseNotesUrl: https://github.com/keyldev/rift/releases/tag/v$Version
ManifestType: defaultLocale
ManifestVersion: $schema
"@, $utf8)

"Wrote $dir (installer sha256 $sha)"
