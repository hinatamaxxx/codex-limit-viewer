param([switch]$Silent, [switch]$NoLaunch, [switch]$Startup)
$ErrorActionPreference = 'Stop'
$setup = Join-Path $PSScriptRoot 'dist/CodexLimitViewer.exe'
if (!(Test-Path -LiteralPath $setup)) {
    throw 'Run tools/BuildRelease.ps1 first, or double-click the distributed Setup.exe.'
}
$arguments = @('--setup')
if ($Silent) { $arguments += '--silent' }
if ($NoLaunch) { $arguments += '--no-launch' }
if ($Startup) { $arguments += '--startup' }
$options = @{ FilePath = $setup; PassThru = $true }
if ($Silent) { $options.WindowStyle = 'Hidden' }
$options.ArgumentList = $arguments
$process = Start-Process @options
$process.WaitForExit() # Wait for setup itself, not the resident app it may launch.
if ($process.ExitCode -ne 0) { throw "Setup failed (exit $($process.ExitCode))." }
