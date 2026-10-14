# Builds Dynamic Island and publishes it as a GitHub release that the in-app updater picks up.
#
#   powershell -ExecutionPolicy Bypass -File tools/publish-release.ps1 -Version 1.1.0
#   powershell -ExecutionPolicy Bypass -File tools/publish-release.ps1 -Version 1.1.0 -NotesFile notes.md
#
# Steps: set the version in both projects; for x64 and ARM64 publish, sign (if set up, see below), zip as
# DI4W-v<version>-win-<arch>.zip and build the installer DI4W-v<version>-win-<arch>.msi (see installer/); write the
# winget manifests; commit and push the version change; create the release "v<version>" with all four attached;
# then submit the new version to winget if the package is already listed there. Installed apps see the release
# on their next update check. Needs the GitHub CLI, logged in (gh auth login).
#
# Code signing (optional; without it Windows SmartScreen warns new users). Either:
#   -CertThumbprint <thumbprint>        a code-signing certificate in your certificate store (Cert:\CurrentUser\My)
#   -TrustedSigningMetadata <file.json> Azure Trusted Signing, with -TrustedSigningDlib <Azure.CodeSigning.Dlib.dll>
# The same can be set once with the environment variables DI4W_SIGN_THUMBPRINT, or DI4W_TRUSTED_SIGNING_METADATA
# and DI4W_TRUSTED_SIGNING_DLIB. See "Code signing" in the README.
param(
    [Parameter(Mandatory)][string]$Version,
    [string]$NotesFile,
    [switch]$Draft,
    [switch]$NoArm64,
    [switch]$NoWinget,
    [string]$CertThumbprint = $env:DI4W_SIGN_THUMBPRINT,
    [string]$TrustedSigningMetadata = $env:DI4W_TRUSTED_SIGNING_METADATA,
    [string]$TrustedSigningDlib = $env:DI4W_TRUSTED_SIGNING_DLIB
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

# ───────────── signing ─────────────

$signing = if ($TrustedSigningMetadata) { 'trusted' } elseif ($CertThumbprint) { 'cert' } else { $null }
$signtool = $null
if ($signing) {
    $signtool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe" -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
    if (-not $signtool) { throw "signtool.exe not found. Install the Windows SDK (it comes with Visual Studio's C++ workload)." }
    if ($signing -eq 'trusted' -and -not (Test-Path $TrustedSigningDlib)) { throw "Trusted Signing needs -TrustedSigningDlib (Azure.CodeSigning.Dlib.dll)." }
    Write-Host "Signing with $(if ($signing -eq 'trusted') { 'Azure Trusted Signing' } else { "certificate $CertThumbprint" })"
} else {
    Write-Warning "Not signing: Windows SmartScreen will warn people who download this release. See 'Code signing' in the README."
}

function Sign-Files([string[]]$files) {
    if (-not $signing) { return }
    $common = @('sign', '/fd', 'SHA256', '/td', 'SHA256', '/d', 'Dynamic Island for Windows')
    $how = if ($signing -eq 'trusted') {
        @('/tr', 'http://timestamp.acs.microsoft.com', '/dlib', $TrustedSigningDlib, '/dmdf', $TrustedSigningMetadata)
    } else {
        @('/tr', 'http://timestamp.digicert.com', '/sha1', $CertThumbprint)
    }
    & $signtool @common @how @files
    if ($LASTEXITCODE -ne 0) { throw "Signing failed: $($files -join ', ')" }
}

# ───────────── 1. version ─────────────

# In both projects (the updater compares it with the release tag).
foreach ($project in 'src\DynamicIsland\DynamicIsland.csproj', 'src\DynamicIsland.Settings\DynamicIsland.Settings.csproj') {
    $path = Join-Path $root $project
    $xml = [IO.File]::ReadAllText($path)
    $xml = [regex]::Replace($xml, '<Version>[^<]*</Version>', "<Version>$Version</Version>")
    [IO.File]::WriteAllText($path, $xml)
}

# ───────────── 2. build and package, per architecture ─────────────

$out = Join-Path $root 'artifacts\release'
if (Test-Path $out) { Remove-Item -LiteralPath $out -Recurse -Force -ErrorAction Stop }
$architectures = if ($NoArm64) { @('x64') } else { @('x64', 'arm64') }
$assets = @{}

foreach ($arch in $architectures) {
    # The x64 folder keeps its old name (DI4W), which the zip has always contained.
    $folder = if ($arch -eq 'x64') { 'DI4W' } else { "DI4W-$arch" }
    $app = Join-Path $out $folder
    dotnet publish src/DynamicIsland -c Release -r "win-$arch" -o $app
    if ($LASTEXITCODE -ne 0) { throw "Build failed ($arch)." }
    Get-ChildItem $app -Recurse -Filter *.pdb | Remove-Item -Force -ErrorAction Stop

    # Our own files; the rest (Microsoft's) is signed already.
    Sign-Files @((Join-Path $app 'DynamicIsland.exe'), (Join-Path $app 'DynamicIsland.dll'),
                 (Join-Path $app 'Settings\DynamicIsland.Settings.exe'), (Join-Path $app 'Settings\DynamicIsland.Settings.dll'))

    $zip = Join-Path $out "DI4W-$tag-win-$arch.zip"
    # The zip always contains a "DI4W" folder, whatever the architecture.
    if ($folder -ne 'DI4W') {
        $staging = Join-Path $out "zip-$arch"
        New-Item -ItemType Directory -Force $staging | Out-Null
        Copy-Item $app (Join-Path $staging 'DI4W') -Recurse
        Compress-Archive -Path (Join-Path $staging 'DI4W') -DestinationPath $zip -CompressionLevel Optimal -ErrorAction Stop
        Remove-Item -LiteralPath $staging -Recurse -Force
    } else {
        Compress-Archive -Path $app -DestinationPath $zip -CompressionLevel Optimal -ErrorAction Stop
    }
    Write-Host ("Package: {0} ({1:N1} MB)" -f $zip, ((Get-Item $zip).Length / 1MB))

    # The installer, built from the same files.
    # --no-incremental: the installer's name changes per version, which an incremental build doesn't notice.
    dotnet build installer --no-incremental -c Release -p:AppVersion=$Version "-p:AppDir=$app" -p:Arch=$arch -o $out
    if ($LASTEXITCODE -ne 0) { throw "Building the installer failed ($arch)." }
    $msi = Join-Path $out "DI4W-$tag-win-$arch.msi"
    if (-not (Test-Path $msi)) { throw "Installer not found at $msi." }
    Sign-Files @($msi)
    Write-Host ("Installer: {0} ({1:N1} MB)" -f $msi, ((Get-Item $msi).Length / 1MB))
    $assets[$arch] = @($msi, $zip)
}

# winget manifests for this release (both architectures in one installer manifest).
$manifestArgs = @('-Version', $Version, '-Msi', $assets['x64'][0], '-OutDir', (Join-Path $out 'winget'))
if ($assets['arm64']) { $manifestArgs += @('-MsiArm64', $assets['arm64'][0]) }
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'winget-manifest.ps1') @manifestArgs
if ($LASTEXITCODE -ne 0) { throw "Writing the winget manifests failed." }

