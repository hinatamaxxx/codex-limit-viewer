# Codex Limit Viewer v1.1.0

## 日本語

Windows 11の通知領域で、AIサービスの利用枠の残り割合をひと目で確認できるアプリです。今回の版では、使用状況ウィンドウの作り直し、インストールとアンインストールの改善、残量取得の安定化を行いました。

![タスクバーの表示例](https://raw.githubusercontent.com/hinatamaxxx/codex-limit-viewer/v1.1.0/docs/taskbar.png)

### 主な変更点

- **使用状況ウィンドウを作り直しました。** 高い表示倍率で文字やボタンが切れる問題を直し、幅を小さくして残量バーを付けました。
- **カードのドラッグ＆ドロップで並べ替えられます。** 1番目のカードがタスクバーの上段、2番目が下段になり、順番は保存されます。
- **右クリックメニューをWindows標準のメニューにしました**（ダークモード対応）。先頭に今の残量を表示し、あまり使わない設定は「その他」にまとめました。終了するときは確認画面が出ます。
- **「設定 → アプリ → インストールされているアプリ」から削除できるようになりました。** レジストリに書くのはこの登録の1か所だけです。スタートメニューの「Uninstall …」は無くなりました。
- **アンインストールでは、初期設定で設定と残量キャッシュも削除します。** 再インストールで引き継ぐ場合は「設定とデータを残す」をオンにしてください。
- **設定の保存先を`%APPDATA%\CodexLimitViewer`に移しました。** v1.0.0の設定は自動で引き継ぎます。
- **自動起動のオン/オフが、Windowsの「スタートアップ」設定と連動します。**
- **新しい版が出ると、右クリックメニューでお知らせします。**
- **残量の取得を改善しました。** npmで入れたCodex CLIに対応し、AntigravityとGrokは3分ごとの取得にしました。Claudeは、CLI側でログイン情報が更新されていても取得を続けられます。画面のロック中とスリープ中は取得を止めます。
- アプリのアイコンに余白を入れ、ほかのアプリと大きさをそろえました。

### ダウンロード

- **セットアップ版**: `Codex-Limit-Viewer-v1.1.0-Setup-win-x64.exe`を実行してインストールします。管理者権限は不要です。v1.0.0の上にそのまま入れると、設定を引き継いで更新します。
- **portable版**: `Codex-Limit-Viewer-v1.1.0-portable-win-x64.zip`を展開し、`CodexLimitViewer.exe`を起動します。設定と残量キャッシュは隣の`Data`フォルダに保存します。`portable`ファイルは削除しないでください。
- **SHA256SUMS.txt**: ダウンロードしたファイルのSHA-256を照合するための一覧です。

Windows 11 x64向けの非公式・未署名アプリです。初回の実行時にSmartScreenの警告が出た場合は、「詳細情報」→「実行」を選んでください。追加の.NETランタイムは不要です。

[準備・使い方・更新・削除の手順](https://github.com/hinatamaxxx/codex-limit-viewer/blob/v1.1.0/README.md)

## English

See your remaining AI quota at a glance in the Windows 11 system tray. This release rebuilds the usage popup, improves installing and uninstalling, and makes quota fetching sturdier.

### Highlights

- **Rebuilt usage popup.** Text and buttons no longer get cut off at high display scales; the popup is narrower and shows quota bars.
- **Reorder cards by drag and drop.** The first card is the taskbar’s top row and the second its bottom row; the order is saved.
- **Standard Windows right-click menu** (dark mode aware). It starts with your current quota, keeps rarely used settings under “More”, and asks before exiting.
- **Uninstall from Settings → Apps → Installed apps.** That listing is the only registry entry; the “Uninstall …” Start menu shortcut is gone.
- **Uninstalling removes settings and quota cache by default.** Turn on “Keep settings and data” to carry them over to a reinstall.
- **Settings now live in `%APPDATA%\CodexLimitViewer`.** Settings from v1.0.0 move over automatically.
- **Startup follows Windows’ own Startup switch.**
- **New versions are announced in the right-click menu.**
- **Sturdier fetching.** Codex CLI installed with npm works; Antigravity and Grok are fetched every three minutes; Claude keeps working when the CLI renews its sign-in; fetching pauses while the screen is locked or the PC sleeps.
- The app icon now has padding, matching other apps in size.

### Downloads

- **Setup edition**: Run `Codex-Limit-Viewer-v1.1.0-Setup-win-x64.exe` to install. No administrator privileges are required. Installing over v1.0.0 updates it and keeps your settings.
- **Portable edition**: Extract `Codex-Limit-Viewer-v1.1.0-portable-win-x64.zip` and run `CodexLimitViewer.exe`. Settings and quota cache are stored in the adjacent `Data` folder. Keep the `portable` file in place.
- **SHA256SUMS.txt**: Use this list to verify the SHA-256 of downloaded files.

This is an unofficial, unsigned Windows 11 x64 app. If SmartScreen warns on first run, choose “More info” → “Run anyway”. No additional .NET runtime is required.

[Setup, usage, update, and uninstall instructions](https://github.com/hinatamaxxx/codex-limit-viewer/blob/v1.1.0/README.en.md)

---

制作協力 / Development assistance: Codex (GPT-6 Sol / Medium, GPT-6.1 Sol / Ultra・High), Claude Code (Claude Opus 5.5 / Medium). モデル名の後ろは推論設定です / Values after model names are reasoning-effort settings.
