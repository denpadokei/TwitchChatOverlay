# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [Unreleased]

---

## [0.4.0] - 2026-04-03

### Added
- MainWindow の設定UIを Prism RegionManager ベースのタブ構成へ移行
  - `共通` / `Twitch` / `YouTube` の3タブを追加
  - 各タブを UserControl 化（`CommonSettingsTabView` / `TwitchSettingsTabView` / `YouTubeSettingsTabView`）
  - `RegionNames` を追加して Region 名を定数管理
- YouTube OAuth（Authorization Code + PKCE）を新規実装
  - `Services/YouTubeOAuthService.cs` を追加
  - ローカルコールバック (`http://127.0.0.1:18765/callback/`) でトークン取得
  - リフレッシュトークンによるアクセストークン更新メソッドを追加
- YouTube Live Chat 受信を新規実装
  - `Services/YouTubeLiveChatService.cs` を追加
  - `liveBroadcasts` から `liveChatId` を取得（待機時は HTTP ポーリング）
  - メッセージ受信は gRPC ストリーム（`StreamList`）を使用
  - `textMessageEvent` / `superChatEvent` / `newSponsorEvent` / `memberMilestoneChatEvent` を `OverlayNotification` にマッピング
- BuildSecrets の自動生成を拡張
  - `BuildSecrets.YouTubeClientId` を追加
  - `build/Generate-BuildSecrets.ps1` に `YouTubeClientId` パラメータを追加
  - `TwitchChatOverlay.csproj` に `YouTubeClientId` プロパティとビルド注入を追加
  - `build/local.props.example` に `YouTubeClientId` 設定を追加
- OBS WebSocket 連携の基盤を追加
  - `Services/ObsWebSocketService.cs` を追加
  - OBS との接続・認証・`StreamStateChanged` イベント監視を実装
  - `App.xaml.cs` の DI に `ObsWebSocketService` を登録
- コメント通知音機能を追加
  - `Services/NotificationSoundService.cs` を追加
  - コメント受信時に通知音を再生できるよう対応
  - 埋め込み音源を既定値として追加し、外部音源ファイルの指定にも対応
  - 対応形式: `wav` / `mp3` / `ogg`
  - 出力音声デバイス選択と音量調整に対応
- 設定画面にプレビュー機能を追加
  - 共通タブに通知音プレビューと表示プレビューを追加
  - Twitch タブにコメント表示プレビューを追加
  - YouTube タブにコメント表示プレビューを追加

### Changed
- `MainWindow.xaml` の直書き設定UIを削除し、タブ+Regionホスト構成へ変更
- `ToastNotificationService` を単一ソース購読から複数ソース購読へ変更
  - Twitch + YouTube の同時購読に対応
  - プラットフォーム別通知フィルタ（YouTube Chat/Super Chat/Membership）に対応
- `OverlayNotification` に `SourcePlatform` プロパティを追加
  - 通知の発生元（Twitch / YouTube）を識別可能に変更
- `MainWindowViewModel` に YouTube 設定・接続状態・コマンド群を追加
  - `AuthorizeYouTubeOAuthCommand` / `ConnectYouTubeCommand` / `DisconnectYouTubeCommand`
  - `SelectedTabIndex` を含むタブ状態の保存・復元に対応
- `App.xaml.cs` の DI 登録を拡張
  - `YouTubeOAuthService` / `YouTubeLiveChatService` の登録
  - タブViewの `RegisterForNavigation` を追加
- YouTube 配信待機フローを OBS 連携対応へ拡張
  - OBS 利用時は配信開始イベントを検出後に 30 秒間隔の配信確認を開始
  - OBS 未利用時は `ConnectYouTube` 実行時に 1 回だけ即時配信確認し、その後 30 秒間隔の配信待機へ移行
- YouTube Live Chat 受信経路を REST ポーリングから gRPC ストリーミングへ変更
  - `Grpc.Net.Client` / `Google.Protobuf` / `Grpc.Tools` を導入
  - `youtube_live_chat_stream.proto` から gRPC クライアントを生成
  - `nextPageToken` を利用した gRPC ストリーム再開に対応
  - 配信終了時は gRPC 切断を検出して待機状態へ戻るよう改善
- YouTube 通信失敗時の再試行戦略を改善
  - 配信確認は `Retry-After` ヘッダー優先 + ジッター付き指数バックオフ（上限なし）を実装
  - gRPC ストリーム切断時も指数バックオフで再接続するよう改善
