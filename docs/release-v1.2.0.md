# Codex Limit Viewer v1.2.0

## 日本語

Windows 11の通知領域で、AIサービスの利用枠の残り割合をひと目で確認できるアプリです。今回の版では、アプリを大幅に小さくし、残量の取得を安定させました。

![タスクバーの表示例](https://raw.githubusercontent.com/hinatamaxxx/codex-limit-viewer/main/docs/taskbar.jpg)

### 主な変更点

- **アプリが約1.4MBになりました**（v1.1.0のセットアップは123MB、インストール後は約300MB）。Windowsに標準で入っている.NET Framework 4.8で動くため、実行環境を同梱しません。常駐中のメモリも少なくなりました。
- **セットアップと本体を1つのEXEにまとめました。** インストール先には本体のEXEだけが入り、アンインストールも同じEXEで行います。
- **残量の取得を改善しました。** npmで入れたCodex CLIに対応しました。AntigravityとGrokは3分ごとに取得します。Claudeは、CLI側でログイン情報が更新されていても取得を続けられます。画面のロック中とスリープ中は取得を止め、戻るとすぐ更新します。
- 通知領域や使用状況ウィンドウで、マウスカーソルが手の形などに変わらないようにしました。

### ダウンロード

- **セットアップ版**: `Codex-Limit-Viewer-v1.2.0-Setup-win-x64.exe`を実行してインストールします。管理者権限は不要です。前の版の上にそのまま入れると、設定を引き継いで更新します。
- **portable版**: `Codex-Limit-Viewer-v1.2.0-portable-win-x64.zip`を展開し、`CodexLimitViewer.exe`を起動します。設定と残量キャッシュは隣の`Data`フォルダに保存します。`portable`ファイルは削除しないでください。
- **SHA256SUMS.txt**: ダウンロードしたファイルのSHA-256を照合するための一覧です。

Windows 11向けの非公式・未署名アプリです。初回の実行時にSmartScreenの警告が出た場合は、「詳細情報」→「実行」を選んでください。

[準備・使い方・更新・削除の手順](https://github.com/hinatamaxxx/codex-limit-viewer/blob/v1.2.0/README.md)

## English

See your remaining AI quota at a glance in the Windows 11 system tray. This release makes the app far smaller and quota fetching sturdier.

### Highlights

- **The app is now about 1.4 MB** (v1.1.0: 123 MB setup, about 300 MB installed). It runs on .NET Framework 4.8, which is part of Windows, so no runtime is bundled. It also uses less memory while running.
- **One exe for setup and app.** The install folder holds just the app's exe, which also uninstalls itself.
- **Sturdier fetching.** Codex CLI installed with npm works; Antigravity and Grok are fetched every three minutes; Claude keeps working when the CLI renews its sign-in; fetching pauses while the screen is locked or the PC sleeps and refreshes as soon as you return.
- The mouse pointer no longer changes to a hand or other shapes over the tray text or in the usage popup.

### Downloads

- **Setup edition**: Run `Codex-Limit-Viewer-v1.2.0-Setup-win-x64.exe` to install. No administrator privileges are required. Installing over an earlier version updates it and keeps your settings.
- **Portable edition**: Extract `Codex-Limit-Viewer-v1.2.0-portable-win-x64.zip` and run `CodexLimitViewer.exe`. Settings and quota cache are stored in the adjacent `Data` folder. Keep the `portable` file in place.
- **SHA256SUMS.txt**: Use this list to verify the SHA-256 of downloaded files.

This is an unofficial, unsigned Windows 11 app. If SmartScreen warns on first run, choose “More info” → “Run anyway”.

[Setup, usage, update, and uninstall instructions](https://github.com/hinatamaxxx/codex-limit-viewer/blob/v1.2.0/README.en.md)

---

制作協力 / Development assistance: Codex (GPT-6 Sol / Medium, GPT-6.1 Sol / Ultra・High), Claude Code (Claude Opus 5.5 / Medium). モデル名の後ろは推論設定です / Values after model names are reasoning-effort settings.
