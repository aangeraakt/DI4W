# Writes the winget manifests for a release, in the layout of microsoft/winget-pkgs:
#   artifacts/winget/manifests/a/aangeraakt/DynamicIsland/<version>/*.yaml
#
#   powershell -ExecutionPolicy Bypass -File tools/winget-manifest.ps1 -Version 1.4.0 -Msi artifacts/release/DI4W-v1.4.0-win-x64.msi
#   ... -Msi <x64 .msi> -MsiArm64 <arm64 .msi>     (both architectures in one manifest)
#
# The checksums and product codes come from the .msi files themselves; the download links are the release's.
# publish-release.ps1 runs this for every release. To publish to winget, see tools/winget-submit.ps1.
param(
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][string]$Msi,
    [string]$MsiArm64,
    [string]$OutDir
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (-not $OutDir) { $OutDir = Join-Path $root 'artifacts\winget' }
$id = 'aangeraakt.DynamicIsland'
$repo = 'aangeraakt/DI4W'
$upgradeCode = '{E513DFD2-80C8-4289-AB9B-6482251656A9}'
$manifestVersion = '1.10.0'

# The product code from an MSI's Property table (Windows Installer COM, built into Windows).
function Get-ProductCode($path) {
    $installer = New-Object -ComObject WindowsInstaller.Installer
    $db = $installer.GetType().InvokeMember('OpenDatabase', 'InvokeMethod', $null, $installer, @($path, 0))
    $view = $db.GetType().InvokeMember('OpenView', 'InvokeMethod', $null, $db, @("SELECT Value FROM Property WHERE Property='ProductCode'"))
    $view.GetType().InvokeMember('Execute', 'InvokeMethod', $null, $view, $null) | Out-Null
    $record = $view.GetType().InvokeMember('Fetch', 'InvokeMethod', $null, $view, $null)
    $code = $record.GetType().InvokeMember('StringData', 'GetProperty', $null, $record, 1)
    $view.GetType().InvokeMember('Close', 'InvokeMethod', $null, $view, $null) | Out-Null
    [System.Runtime.InteropServices.Marshal]::ReleaseComObject($installer) | Out-Null
    return $code
}

function Get-InstallerEntry($arch, $path) {
    $path = (Resolve-Path $path).Path
    $sha = (Get-FileHash $path -Algorithm SHA256).Hash
    $productCode = Get-ProductCode $path
    $url = "https://github.com/$repo/releases/download/v$Version/DI4W-v$Version-win-$arch.msi"
    return @"
- Architecture: $arch
  InstallerUrl: $url
  InstallerSha256: $sha
  ProductCode: '$productCode'
  AppsAndFeaturesEntries:
  - DisplayName: Dynamic Island for Windows
    Publisher: aangeraakt
    ProductCode: '$productCode'
    UpgradeCode: '$upgradeCode'
"@
}

$installers = Get-InstallerEntry 'x64' $Msi
if ($MsiArm64) { $installers += "`n" + (Get-InstallerEntry 'arm64' $MsiArm64) }

$dir = Join-Path $OutDir "manifests\a\aangeraakt\DynamicIsland\$Version"
New-Item -ItemType Directory -Force $dir | Out-Null
$date = Get-Date -Format 'yyyy-MM-dd'
$utf8 = New-Object Text.UTF8Encoding $false

function Write-Manifest($name, $content) {
    [IO.File]::WriteAllText((Join-Path $dir "$id.$name.yaml"), $content.Replace("`r`n", "`n"), $utf8)
}

Write-Manifest 'installer' @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.installer.$manifestVersion.schema.json
PackageIdentifier: $id
PackageVersion: $Version
MinimumOSVersion: 10.0.19041.0
InstallerType: wix
Scope: user
InstallModes:
- interactive
- silent
- silentWithProgress
UpgradeBehavior: install
Dependencies:
  PackageDependencies:
  - PackageIdentifier: Microsoft.DotNet.DesktopRuntime.8
