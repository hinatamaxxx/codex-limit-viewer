$ErrorActionPreference = 'Stop'
$source = Join-Path $PSScriptRoot 'dist/CodexLimitViewer.exe'
if (!(Test-Path -LiteralPath $source)) { throw 'Build or extract dist/CodexLimitViewer.exe first.' }
$target = Join-Path $env:LOCALAPPDATA 'Programs/CodexLimitViewer'
$exe = Join-Path $target 'CodexLimitViewer.exe'
New-Item -ItemType Directory -Force $target | Out-Null
Get-Process CodexLimitViewer -ErrorAction SilentlyContinue | Where-Object Path -eq $exe | ForEach-Object { $_.Kill(); $_.WaitForExit() }
Copy-Item -LiteralPath $source -Destination $exe -Force
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut((Join-Path ([Environment]::GetFolderPath('Programs')) 'Codex Limit Viewer.lnk'))
$shortcut.TargetPath = $exe
$shortcut.WorkingDirectory = $target
$shortcut.Save()
Start-Process -FilePath $exe
Write-Output "Installed: $exe"
