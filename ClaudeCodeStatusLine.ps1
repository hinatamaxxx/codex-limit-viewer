param(
    [string]$OutputPath = (Join-Path $env:LOCALAPPDATA 'CodexLimitViewer/claude-code-usage.json')
)

try {
    $session = [Console]::In.ReadToEnd() | ConvertFrom-Json
    $windows = @{}
    foreach ($name in @('five_hour', 'seven_day')) {
        $window = $session.rate_limits.$name
        if ($null -eq $window -or $null -eq $window.used_percentage) { continue }
        $percent = [double]$window.used_percentage
        if ([double]::IsNaN($percent) -or [double]::IsInfinity($percent) -or $percent -lt 0 -or $percent -gt 100) { continue }
        $entry = @{ used_percentage = $percent }
        if ($null -ne $window.resets_at) { $entry.resets_at = [long]$window.resets_at }
        $windows[$name] = $entry
    }
    $snapshot = @{
        captured_at = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
        rate_limits = $windows
    } | ConvertTo-Json -Compress -Depth 5
    $folder = [System.IO.Path]::GetDirectoryName($OutputPath)
    [System.IO.Directory]::CreateDirectory($folder) | Out-Null
    [System.IO.File]::WriteAllText($OutputPath, $snapshot, [System.Text.UTF8Encoding]::new($false))
    if ($windows.ContainsKey('five_hour')) {
        'Claude Code 5h {0:0.#}% remaining' -f (100 - $windows.five_hour.used_percentage)
    } else {
        'Claude Code'
    }
} catch {
    'Claude Code'
}