ReleaseDate: $date
Installers:
$installers
ManifestType: installer
ManifestVersion: $manifestVersion
"@

Write-Manifest 'locale.en-US' @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.defaultLocale.$manifestVersion.schema.json
PackageIdentifier: $id
PackageVersion: $Version
PackageLocale: en-US
Publisher: aangeraakt
PublisherUrl: https://github.com/aangeraakt
PublisherSupportUrl: https://github.com/$repo/issues
PackageName: Dynamic Island for Windows
PackageUrl: https://github.com/$repo
License: MIT
LicenseUrl: https://github.com/$repo/blob/main/LICENSE
ShortDescription: The iPhone Dynamic Island for the Windows desktop.
Description: |-
  A small black pill at the top of your screen that shows what's going on: music with live audio bars,
  Claude Code activity, timers, focus sessions, downloads, calendar reminders, chat messages and calls,
  battery, Bluetooth devices, weather and more. Each add-on can be switched on or off in the settings app.
Moniker: dynamic-island
Tags:
- dynamic-island
- notifications
- spotify
- timer
- widget
ReleaseNotesUrl: https://github.com/$repo/releases/tag/v$Version
ManifestType: defaultLocale
ManifestVersion: $manifestVersion
"@

Write-Manifest 'locale.nl-NL' @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.locale.$manifestVersion.schema.json
PackageIdentifier: $id
PackageVersion: $Version
PackageLocale: nl-NL
ShortDescription: Het Dynamic Island van de iPhone, voor het Windows-bureaublad.
Description: |-
  Een klein zwart eiland bovenaan je scherm dat laat zien wat er gebeurt: muziek met live geluidsbalkjes,
  Claude Code-activiteit, timers, focussessies, downloads, agendaherinneringen, chatberichten en oproepen,
  batterij, Bluetooth-apparaten, het weer en meer. Elke add-on zet je aan of uit in de instellingen-app.
ManifestType: locale
ManifestVersion: $manifestVersion
"@

Write-Manifest 'locale.de-DE' @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.locale.$manifestVersion.schema.json
PackageIdentifier: $id
PackageVersion: $Version
PackageLocale: de-DE
ShortDescription: Die Dynamic Island des iPhone für den Windows-Desktop.
Description: |-
  Eine kleine schwarze Insel oben auf dem Bildschirm, die zeigt, was gerade passiert: Musik mit Live-Pegelbalken,
  Claude-Code-Aktivität, Timer, Fokus-Sitzungen, Downloads, Terminerinnerungen, Chatnachrichten und Anrufe,
  Akku, Bluetooth-Geräte, das Wetter und mehr. Jedes Add-on lässt sich in der Einstellungen-App ein- und ausschalten.
ManifestType: locale
ManifestVersion: $manifestVersion
"@

Write-Manifest 'locale.fr-FR' @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.locale.$manifestVersion.schema.json
PackageIdentifier: $id
PackageVersion: $Version
PackageLocale: fr-FR
ShortDescription: La Dynamic Island de l'iPhone, sur le bureau Windows.
Description: |-
  Une petite pilule noire en haut de l'écran qui montre ce qui se passe : la musique avec des barres audio en direct,
  l'activité de Claude Code, les minuteurs, les sessions de concentration, les téléchargements, les rappels d'agenda,
  les messages et appels, la batterie, les appareils Bluetooth, la météo et plus encore. Chaque module s'active ou se
  désactive dans l'application Paramètres.
ManifestType: locale
ManifestVersion: $manifestVersion
"@

# The version manifest is named after the package itself (no ".version" suffix).
$versionManifest = @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.version.$manifestVersion.schema.json
PackageIdentifier: $id
PackageVersion: $Version
DefaultLocale: en-US
ManifestType: version
ManifestVersion: $manifestVersion
"@
[IO.File]::WriteAllText((Join-Path $dir "$id.yaml"), $versionManifest.Replace("`r`n", "`n"), $utf8)

Write-Host "winget manifests: $dir"