- 設定暗号化を新フォーマットへ移行
  - フォーマット識別子（`TCOSET1`）付きの保存形式を導入
  - Windows DPAPI（CurrentUser）で暗号化保存するよう変更
  - 旧 AES-CBC 形式設定の自動移行を追加
- `CommonSettingsTabView` に通知音設定 UI を追加
  - 通知音 ON/OFF
  - 埋め込み音源 / ファイル指定の切り替え
  - 音量スライダー
  - 出力音声デバイス一覧
- プレビュー操作時に保存前の設定値をそのまま反映するよう変更
  - 通知音プレビューで現在の音量・出力デバイス・音源選択を即時反映
  - コメント表示プレビューで現在のトースト外観設定を確認可能

### Fixed
- YouTube接続の耐障害性を改善
  - 401（Unauthorized）時のみトークン更新を試行するよう修正
  - `YouTubeLiveChatService.ConnectionLost` イベントを追加し、自動再接続フローを実装
- アプリ終了時のクリーンアップを改善
  - `MainWindow` クローズ時に YouTube 接続も確実に切断するよう修正
- 設定I/Oの効率と安全性を改善
  - `SettingsService` にスレッドセーフなメモリキャッシュを追加
  - `LoadSettings()` の null 安全化とクローン返却で破壊的変更リスクを低減
- YouTube 重複通知の抑制を改善
  - 既読メッセージID管理を全消去方式から固定長キュー方式へ変更
  - 長時間配信での重複再通知リスクを低減
- 埋め込み MP3 通知音の再生失敗を修正
  - ID3 タグを持たない埋め込み MP3 でも再生できるよう、ローカルキャッシュへ展開して再生する方式に変更
- 通知音プレビューで保存前の音量調整が効かない不具合を修正

### Docs
- `README.md` を Twitch + YouTube 対応内容に更新
  - YouTube OAuth / Live Chat 接続手順を追加
  - Twitch / YouTube Client ID の詳細取得手順を追加
  - Google Cloud 側の必須設定（OAuth同意画面・API有効化・loopback URI）を追記
  - YouTube Live Chat の gRPC ストリーミング受信と配信待機の挙動を追記
  - 通知音設定、プレビュー機能、追加ライブラリの説明を追記

---

## [0.3.3] - 2026-04-02

### Fixed
- アップデート実行時に、ダウンロード直後の ZIP ファイルを SHA256 検証する際に `IOException`（別プロセスにより使用中）が発生する不具合を修正
  - ダウンロード用 `FileStream` / レスポンスストリームの書き込み完了後に確実に破棄してから検証処理へ進むよう変更
  - 書き込み後に明示的にフラッシュしてから検証することで、自己ロックによる更新失敗を防止

---

## [0.3.2] - 2026-04-02

### Added
- Twitch リフレッシュトークンが無効 (401/400 invalid_grant 系) と判定された場合に、保存済みリフレッシュトークンを自動でクリアするように変更
  - `TwitchTokenRefreshException` を新規追加。HTTP ステータスコードと `error`/`message` フィールドを保持し、`IsInvalidRefreshToken` で無効判定を提供
  - `InvalidateTwitchRefreshToken()` ヘルパーメソッドを追加。サイレント更新・切断再接続・起動時の3経路すべてで共通利用
- リフレッシュ後のアクセストークンに含まれる `expires_in` をタイマー間隔に反映
  - `DeviceTokenResponse` に `ExpiresIn` プロパティを追加
  - `StartTokenRefreshTimer()` が `expires_in - 600` 秒後（最小60秒・最大3時間）にリフレッシュを発動するよう動的計算に変更
  - `expires_in` が不明な場合は従来どおり3時間をデフォルト値として使用
- `ValidateTokenAsync()` の戻り値に `ExpiresIn` (int) を追加し、`expires_in` を応答から取得して返すように変更

### Changed
- 起動時トークン処理フローを「validate 優先、必要時のみ refresh」に変更
  - 変更前: リフレッシュトークンが保存されていれば起動時に無条件 refresh
  - 変更後: まず保存済みアクセストークンを validate し、有効かつ残り 600 秒以上であればそのまま接続
  - アクセストークンが無効または残り 600 秒未満の場合のみリフレッシュトークンで refresh を試行
  - 短時間の起動・終了繰り返しによる不要な refresh 発行を抑制

