#requires -Version 5.1
param()
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$project = Join-Path $root 'CodexLimitViewer.csproj'
$version = [string]([xml](Get-Content -LiteralPath $project -Raw)).Project.PropertyGroup.Version
$release = Join-Path $root "release/v$version"
$work = Join-Path $release ('.work-' + [guid]::NewGuid().ToString('N'))
$payload = Join-Path $work 'payload'
$portable = Join-Path $work "Codex-Limit-Viewer-v$version-portable-win-x64"
New-Item -ItemType Directory -Path $payload, $portable -Force | Out-Null
& dotnet publish $project -c Release --self-contained true -p:PublishSingleFile=true -o (Join-Path $root 'dist')
if ($LASTEXITCODE -ne 0) { throw 'Application build failed.' }
Copy-Item -LiteralPath (Join-Path $root 'dist/CodexLimitViewer.exe') -Destination $payload
foreach ($file in @('README.md','README.en.md','LICENSE','EnableClaudeCode.ps1','ClaudeCodeStatusLine.ps1')) {
    Copy-Item -LiteralPath (Join-Path $root $file) -Destination $payload
}
Copy-Item -LiteralPath (Join-Path $root 'docs') -Destination $payload -Recurse
Get-ChildItem -LiteralPath $payload | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $portable -Recurse }
'Codex Limit Viewer portable' | Set-Content -LiteralPath (Join-Path $portable 'portable.flag') -Encoding ascii
Add-Type -AssemblyName System.IO.Compression.FileSystem
$payloadZip = Join-Path $work 'payload.zip'
[IO.Compression.ZipFile]::CreateFromDirectory($payload,$payloadZip,[IO.Compression.CompressionLevel]::Optimal,$false)
Copy-Item -LiteralPath $payloadZip -Destination (Join-Path $root 'installer/payload.zip') -Force
& dotnet publish (Join-Path $root 'installer/CodexLimitViewerSetup.csproj') -c Release --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o (Join-Path $root 'dist-setup')
if ($LASTEXITCODE -ne 0) { throw 'Installer build failed.' }
$setupName = "Codex-Limit-Viewer-v$version-Setup-win-x64.exe"
Copy-Item -LiteralPath (Join-Path $root 'dist-setup/CodexLimitViewerSetup.exe') -Destination (Join-Path $release $setupName) -Force
$portableName = "Codex-Limit-Viewer-v$version-portable-win-x64.zip"
$portableZip = Join-Path $work $portableName
[IO.Compression.ZipFile]::CreateFromDirectory($portable,$portableZip,[IO.Compression.CompressionLevel]::Optimal,$true)
Move-Item -LiteralPath $portableZip -Destination (Join-Path $release $portableName) -Force
$checksums = foreach ($name in @($setupName,$portableName)) {
    $hash = (Get-FileHash -LiteralPath (Join-Path $release $name) -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $name"
}
$checksums | Set-Content -LiteralPath (Join-Path $release 'SHA256SUMS.txt') -Encoding ascii
Write-Output "Release files: $release"
