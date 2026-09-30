# Codex Limit Viewer v1.0.0

## 日本語

Windows 11の通知領域で利用枠の残量を確認できるCodex Limit Viewerを、v1.0.0として正式リリースしました。v0.1.19の機能をそのまま引き継ぎ、アプリとセットアップのバージョンを1.0.0にそろえています。

- Codex、Antigravity CLI、Claude Code、Grok Build CLIから、タスクバーに表示する2つのサービスを選択できます。
- クリックで残量・リセット日時・取得時刻の詳細を表示できます。ホバーでの詳細表示は任意で有効にできます。
- 日本語が初期設定で、英語に切り替えられます。
- セットアップ版は同じフォルダへ更新し、設定を引き継ぎます。インストールと自動起動の設定ではレジストリを変更しません。

**通常利用**: `Codex-Limit-Viewer-v1.0.0-Setup-win-x64.exe`を実行してください。管理者権限は不要です。旧セットアップ版からの更新も同じ手順です。スタートメニューに起動・アンインストールの項目を追加し、自動起動は任意で有効にできます。

**portable版**: `Codex-Limit-Viewer-v1.0.0-portable-win-x64.zip`を展開して`CodexLimitViewer.exe`を起動します。設定と残量キャッシュは隣の`Data`フォルダに保存します。`portable.flag`は削除しないでください。CLIのログイン情報とClaude Codeのstatus line連携用ファイルはセットアップ版と共通です。

利用したいサービスのCLIなどに、あらかじめログインしてください。Antigravityはagyのバージョンによって残量を取得できない場合があります。準備、旧版の自動起動、アンインストールの手順は[日本語ガイド](https://github.com/hinatamaxxx/codex-limit-viewer/blob/v1.0.0/README.md)を参照してください。

Windows x64向けの自己完結型・未署名アプリです。配布ZIPの自己テスト、設定を引き継ぐ更新、配布内容とSHA-256を確認しました。サービスからの実際の残量取得はこの版では再検証していません。再起動後の自動起動と異なるDPIのマルチモニタ環境は未検証です。チェックサムは`SHA256SUMS.txt`で確認できます。

v1.0.0のリリース作業にはCodex（GPT-6.1 Sol、推論設定: High）を使用し、日本語・英語の公開文はGemini 3.8 Flash (High)（推論設定: High）で校正しました。

## English

Codex Limit Viewer is now released as v1.0.0, bringing AI quota information to the Windows 11 system tray. It carries forward the features of v0.1.19, with both the app and setup version set to 1.0.0.

- Choose two taskbar services from Codex, Antigravity CLI, Claude Code, and Grok Build CLI.
- Click to see remaining quota, reset times, and fetch times. Optionally enable details on hover.
- Japanese is the default language; English is available in the menu.
- Setup updates the app in the same folder while keeping settings. Installation and startup settings do not change the registry.

**Regular use**: Run `Codex-Limit-Viewer-v1.0.0-Setup-win-x64.exe`. No administrator privileges are required. Use the same steps to update an existing setup installation. Setup adds app and uninstall entries to the Start Menu, with optional startup at sign-in.

**Portable edition**: Extract `Codex-Limit-Viewer-v1.0.0-portable-win-x64.zip` and run `CodexLimitViewer.exe`. Settings and quota cache are stored in the adjacent `Data` folder. Keep `portable.flag` in place. CLI sign-ins and Claude Code status-line integration files are shared with the setup edition.

Sign in to the CLI or app required by each service first. Antigravity quota may be unavailable with some agy versions. See the [English guide](https://github.com/hinatamaxxx/codex-limit-viewer/blob/v1.0.0/README.en.md) for prerequisites, startup entries from earlier versions, and uninstall instructions.

This is a self-contained, unsigned Windows x64 app. The distributed ZIP's self-tests, an update preserving settings, package contents, and SHA-256 checksums were checked. Live service quota fetching was not retested for this version. Startup after a reboot and mixed-DPI monitors remain unverified. Checksums are provided in `SHA256SUMS.txt`.

The v1.0.0 release work used Codex with GPT-6.1 Sol (reasoning effort: High). The Japanese and English publication text was proofread with Gemini 3.8 Flash (High) (reasoning effort: High).
