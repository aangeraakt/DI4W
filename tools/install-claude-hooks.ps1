# Adds the Dynamic Island hooks to your user-level Claude Code settings (~/.claude/settings.json).
# Safe to run more than once: existing island hooks are replaced, all other settings and hooks are kept.
# A backup is written next to the file first.
param([switch]$Uninstall)

$ErrorActionPreference = 'Stop'
$settingsPath = Join-Path $env:USERPROFILE '.claude\settings.json'
$hookScript = (Resolve-Path (Join-Path $PSScriptRoot 'claude-hook.ps1')).Path
$marker = 'claude-hook.ps1'

New-Item -ItemType Directory -Force (Split-Path $settingsPath) | Out-Null
if (Test-Path $settingsPath) {
    Copy-Item $settingsPath "$settingsPath.bak" -Force
    $raw = Get-Content $settingsPath -Raw
    $settings = if ([string]::IsNullOrWhiteSpace($raw)) { [pscustomobject]@{} } else { $raw | ConvertFrom-Json }
} else {
    $settings = [pscustomobject]@{}
}

if (-not $settings.PSObject.Properties['hooks']) {
    $settings | Add-Member -NotePropertyName hooks -NotePropertyValue ([pscustomobject]@{})
}

$events = [ordered]@{
    UserPromptSubmit = 'start'
    Stop             = 'stop'
    Notification     = 'notify'
    SessionEnd       = 'end'
}

foreach ($hookEvent in $events.Keys) {
    $existing = @()
    if ($settings.hooks.PSObject.Properties[$hookEvent]) {
        # Keep every matcher group that isn't ours.
        $existing = @($settings.hooks.$hookEvent | Where-Object {
            -not ($_.hooks | Where-Object { $_.command -like "*$marker*" })
        })
    }

    if (-not $Uninstall) {
        $command = "powershell -NoProfile -ExecutionPolicy Bypass -File `"$hookScript`" $($events[$hookEvent])"
        $existing += [pscustomobject]@{
            hooks = @([pscustomobject]@{ type = 'command'; command = $command; timeout = 5 })
        }
    }

    if ($settings.hooks.PSObject.Properties[$hookEvent]) {
        $settings.hooks.PSObject.Properties.Remove($hookEvent)
    }
    if ($existing.Count -gt 0) {
        $settings.hooks | Add-Member -NotePropertyName $hookEvent -NotePropertyValue $existing
    }
}

$json = $settings | ConvertTo-Json -Depth 32
[System.IO.File]::WriteAllText($settingsPath, $json, (New-Object System.Text.UTF8Encoding $false))

if ($Uninstall) {
    Write-Host "Removed Dynamic Island hooks from $settingsPath"
} else {
    Write-Host "Installed Dynamic Island hooks into $settingsPath"
    Write-Host "Restart running Claude Code sessions (or run /hooks) for them to take effect."
}
