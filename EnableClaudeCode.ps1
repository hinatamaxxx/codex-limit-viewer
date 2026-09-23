param(
    [string]$SettingsPath = (Join-Path $env:USERPROFILE '.claude/settings.json'),
    [string]$BridgePath = (Join-Path $env:LOCALAPPDATA 'CodexLimitViewer/ClaudeCodeStatusLine.ps1')
)

$ErrorActionPreference = 'Stop'
$source = Join-Path $PSScriptRoot 'ClaudeCodeStatusLine.ps1'
if (!(Test-Path -LiteralPath $source)) { throw 'ClaudeCodeStatusLine.ps1 is missing.' }

$settingsFolder = [System.IO.Path]::GetDirectoryName($SettingsPath)
[System.IO.Directory]::CreateDirectory($settingsFolder) | Out-Null
$raw = if (Test-Path -LiteralPath $SettingsPath) { [System.IO.File]::ReadAllText($SettingsPath) } else { '{}' }
if ([string]::IsNullOrWhiteSpace($raw)) { $raw = '{}' }
$settings = $raw | ConvertFrom-Json
if ($null -ne $settings.PSObject.Properties['statusLine']) {
    Write-Output 'An existing Claude Code status line was left unchanged. Add the bridge command to that status line manually.'
    exit 0
}

$command = 'powershell -NoProfile -ExecutionPolicy Bypass -File "' + $BridgePath.Replace('\', '/') + '"'
$statusLine = '"statusLine": {"type": "command", "command": ' + (ConvertTo-Json -InputObject $command -Compress) + '}'
$empty = [regex]::IsMatch($raw, '^\s*\{\s*\}\s*$')
$opening = $raw.IndexOf('{')
if ($opening -lt 0) { throw 'Claude Code settings must be a JSON object.' }
$insert = "`r`n  $statusLine" + $(if ($empty) { '' } else { ',' })
$updated = $raw.Insert($opening + 1, $insert)
$null = $updated | ConvertFrom-Json

$bridgeFolder = [System.IO.Path]::GetDirectoryName($BridgePath)
[System.IO.Directory]::CreateDirectory($bridgeFolder) | Out-Null
Copy-Item -LiteralPath $source -Destination $BridgePath -Force
if (Test-Path -LiteralPath $SettingsPath) { Copy-Item -LiteralPath $SettingsPath -Destination "$SettingsPath.codex-limit-viewer.bak" -Force }
[System.IO.File]::WriteAllText($SettingsPath, $updated, [System.Text.UTF8Encoding]::new($false))
Write-Output 'Claude Code status line connected to Codex Limit Viewer. Quota data appears after a Claude Code response.'
