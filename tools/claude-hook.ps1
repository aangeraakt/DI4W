# Forwards a Claude Code hook event to the Dynamic Island.
# Usage (from ~/.claude/settings.json hooks): powershell -NoProfile -ExecutionPolicy Bypass -File claude-hook.ps1 <start|stop|notify|end>
# Reads the hook's JSON payload from stdin. Never fails the hook: if the island isn't running, it exits quietly.
param([Parameter(Mandatory = $true)][string]$Event)

$ErrorActionPreference = 'SilentlyContinue'
$port = if ($env:DYNAMIC_ISLAND_PORT) { $env:DYNAMIC_ISLAND_PORT } else { 47823 }

try {
    $body = [Console]::In.ReadToEnd()
} catch {
    $body = '{}'
}
if ([string]::IsNullOrWhiteSpace($body)) { $body = '{}' }

try {
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($body)
    Invoke-WebRequest -UseBasicParsing -Method Post -Uri "http://127.0.0.1:$port/claude/$Event" `
        -ContentType 'application/json; charset=utf-8' -Body $bytes -TimeoutSec 2 | Out-Null
} catch { }

exit 0
