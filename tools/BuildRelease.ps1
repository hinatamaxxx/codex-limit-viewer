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
try {
    # Documents and scripts that go beside the exe; the setup embeds them and installs them with itself.
    foreach ($file in @('README.md','README.en.md','LICENSE','EnableClaudeCode.ps1','ClaudeCodeStatusLine.ps1')) {
        Copy-Item -LiteralPath (Join-Path $root $file) -Destination $payload
    }
    Copy-Item -LiteralPath (Join-Path $root 'docs') -Destination $payload -Recurse
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $payloadZip = Join-Path $root 'installer/payload.zip'
    if (Test-Path -LiteralPath $payloadZip) { Remove-Item -LiteralPath $payloadZip }
    [IO.Compression.ZipFile]::CreateFromDirectory($payload, $payloadZip, [IO.Compression.CompressionLevel]::Optimal, $false)

    # One compressed, self-contained exe is the app, its setup (when named "...Setup...") and its uninstaller.
    & dotnet publish $project -c Release --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o (Join-Path $root 'dist')
    if ($LASTEXITCODE -ne 0) { throw 'Application build failed.' }
    $exe = Join-Path $root 'dist/CodexLimitViewer.exe'

    $setupName = "Codex-Limit-Viewer-v$version-Setup-win-x64.exe"
    Copy-Item -LiteralPath $exe -Destination (Join-Path $release $setupName) -Force

    Copy-Item -LiteralPath $exe -Destination $portable
    Get-ChildItem -LiteralPath $payload | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $portable -Recurse }
    'Codex Limit Viewer portable' | Set-Content -LiteralPath (Join-Path $portable 'portable') -Encoding ascii
    $portableName = "Codex-Limit-Viewer-v$version-portable-win-x64.zip"
    $portableZip = Join-Path $release $portableName
    if (Test-Path -LiteralPath $portableZip) { Remove-Item -LiteralPath $portableZip }
    [IO.Compression.ZipFile]::CreateFromDirectory($portable, $portableZip, [IO.Compression.CompressionLevel]::Optimal, $true)

    $checksums = foreach ($name in @($setupName, $portableName)) {
        $hash = (Get-FileHash -LiteralPath (Join-Path $release $name) -Algorithm SHA256).Hash.ToLowerInvariant()
        "$hash  $name"
    }
    $checksums | Set-Content -LiteralPath (Join-Path $release 'SHA256SUMS.txt') -Encoding ascii
}
finally {
    if (Test-Path -LiteralPath $work) { Remove-Item -LiteralPath $work -Recurse -Force }
}
Write-Output "Release files: $release"
