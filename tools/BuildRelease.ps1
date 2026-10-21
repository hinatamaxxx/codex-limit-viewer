#requires -Version 5.1
# Builds the release: usage-limit-viewer.zip (the app and its scripts) and usage-limit-viewer.zip.sha256.
# get.ps1 and the app's self-update download the zip and accept it only when the .sha256 matches.
param()
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$project = Join-Path $root 'UsageLimitViewer.csproj'
$version = [string]([xml](Get-Content -LiteralPath $project -Raw)).Project.PropertyGroup.Version
$release = Join-Path $root "release/v$version"
$work = Join-Path $release ('.work-' + [guid]::NewGuid().ToString('N'))
$payload = Join-Path $work 'payload'
$package = Join-Path $work 'package'
New-Item -ItemType Directory -Path $payload, $package -Force | Out-Null
try {
    # Documents and scripts that go beside the exe; the exe embeds them so `--setup` can install them too.
    foreach ($file in @('README.md', 'README.en.md', 'LICENSE', 'EnableClaudeCode.ps1', 'ClaudeCodeStatusLine.ps1')) {
        Copy-Item -LiteralPath (Join-Path $root $file) -Destination $payload
    }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $payloadZip = Join-Path $root 'installer/payload.zip'
    if (Test-Path -LiteralPath $payloadZip) { Remove-Item -LiteralPath $payloadZip }
    [IO.Compression.ZipFile]::CreateFromDirectory($payload, $payloadZip, [IO.Compression.CompressionLevel]::Optimal, $false)

    # One small exe for .NET Framework 4.8 (part of Windows): the app, its setup (--setup) and its uninstaller.
    $dist = Join-Path $root 'dist'
    & dotnet build $project -c Release -o $dist
    if ($LASTEXITCODE -ne 0) { throw 'Application build failed.' }
    $exe = Join-Path $dist 'UsageLimitViewer.exe'
    $built = (Get-Item -LiteralPath $exe).VersionInfo.FileVersion
    if (-not $built.StartsWith($version)) { throw "The exe version $built does not match $version." }

    Copy-Item -LiteralPath $exe -Destination $package
    Get-ChildItem -LiteralPath $payload | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $package }
    $zipName = 'usage-limit-viewer.zip'
    $zip = Join-Path $release $zipName
    if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip }
    # Files sit at the top of the zip (no folder); entry names use the file names as they are.
    $archive = [IO.Compression.ZipFile]::Open($zip, 'Create')
    try {
        foreach ($file in Get-ChildItem -LiteralPath $package -File) {
            [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $file.Name, [IO.Compression.CompressionLevel]::Optimal)
        }
    }
    finally { $archive.Dispose() }
    $hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText("$zip.sha256", "$hash  $zipName`n", (New-Object Text.UTF8Encoding $false))
}
finally {
    if (Test-Path -LiteralPath $work) { Remove-Item -LiteralPath $work -Recurse -Force }
}
Write-Output "Release files: $release"