---

## [0.3.1] - 2026-03-30

### Added
- トークンリフレッシュ関連のデバッグログを充実
  - `TwitchOAuthServer.RefreshTokenAsync()` に以下のログを追加
    - リフレッシュ開始時: `refresh_token` の末尾4文字のみ表示
    - HTTP レスポンス: ステータスコードと所要時間 (ms)
    - 成功時: 新アクセストークンの末尾4文字と付与されたスコープ一覧
    - 失敗時: エラー詳細
  - `RefreshTokenSilentlyAsync()`: タイマー起動・成功 (ユーザー名)・再接続実行/完了をデバッグログ出力
  - `OnConnectionLost()`: トークン更新成功・再接続開始をデバッグログ出力
  - `ValidateSavedTokenAsync()`: 起動時更新の開始・成功をデバッグログ出力
- ビルド時シークレット自動注入の仕組みを実装
  - `build/Generate-BuildSecrets.ps1` を新規追加
  - ビルドプロパティ `TwitchClientId` を MSBuild ターゲット (`GenerateBuildSecrets`) でビルド前に受け取り、XOR 難読化した `BuildSecrets.g.cs` を `obj/` 以下に自動生成
  - 生成ファイルは `obj/` 配下のため git には含まれない
- GitHub Actions リリースワークフロー (`release.yml`) でのシークレット注入に対応
  - リポジトリシークレット `TWITCH_CLIENT_ID` を `dotnet publish` に `-p:` で渡す
- ローカルデバッグ用クライアントID設定ファイル (`build/local.props`) のサポートを追加
  - `build/local.props.example` をテンプレートとして同梱（git 管理対象）
  - `build/local.props` は `.gitignore` に追加済み（git 管理対象外）
  - `local.props` が存在する場合のみ csproj に自動インポートされる
- CI ワークフロー (`ci.yml`) にアーティファクト保存を追加
  - Release ビルドと Debug ビルドをそれぞれ publish し、アーティファクトとして保存
  - `TwitchChatOverlay-win-x64-release` / `TwitchChatOverlay-win-x64-debug`
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
  - `UpdateService`: アップデート検出・ダウンロード・インストール（Info）
  - `ToastNotificationService`: トースト表示エラー
  - `MainWindowViewModel`: 接続・切断・OAuth認可・設定保存・再接続の全エラーおよび成功イベント（Info）

### Changed
- **クライアントシークレット完全除去 — DCF パブリッククライアント化**
  - Twitch の Device Code Grant Flow はパブリッククライアントとして動作可能なため、クライアントシークレットを不要とする構成に変更
  - `TwitchOAuthServer` コンストラクタから `clientSecret` パラメータを削除
  - `TwitchOAuthServer.RefreshTokenAsync()` のリクエストから `client_secret` フィールドを削除（パブリック DCF クライアントはシークレットなしでリフレッシュ可能）
  - `MainWindowViewModel` 内の `new TwitchOAuthServer(...)` 呼び出し全箇所から `BuildSecrets.ClientSecret` を削除
  - `build/Generate-BuildSecrets.ps1` から `ClientSecret` パラメータ・XOR エンコード処理・生成コードの `ClientSecret` プロパティを削除
  - `TwitchChatOverlay.csproj` から `<TwitchClientSecret>` プロパティと `-ClientSecret` MSBuild 引数を削除
  - `build/local.props` / `build/local.props.example` から `<TwitchClientSecret>` 行を削除
  - `.github/workflows/ci.yml` (2 箇所) / `release.yml` (1 箇所) から `-p:TwitchClientSecret=...` を削除
- `ClientId` をコードに直書きしないよう変更
  - `AppSettings` から `ClientId` プロパティを削除（settings.json への保存・読込を廃止）
  - `MainWindowViewModel` から `ClientId` フィールド・プロパティを削除
  - 全箇所で `BuildSecrets.ClientId` を使用するように変更

### Fixed
- `ToastNotificationViewModel.FontFamily` が `null` になり WPF バインディングエラーが発生するバグを修正（空文字のとき `SystemFonts.MessageFontFamily` を返すように変更）

### Removed
- `TwitchTokenExchange.cs` を削除（Authorization Code Flow の未使用残骸）

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
