# Shows a pop-up or progress in Dynamic Island from any script, through the Island API (127.0.0.1 only).
#
#   island.ps1 -Title "Backup done" -Message "312 files" -Icon success -Color green
#   island.ps1 -Progress backup -Title "Backing up" -Value 0.4 -Detail "120 of 312 files"
#   island.ps1 -Progress backup -Done -Title "Backup done"
#
# Icons: info, success, warning, error, download, upload, music, mail, timer, bell, code, home, star, heart, game, sync
# Colors: blue, green, orange, red, purple, teal, yellow, indigo, gray, or "#RRGGBB"
param(
    [string]$Title,
    [string]$Message,
    [string]$Icon,
    [string]$Color,
    [int]$Seconds,
    [string]$Url,
    [switch]$Sticky,
    [string]$Progress,
    [Nullable[double]]$Value,
    [string]$Detail,
    [switch]$Done,
    [int]$Port = $(if ($env:DYNAMIC_ISLAND_PORT) { [int]$env:DYNAMIC_ISLAND_PORT } else { 47823 })
)

$body = @{}
if ($Progress) {
    $path = 'progress'
    $body.id = $Progress
    if ($Done) {
        $body.done = $true
        if ($Title) { $body.message = $Title }
        if ($Message) { $body.detail = $Message }
    } else {
        if ($Title) { $body.title = $Title }
        if ($null -ne $Value) { $body.value = $Value }
        if ($Detail) { $body.detail = $Detail }
        if ($Color) { $body.color = $Color }
    }
} else {
    if (-not $Title) { throw "-Title is required (or use -Progress)." }
    $path = 'alert'
    $body.title = $Title
    if ($Message) { $body.message = $Message }
    if ($Icon) { $body.icon = $Icon }
    if ($Color) { $body.color = $Color }
    if ($Seconds) { $body.seconds = $Seconds }
    if ($Url) { $body.url = $Url }
    if ($Sticky) { $body.sticky = $true }
}

$json = $body | ConvertTo-Json -Compress
Invoke-RestMethod -Method Post -Uri "http://127.0.0.1:$Port/$path" -ContentType 'application/json' `
    -Body ([Text.Encoding]::UTF8.GetBytes($json)) | Out-Null
