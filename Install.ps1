param([switch]$Silent, [switch]$NoLaunch, [switch]$Startup)
$ErrorActionPreference = 'Stop'
$setup = Join-Path $PSScriptRoot 'dist-setup/CodexLimitViewerSetup.exe'
if (!(Test-Path -LiteralPath $setup)) {
    throw 'Run tools/BuildRelease.ps1 first, or double-click the distributed Setup.exe.'
}
$arguments = @()
if ($Silent) { $arguments += '--silent' }
if ($NoLaunch) { $arguments += '--no-launch' }
if ($Startup) { $arguments += '--startup' }
$options = @{ FilePath = $setup; PassThru = $true }
if ($Silent) { $options.WindowStyle = 'Hidden' }
if ($arguments.Count) { $options.ArgumentList = $arguments }
$process = Start-Process @options
$process.WaitForExit() # Wait for setup itself, not the resident app it may launch.
if ($process.ExitCode -ne 0) { throw "Setup failed (exit $($process.ExitCode))." }