# ───────────── 3. commit the version change, so the tag points at the code that was built ─────────────

if (git status --porcelain) {
    git add src/DynamicIsland/DynamicIsland.csproj src/DynamicIsland.Settings/DynamicIsland.Settings.csproj
    git commit -m "Release $tag"
    if ($LASTEXITCODE -ne 0) { throw "git commit failed." }
}
git push origin HEAD
if ($LASTEXITCODE -ne 0) { throw "git push failed." }

# ───────────── 4. the release ─────────────

# GitHub records each file's SHA-256, which the updater checks after downloading.
$notesArgs = if ($NotesFile) { @('--notes-file', $NotesFile) } else { @('--generate-notes') }
$draftArgs = if ($Draft) { @('--draft') } else { @() }
$files = $architectures | ForEach-Object { $assets[$_] }
gh release create $tag @files --repo $repo --target (git rev-parse HEAD) --title "Dynamic Island for Windows $tag" @notesArgs @draftArgs
if ($LASTEXITCODE -ne 0) { throw "Creating the release failed." }
Write-Host "Released $tag. Installed apps will offer it on their next update check."

# ───────────── 5. winget ─────────────

# Only once the package is listed: before that, the first submission's pull request is still being reviewed,
# and a second "new package" request would compete with it.
if ($NoWinget -or $Draft) { return }
gh api "repos/microsoft/winget-pkgs/contents/manifests/a/aangeraakt/DynamicIsland" *> $null
if ($LASTEXITCODE -ne 0) {
    Write-Host "Not submitting to winget yet: the package isn't listed there. Once it is, run: tools/winget-submit.ps1 -Version $Version"
    return
}
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'winget-submit.ps1') -Version $Version
if ($LASTEXITCODE -ne 0) { Write-Warning "The winget submission failed; the release itself is done. Retry with: tools/winget-submit.ps1 -Version $Version" }
