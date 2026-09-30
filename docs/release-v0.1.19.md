# Codex Limit Viewer v0.1.19

## 日本語

Windows 11の通知領域に利用枠の残量を表示するアプリに、ダークテーマのセットアップ画面とportable版を追加しました。セットアップ版は同じ場所へ更新するため、バージョンごとにインストール先が増えません。

- **通常利用**: `Codex-Limit-Viewer-v0.1.19-Setup-win-x64.exe`を実行します。管理者権限は不要です。スタートメニューにアプリとアンインストールの項目が追加され、自動起動は任意で有効にできます。
- **更新**: 新しいセットアップEXEを実行するだけで、設定を引き継いで同じ場所のアプリを置き換えます。
- **portable版**: `Codex-Limit-Viewer-v0.1.19-portable-win-x64.zip`を展開し、`CodexLimitViewer.exe`を起動します。設定と残量キャッシュは隣の`Data`フォルダに保存します。`portable.flag`は削除しないでください。CLIのログイン情報とClaude Codeのstatus line連携用ファイルはセットアップ版と共通です。
- **アンインストール**: セットアップ版はスタートメニューの「Uninstall Codex Limit Viewer」を開きます。設定やキャッシュは初期状態では残り、削除を選ぶこともできます。Claude Codeの連携用スクリプトは残します。portable版は自動起動をオフにして終了し、展開フォルダを削除します。

インストールと自動起動の設定ではレジストリを変更せず、ファイルとショートカットを使用します。Windows設定の「インストールされているアプリ」には登録せず、スタートメニューから管理します。同じWindowsセッションでは、セットアップ版とportable版を合わせて1つだけ起動します。

以前の版で自動起動を有効にしていた場合は、Windowsの「設定」>「アプリ」>「スタートアップ」で旧版の項目を無効にしてください。この版の自動起動をオフにしても、旧版がレジストリに保存した設定は変更されません。

Windows 11で、インストール・更新・アンインストールの繰り返し、設定の引き継ぎ、portable版の設定の分離、自己テストを確認しました。再起動後の自動起動と異なるDPIのマルチモニタ環境は未検証です。サービスからの実際の残量取得は、この版では再検証していません。Windows x64向けの自己完結型・未署名プレリリースです。配布ファイルのSHA-256は`SHA256SUMS.txt`で確認できます。

v0.1.19の作成にはCodex（GPT-6.1 Sol、推論設定: Ultra）を使用し、日本語・英語の公開文はGemini 3.8 Flash (High)（推論設定: High）で校正しました。[詳しい使い方](https://github.com/hinatamaxxx/codex-limit-viewer/blob/v0.1.19/README.md)

## English

This Windows 11 quota viewer now includes a dark setup window and a portable edition. Setup updates the app in one fixed folder, so each version does not create another installation.

- **Regular use**: Run `Codex-Limit-Viewer-v0.1.19-Setup-win-x64.exe`. No administrator privileges are required. Setup adds app and uninstall entries to the Start Menu, with optional startup at sign-in.
- **Updates**: Run the new setup EXE to replace the app in the same folder while keeping your settings.
- **Portable edition**: Extract `Codex-Limit-Viewer-v0.1.19-portable-win-x64.zip` and run `CodexLimitViewer.exe`. Settings and quota cache are stored in the adjacent `Data` folder. Keep `portable.flag` in place. CLI sign-ins and Claude Code status-line integration files are shared with the setup edition.
- **Uninstall**: For the setup edition, open “Uninstall Codex Limit Viewer” from the Start Menu. Settings and cache are kept by default, with an option to remove them. The Claude Code bridge script is retained. For the portable edition, disable startup, exit the app, and delete its extracted folder.

Installation and startup use files and shortcuts without changing the registry. The app is managed through the Start Menu rather than registered under Windows Settings > Installed apps. Only one copy can run in the same Windows session, including across both editions.

If you enabled startup in an earlier version, disable its old entry in Windows **Settings > Apps > Startup**. Turning startup off in this version does not change registry settings created by an earlier version.

Checked on Windows 11: repeated installation, updates and uninstallation, settings preservation, portable settings isolation, and self-tests. Startup after a reboot and mixed-DPI monitors are unverified. Live service quota fetching was not retested for this version. This is a self-contained, unsigned Windows x64 pre-release. SHA-256 checksums are provided in `SHA256SUMS.txt`.

v0.1.19 was made with Codex using GPT-6.1 Sol (reasoning effort: Ultra). The Japanese and English publication text was proofread with Gemini 3.8 Flash (High) (reasoning effort: High). [Full guide](https://github.com/hinatamaxxx/codex-limit-viewer/blob/v0.1.19/README.en.md)
