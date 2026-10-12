# Submits a release to the Windows Package Manager (winget) as a pull request to microsoft/winget-pkgs.
#
#   powershell -ExecutionPolicy Bypass -File tools/winget-submit.ps1 -Version 1.4.0
#
# Run it after publish-release.ps1, once the release (and its .msi) is public. It:
#   1. checks the manifests with "winget validate",
#   2. forks microsoft/winget-pkgs to your GitHub account (once),
#   3. pushes a branch with the manifests to your fork (sparse clone, so the huge repo isn't downloaded),
#   4. opens the pull request. Microsoft's bots then test the installer; a person merges it, usually within days.
# After that: winget install aangeraakt.DynamicIsland
param(
    [Parameter(Mandatory)][string]$Version,
    [string]$Manifests
)
$ErrorActionPreference = 'Continue' # native git/gh write progress to stderr; check exit codes instead
$root = Split-Path $PSScriptRoot -Parent
$id = 'aangeraakt.DynamicIsland'
$relative = "manifests/a/aangeraakt/DynamicIsland/$Version"
if (-not $Manifests) { $Manifests = Join-Path $root "artifacts\release\winget\$($relative.Replace('/', '\'))" }
if (-not (Test-Path $Manifests)) { throw "No manifests at $Manifests. Run publish-release.ps1 (or winget-manifest.ps1) first." }

winget validate --manifest $Manifests
if ($LASTEXITCODE -ne 0) { throw "The manifests don't validate." }

$user = gh api user --jq .login
if ($LASTEXITCODE -ne 0 -or -not $user) { throw "The GitHub CLI isn't logged in. Run: gh auth login" }

# The release must be downloadable, or Microsoft's validation fails.
$url = "https://github.com/aangeraakt/DI4W/releases/download/v$Version/DI4W-v$Version-win-x64.msi"
try { Invoke-WebRequest -Method Head -Uri $url -UseBasicParsing | Out-Null } catch { throw "The installer isn't downloadable yet: $url" }

gh repo fork microsoft/winget-pkgs --clone=false 2>$null | Out-Null
$work = Join-Path ([IO.Path]::GetTempPath()) "winget-pkgs-$([guid]::NewGuid().ToString('N').Substring(0, 8))"
git clone --filter=blob:none --no-checkout --depth 1 "https://github.com/$user/winget-pkgs.git" $work
if ($LASTEXITCODE -ne 0) { throw "Cloning your fork failed." }
Push-Location $work
try {
    # Commit as the GitHub account with its private no-reply address: the pull request is public.
    $userId = gh api user --jq .id
    git config user.name $user
    git config user.email "$userId+$user@users.noreply.github.com"
    git sparse-checkout set --no-cone "/manifests/a/aangeraakt/"
    git remote add upstream https://github.com/microsoft/winget-pkgs.git
    git fetch --depth 1 upstream master
    $branch = "aangeraakt.DynamicIsland-$Version"
    git checkout -b $branch upstream/master
    if ($LASTEXITCODE -ne 0) { throw "Creating the branch failed." }

    $isNew = -not (git ls-tree -d --name-only upstream/master "manifests/a/aangeraakt/DynamicIsland")
    New-Item -ItemType Directory -Force $relative | Out-Null
    Copy-Item (Join-Path $Manifests '*.yaml') $relative
    git add $relative
    $title = if ($isNew) { "New package: $id version $Version" } else { "New version: $id version $Version" }
    git commit -m $title
    if ($LASTEXITCODE -ne 0) { throw "Committing the manifests failed." }
    # Force: a branch left by an earlier, failed attempt is replaced (it's only ever used for this pull request).
    git push --force origin $branch
    if ($LASTEXITCODE -ne 0) { throw "Pushing to your fork failed." }

    gh pr create --repo microsoft/winget-pkgs --head "${user}:$branch" --title $title `
        --body "Dynamic Island for Windows $Version. Release: https://github.com/aangeraakt/DI4W/releases/tag/v$Version"
    if ($LASTEXITCODE -ne 0) { throw "Opening the pull request failed." }
} finally {
    Pop-Location
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}
