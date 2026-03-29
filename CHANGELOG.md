# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [Unreleased]

---

## [0.3.1] - 2026-03-30

### Added
- ログ機能を新規実装
  - `Services/LogService.cs` を新規追加（外部ライブラリ不使用、.NET 標準機能のみ）
  - ログ保存先: `%APPDATA%\TwitchChatOverlay\logs\TwitchChatOverlay_YYYY-MM-DD.log`（設定ファイルと同じ `TwitchChatOverlay` フォルダ配下）
  - ログエントリに **時刻・ファイル名・行番号・メソッド名** を自動付与（`CallerFilePath` / `CallerLineNumber` / `CallerMemberName` 属性を使用）
  - ログレベル: `Debug` / `Info` / `Warning` / `Error` の4段階
  - `Exception` を渡すと型名・メッセージ・`InnerException`・スタックトレースを自動記録
  - `ConcurrentQueue` + バックグラウンドタスクによる非同期書き込み（UIスレッドをブロックしない）
  - 日次ログローテーション（日付が変わると新ファイルを自動作成）
  - 起動時に30日超の古いログファイルを自動削除
  - `MinLevel` プロパティでフィルタリングレベルを動的に変更可能
  - `Flush()` / `Shutdown()` でアプリ終了時にキュー内の残ログを確実に書き出し
- グローバル未処理例外ハンドラを `App.xaml.cs` に登録
  - `Application.DispatcherUnhandledException`（UIスレッドの未捕捉例外）
  - `AppDomain.CurrentDomain.UnhandledException`（致命的なバックグラウンド例外）
  - `TaskScheduler.UnobservedTaskException`（非同期タスクの未観測例外）
- 各サービス・ViewModel の既存エラー処理箇所に `LogService.Error()` / `LogService.Warning()` を追加
  - `SettingsService`: 設定保存・読込エラー
  - `TwitchApiService`: トークン検証ネットワークエラー
  - `TwitchEventSubService`: 接続・切断イベント（Info）、予期しない切断（Error）、メッセージパースエラー（Warning）
  - `TwitchOAuthServer`: ブラウザ起動失敗・JSONパースエラー
  - `TwitchTokenExchange`: トークン交換エラー
  - `UpdateService`: アップデート検出・ダウンロード・インストール（Info）
  - `ToastNotificationService`: トースト表示エラー
  - `MainWindowViewModel`: 接続・切断・OAuth認可・設定保存・再接続の全エラーおよび成功イベント（Info）

---

## [0.3.0] - 2026-03-29

### Added
- アプリ起動時に GitHub Releases から最新バージョンを自動確認
  - `UpdateService` を新規追加（`Services/UpdateService.cs`）
  - GitHub API (`/repos/denpadokei/TwitchChatOverlay/releases/latest`) を使用
  - 現バージョンより新しいリリースがある場合、メインウィンドウ上部に更新バナーを表示
- 更新バナーに「⬇ 更新する」ボタンを追加
  - 最新アセット（`.exe` / `.zip`）を `%TEMP%\TwitchChatOverlay\` へ自動ダウンロード
  - ダウンロード中は進捗バーを表示し、ボタンを無効化
  - `.exe` の場合はインストーラーを起動してアプリを終了
  - `.zip` の場合は解凍後、アプリのインストールフォルダへ上書きコピーしてアプリを再起動
- 更新バナーに「🔗 リリースページ」ボタンを追加（ブラウザで GitHub Releases ページを開く）

---

## [0.2.0] - 2026-03-29

### Added
- リフレッシュトークンによるアクセストークンの自動更新
  - `AppSettings` に `RefreshToken` プロパティを追加
  - `TwitchOAuthServer.RefreshTokenAsync()` メソッドを追加（`POST /oauth2/token grant_type=refresh_token`）
  - OAuth 認可完了時にリフレッシュトークンを設定ファイルへ保存
  - 起動時のトークン検証で無効な場合、リフレッシュトークンで自動更新してから接続
- 接続中のトークン期限切れ対応
  - `TwitchEventSubService` に `ConnectionLost` イベントを追加（予期しない切断を通知）
  - 切断検知時にリフレッシュトークンで新トークンを取得して自動再接続
  - 接続成功時に3時間ごとの予防的リフレッシュタイマーを開始（アクセストークンは約4時間で失効）
  - 手動切断時はタイマーを停止
- トースト通知ウィンドウのレイアウト改善
  - フォントサイズ大時のテキストクリップを修正（`MaxHeight` 制限を削除）
  - 複数トーストのスタック計算を実際の `ActualHeight` ベースに変更（重なり解消）
  - 下配置時に `ContentRendered` 後に再整列して位置ずれを修正
- トースト通知ウィンドウのカスタマイズ設定
  - **横幅**設定を追加（200〜600px、スライダー+TextBox 直接入力）
  - **背景の濃さ**設定を追加（0〜100%、スライダー+TextBox 直接入力）
  - **フォント指定**を追加（プリセットから選択 or 直接入力、空白でシステムデフォルト）
  - **背景色モード**を追加（ダーク / ライト / システム設定 / カスタム HEX）
  - カスタムモード選択時のみ HEX カラー入力欄を表示
  - 背景色の明暗に応じてテキスト色を自動切替（暗色→白文字、明色→濃紺文字）
  - **文字色モード**を追加（自動: 背景色の明暗に応じて自動選択 / カスタム: HEX 指定）
  - カスタム文字色選択時のみ HEX カラー入力欄を表示
  - サブテキスト（チャンネル名など）はカスタム文字色に透明度 `0xCC` を適用
- 文字サイズ・横幅・背景の濃さの設定 UI をスライダー＋TextBox（直接入力）形式に変更

---

## [0.1.0] - 2026-03-29

### Added
- Twitch EventSub WebSocket による通知オーバーレイ
  - チャットメッセージ (`channel.chat.message`)
  - チャネルポイント報酬 (`channel.channel_points_custom_reward_redemption.add`)
  - Raid (`channel.raid`)
  - フォロー (`channel.follow`)
  - サブスクライブ (`channel.subscribe`)
  - ギフトサブスクライブ (`channel.subscription.gift`)
  - リサブ (`channel.subscription.message`)
  - ハイプトレイン開始/終了 (`channel.hype_train.begin`, `channel.hype_train.end`)
- Device Authorization Flow による OAuth 認証（ブラウザ不要のデバイスコード方式）
- トークン・設定の永続化（AES-256-CBC 暗号化、`%APPDATA%\TwitchChatOverlay\settings.json`）
- 起動時に保存済みトークンを検証して自動接続
- チャンネル接続履歴（最大10件の最近のチャンネルリスト）
- トースト通知ウィンドウ
  - 表示位置選択（4隅: 右上 / 左上 / 右下 / 左下）
  - マルチモニター対応（表示先モニターの選択）
  - フォントサイズ設定（8〜24pt スライダー）
  - 表示時間・最大同時表示数の設定
  - イベント種別ごとの表示 ON/OFF 設定
- Twitch エモート表示対応（静止画・アニメーション GIF）
  - XamlAnimatedGif v2.3.1 によるアニメーション GIF レンダリング
  - `TextFragment` / `StaticEmoteFragment` / `AnimatedEmoteFragment` モデル
- .NET 10 対応（net6.0-windows → net10.0-windows）
- GitHub Actions CI ワークフロー（プッシュ・PR 時に自動ビルド）
- GitHub Actions Release ワークフロー（`v*.*.*` タグで単一ファイル EXE を生成し GitHub Release に添付）
- GitHub Issue テンプレート（バグ報告・機能要望）
