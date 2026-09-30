# Codex Limit Viewer v1.0.0

## 日本語

Windows 11の通知領域で、AIサービスの利用枠の残り割合をひと目で確認できます。Codex、Antigravity CLI、Claude Code、Grok Build CLIから2つを選んで表示し、クリックでリセット日時などの詳細を開けます。日本語が初期設定で、英語にも切り替えられます。

![タスクバーの表示例](https://raw.githubusercontent.com/hinatamaxxx/codex-limit-viewer/v1.0.0/docs/taskbar.png)

### ダウンロード

- **セットアップ版**: `Codex-Limit-Viewer-v1.0.0-Setup-win-x64.exe`を実行してインストールします。管理者権限は不要です。スタートメニューから起動・アンインストールできます。更新時は設定を保ったまま同じフォルダのアプリを置き換えます。
- **portable版**: `Codex-Limit-Viewer-v1.0.0-portable-win-x64.zip`を展開し、`CodexLimitViewer.exe`を起動します。設定と残量キャッシュは隣の`Data`フォルダに保存します。`portable.flag`は削除しないでください。
- **SHA256SUMS.txt**: ダウンロードしたファイルのSHA-256を照合するための一覧です。

利用するサービスのCLIなどに、あらかじめログインしてください。通知領域の表示設定で本アプリの4つのアイコンをすべてオンにし、隣り合うように並べます。自動起動とホバーでの詳細表示は任意で有効にできます。インストールと自動起動の設定ではレジストリを変更しません。

Windows 11 x64向けの非公式・未署名アプリです。追加の.NETランタイムは不要です。Antigravityはagyのバージョンによって残量を取得できない場合があります。異なる表示倍率の複数モニターと、再起動後の自動起動は動作未確認です。

[準備・使い方・更新・削除の手順](https://github.com/hinatamaxxx/codex-limit-viewer/blob/v1.0.0/README.md)

## English

See your remaining AI quota at a glance in the Windows 11 system tray. Choose two services from Codex, Antigravity CLI, Claude Code, and Grok Build CLI, then click to view details such as reset times. Japanese is the default language; English is also available.

### Downloads

- **Setup edition**: Run `Codex-Limit-Viewer-v1.0.0-Setup-win-x64.exe` to install. No administrator privileges are required. Launch or uninstall the app from the Start Menu. Updates replace it in the same folder while keeping settings.
- **Portable edition**: Extract `Codex-Limit-Viewer-v1.0.0-portable-win-x64.zip` and run `CodexLimitViewer.exe`. Settings and quota cache are stored in the adjacent `Data` folder. Keep `portable.flag` in place.
- **SHA256SUMS.txt**: Use this list to verify the SHA-256 of downloaded files.

Sign in to the CLI or app needed for each service first. In Windows tray settings, enable all four icons for this app and keep them adjacent. Startup and details on hover are optional. Installation and startup settings do not change the registry.

This is an unofficial, unsigned Windows 11 x64 app. No additional .NET runtime is required. Antigravity quota may be unavailable with some agy versions. Mixed-DPI monitors and startup after a reboot are unverified.

[Setup, usage, update, and uninstall instructions](https://github.com/hinatamaxxx/codex-limit-viewer/blob/v1.0.0/README.en.md)

---

制作協力 / Development assistance: Codex (GPT-6 Sol / Medium, GPT-6.1 Sol / Ultra・High), Claude Code (Claude Opus 5.5 / Medium). 公開文校正 / Proofreading: Gemini 3.8 Flash (High) / High. モデル名の後ろは推論設定です / Values after model names are reasoning-effort settings.
