# Installs Usage Limit Viewer from this source folder: builds it if needed, then runs the app's own setup.
# Most people use the one-line installer in README.md instead.
param([switch]$Rebuild)
$ErrorActionPreference = 'Stop'
$exe = Join-Path $PSScriptRoot 'dist/UsageLimitViewer.exe'
if ($Rebuild -or !(Test-Path -LiteralPath $exe)) {
    & (Join-Path $PSScriptRoot 'tools/BuildRelease.ps1')
}
$setup = Start-Process -FilePath $exe -ArgumentList '--setup', '--silent', '--no-launch' -PassThru
$setup.WaitForExit()
if ($setup.ExitCode -ne 0) { throw "Setup failed (exit $($setup.ExitCode)). See $env:TEMP\UsageLimitViewer-Setup.log" }
Start-Process -FilePath (Join-Path $env:LOCALAPPDATA 'Programs\UsageLimitViewer\UsageLimitViewer.exe') -ArgumentList '--install-finished'
