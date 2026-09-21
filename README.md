# Codex Limit Viewer

[English](README.en.md)

Windows 11のタスクバー通知領域（システムトレイ）に、CodexおよびAntigravity CLI（`agy`）の利用枠残量を常時2行でスマートに表示する軽量ツールです。

![Taskbar Preview](docs/taskbar.png)

## 主な特徴

- **常時モニタリング**: タスクバー上に残量（最も低いバケットの割合）を常に表示。作業の手を止めずに残り枠を一目で把握できます。
- **ワンクリックで詳細確認**: トレイアイコンをクリックすると、各バケットの詳細残量、リセットまでの時間（日数・時間・分）、最終取得時刻を確認できます。
- **安全な公式CLI連携**: 60秒ごとにインストール済みの公式CLIを呼び出して取得します。アプリ自体がアカウントの認証情報を抽出・保持することはありません。
- **エラー時の安心表示**: 取得に失敗した場合は前回の数値をグレーで維持表示し、急な表示消去を防ぎます。
- **バイリンガル対応**: 日本語を標準搭載。右クリックメニューから英語表記へ簡単に切り替え可能です。

![サンプル表示](docs/preview-ja.png)

## はじめ方

1. **事前準備**:
   公式の [Codex](https://github.com/openai/codex) および/または [Antigravity CLI (`agy`)](https://antigravity.google/docs/cli-install) を事前にインストールし、ログインを済ませておきます（本アプリにCLI本体は同梱されていません）。
2. **ダウンロード**:
   [Releases](https://github.com/hinatamaxxx/codex-limit-viewer/releases) より最新のZIPファイルをダウンロードし、展開します。
3. **インストール**:
   展開したフォルダ内でPowerShellを開き、以下を実行します。
   ```powershell
   powershell -NoProfile -ExecutionPolicy Bypass -File ./Install.ps1
   ```
   - インストール先: `%LOCALAPPDATA%/Programs/CodexLimitViewer`
   - データ保存先: `%LOCALAPPDATA%/CodexLimitViewer`
4. **タスクバーの表示設定**:
   Windowsの「設定」>「個人用設定」>「タスクバー」>「その他のシステム トレイ アイコン」を開き、本アプリの項目をオンに設定して、4つのスロットが隣り合うように配置してください。

## 使い方

- **左クリック**: 詳細ウィンドウの表示 / 非表示
- **右クリック**: メニュー表示（今すぐ更新、言語切り替え、スタートアップ設定、終了）
- **スタートアップ起動**: 右クリックメニューから任意で有効化できます（初期状態はオフ）。

## カスタマイズ

Claude Code など他のプロバイダーへの対応を追加したい場合は、本リポジトリのURLをCodex等のAIアシスタントに渡し「別のプロバイダーを追加して」と依頼してみてください。
※各サービスのAPIや認証仕様に合わせた実装が別途必要です（標準では組み込まれていません）。

## 開発者向けビルド（任意）

.NET 10 SDK を使用して、追加のランタイム不要な自己完結型バイナリをビルドできます。

```bash
dotnet publish -c Release --self-contained true -p:PublishSingleFile=true -o dist
```

## アンインストール

1. 右クリックメニューから「スタートアップ」をオフにし、アプリを「終了」します。
2. 以下のフォルダおよびスタートメニューのショートカットを削除してください。
   - `%LOCALAPPDATA%/Programs/CodexLimitViewer`
   - `%LOCALAPPDATA%/CodexLimitViewer`

## 注意事項

- 本ソフトウェアは非公式ツールです。
- 未署名のプレリリース版です。Windows 11（150% DPI環境）で動作確認を行っています。マルチモニタ（異なるDPI設定）や再起動直後の自動起動挙動は未検証です。
- ライセンス: [MIT License](LICENSE)