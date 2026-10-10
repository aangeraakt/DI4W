# Builds Dynamic Island and publishes it as a GitHub release that the in-app updater picks up.
#
#   powershell -ExecutionPolicy Bypass -File tools/publish-release.ps1 -Version 1.1.0
#   powershell -ExecutionPolicy Bypass -File tools/publish-release.ps1 -Version 1.1.0 -NotesFile notes.md
#
# Steps: set the version in both projects, publish, zip as DI4W-v<version>-win-x64.zip, commit and push the
# version change, then create the release "v<version>" with the zip attached. Installed apps see it on their
# next update check. Needs the GitHub CLI, logged in (gh auth login).
param(
    [Parameter(Mandatory)][string]$Version,
    [string]$NotesFile,
    [switch]$Draft
)
$ErrorActionPreference = 'Stop'
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
if (Test-Path $out) { Remove-Item -LiteralPath $out -Recurse -Force }
dotnet publish src/DynamicIsland -c Release -o (Join-Path $out 'DI4W')
if ($LASTEXITCODE -ne 0) { throw "Build failed." }
Get-ChildItem (Join-Path $out 'DI4W') -Recurse -Filter *.pdb | Remove-Item -Force
$zip = Join-Path $out "DI4W-$tag-win-x64.zip"
Compress-Archive -Path (Join-Path $out 'DI4W') -DestinationPath $zip -CompressionLevel Optimal
Write-Host ("Package: {0} ({1:N1} MB)" -f $zip, ((Get-Item $zip).Length / 1MB))

# 3. Commit the version change so the tag points at the code that was built.
if (git status --porcelain) {
    git add src/DynamicIsland/DynamicIsland.csproj src/DynamicIsland.Settings/DynamicIsland.Settings.csproj
    git commit -m "Release $tag"
}
git push origin HEAD
if ($LASTEXITCODE -ne 0) { throw "git push failed." }

# 4. The release. GitHub records the zip's SHA-256, which the updater checks after downloading.
$notesArgs = if ($NotesFile) { @('--notes-file', $NotesFile) } else { @('--generate-notes') }
$draftArgs = if ($Draft) { @('--draft') } else { @() }
gh release create $tag $zip --repo $repo --target (git rev-parse HEAD) --title "Dynamic Island for Windows $tag" @notesArgs @draftArgs
if ($LASTEXITCODE -ne 0) { throw "Creating the release failed." }
Write-Host "Released $tag. Installed apps will offer it on their next update check."
