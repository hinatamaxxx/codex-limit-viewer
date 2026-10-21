# Usage Limit Viewer: one-line install and update.
#   irm https://raw.githubusercontent.com/hinatamaxxx/usage-limit-viewer/main/get.ps1 | iex
# Downloads the latest release, checks it against the release's SHA-256, installs it for the current user
# (no administrator rights, no UAC), turns on startup for a new install, and shows the finished screen.
# Saved as UTF-8 without a BOM: `irm | iex` fails on a leading BOM.
& {
    $ErrorActionPreference = 'Stop'
    $ProgressPreference = 'SilentlyContinue'
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
    # Japanese when either this console or Windows itself is Japanese.
    $japanese = (Get-UICulture).Name -like 'ja*' -or [Globalization.CultureInfo]::InstalledUICulture.Name -like 'ja*'
    $progress = @{ Step = 0 }   # A hashtable, so the function below updates the same counter.
    function Say([string]$ja, [string]$en) {
        $progress.Step++
        Write-Host ("[{0}] {1}" -f $progress.Step, $(if ($japanese) { $ja } else { $en }))
    }
    $repository = 'hinatamaxxx/usage-limit-viewer'
    $asset = 'usage-limit-viewer.zip'
    $work = Join-Path ([IO.Path]::GetTempPath()) ('usage-limit-viewer-' + [guid]::NewGuid().ToString('N'))
    try {
        Say '最新版を確認しています' 'Looking up the latest version'
        $release = Invoke-RestMethod "https://api.github.com/repos/$repository/releases/latest" -Headers @{ 'User-Agent' = 'usage-limit-viewer-get' }
        $prefix = "https://github.com/$repository/releases/download/"
        $zipUrl = ($release.assets | Where-Object name -eq $asset).browser_download_url
        $shaUrl = ($release.assets | Where-Object name -eq "$asset.sha256").browser_download_url
        if (-not $zipUrl -or -not $shaUrl -or -not $zipUrl.StartsWith($prefix) -or -not $shaUrl.StartsWith($prefix)) {
            throw $(if ($japanese) { "リリース $($release.tag_name) にインストール用のファイルがありません。" } else { "Release $($release.tag_name) has no install files." })
        }

        Say "$($release.tag_name) をダウンロードしています" "Downloading $($release.tag_name)"
        New-Item -ItemType Directory -Path $work | Out-Null
        $zip = Join-Path $work $asset
        Invoke-WebRequest $zipUrl -OutFile $zip -UseBasicParsing
        # Release files come back as bytes from Invoke-WebRequest, so save the checksum and read it as text.
        $shaFile = Join-Path $work "$asset.sha256"
        Invoke-WebRequest $shaUrl -OutFile $shaFile -UseBasicParsing
        $expected = ((Get-Content -LiteralPath $shaFile -Raw).Trim() -split '\s+')[0].ToLowerInvariant()

        Say 'ダウンロードしたファイルを照合しています（SHA-256）' 'Checking the download (SHA-256)'
        $actual = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actual -ne $expected) {
            throw $(if ($japanese) { 'ダウンロードしたファイルのハッシュが一致しません。もう一度お試しください。' } else { 'The download does not match its SHA-256. Please try again.' })
        }
        $files = Join-Path $work 'files'
        Expand-Archive -LiteralPath $zip -DestinationPath $files

        Say 'インストールしています（管理者の権限は使いません）' 'Installing (no administrator rights needed)'
        $setup = Start-Process -FilePath (Join-Path $files 'UsageLimitViewer.exe') -ArgumentList '--setup', '--silent', '--no-launch' -PassThru
        $setup.WaitForExit()
        if ($setup.ExitCode -ne 0) {
            $log = Join-Path ([IO.Path]::GetTempPath()) 'UsageLimitViewer-Setup.log'
            $reason = if (Test-Path -LiteralPath $log) { (Get-Content -LiteralPath $log -TotalCount 1) } else { "exit $($setup.ExitCode)" }
            throw $(if ($japanese) { "インストールできませんでした: $reason" } else { "Installation failed: $reason" })
        }

        Say '完了しました' 'Done'
        Start-Process -FilePath (Join-Path $env:LOCALAPPDATA 'Programs\UsageLimitViewer\UsageLimitViewer.exe') -ArgumentList '--install-finished'
    }
    catch {
        Write-Host $_.Exception.Message -ForegroundColor Red
    }
    finally {
        if (Test-Path -LiteralPath $work) { Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue }
    }
}
