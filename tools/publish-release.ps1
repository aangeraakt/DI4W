# Builds Dynamic Island and publishes it as a GitHub release that the in-app updater picks up.
#
#   powershell -ExecutionPolicy Bypass -File tools/publish-release.ps1 -Version 1.1.0
#   powershell -ExecutionPolicy Bypass -File tools/publish-release.ps1 -Version 1.1.0 -NotesFile notes.md
#
# Steps: set the version in both projects, publish, zip as DI4W-v<version>-win-x64.zip, build the installer
# (DI4W-v<version>-win-x64.msi, see installer/), commit and push the
# version change, then create the release "v<version>" with both attached. Installed apps see it on their
# next update check. Needs the GitHub CLI, logged in (gh auth login).
param(
    [Parameter(Mandatory)][string]$Version,
    [string]$NotesFile,
    [switch]$Draft
)
# Not 'Stop': Windows PowerShell 5.1 then treats any stderr output of git/gh/dotnet (progress, "release not found")
# as a fatal error. Native commands are checked through $LASTEXITCODE; cmdlets that must succeed use -ErrorAction Stop.
$ErrorActionPreference = 'Continue'
$repo = 'aangeraakt/DI4W'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version must look like 1.2.3 (got '$Version')." }
$tag = "v$Version"

gh auth status *> $null
if ($LASTEXITCODE -ne 0) { throw "The GitHub CLI isn't logged in. Run: gh auth login" }
gh release view $tag --repo $repo *> $null
if ($LASTEXITCODE -eq 0) { throw "Release $tag already exists." }
if (git status --porcelain) { throw "There are uncommitted changes. Commit or stash them first." }

# 1. Version in both projects (the updater compares it with the release tag).
foreach ($project in 'src\DynamicIsland\DynamicIsland.csproj', 'src\DynamicIsland.Settings\DynamicIsland.Settings.csproj') {
    $path = Join-Path $root $project
    $xml = [IO.File]::ReadAllText($path)
    $xml = [regex]::Replace($xml, '<Version>[^<]*</Version>', "<Version>$Version</Version>")
    [IO.File]::WriteAllText($path, $xml)
}

# 2. Build and package.
$out = Join-Path $root 'artifacts\release'
if (Test-Path $out) { Remove-Item -LiteralPath $out -Recurse -Force -ErrorAction Stop }
dotnet publish src/DynamicIsland -c Release -o (Join-Path $out 'DI4W')
if ($LASTEXITCODE -ne 0) { throw "Build failed." }
Get-ChildItem (Join-Path $out 'DI4W') -Recurse -Filter *.pdb | Remove-Item -Force -ErrorAction Stop
$zip = Join-Path $out "DI4W-$tag-win-x64.zip"
Compress-Archive -Path (Join-Path $out 'DI4W') -DestinationPath $zip -CompressionLevel Optimal -ErrorAction Stop
Write-Host ("Package: {0} ({1:N1} MB)" -f $zip, ((Get-Item $zip).Length / 1MB))

# The installer, built from the same files.
# --no-incremental: the installer's name changes per version, which an incremental build doesn't notice.
dotnet build installer --no-incremental -c Release -p:AppVersion=$Version "-p:AppDir=$(Join-Path $out 'DI4W')" -o $out
if ($LASTEXITCODE -ne 0) { throw "Building the installer failed." }
$msi = Join-Path $out "DI4W-$tag-win-x64.msi"
if (-not (Test-Path $msi)) { throw "Installer not found at $msi." }
Write-Host ("Installer: {0} ({1:N1} MB)" -f $msi, ((Get-Item $msi).Length / 1MB))

# winget manifests for this release (submit them with tools/winget-submit.ps1 once the release is live).
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'winget-manifest.ps1') -Version $Version -Msi $msi -OutDir (Join-Path $out 'winget')
if ($LASTEXITCODE -ne 0) { throw "Writing the winget manifests failed." }

# 3. Commit the version change so the tag points at the code that was built.
if (git status --porcelain) {
    git add src/DynamicIsland/DynamicIsland.csproj src/DynamicIsland.Settings/DynamicIsland.Settings.csproj
    git commit -m "Release $tag"
    if ($LASTEXITCODE -ne 0) { throw "git commit failed." }
}
git push origin HEAD
if ($LASTEXITCODE -ne 0) { throw "git push failed." }

# 4. The release. GitHub records each file's SHA-256, which the updater checks after downloading.
$notesArgs = if ($NotesFile) { @('--notes-file', $NotesFile) } else { @('--generate-notes') }
$draftArgs = if ($Draft) { @('--draft') } else { @() }
gh release create $tag $msi $zip --repo $repo --target (git rev-parse HEAD) --title "Dynamic Island for Windows $tag" @notesArgs @draftArgs
if ($LASTEXITCODE -ne 0) { throw "Creating the release failed." }
Write-Host "Released $tag. Installed apps will offer it on their next update check."
