# PR #10 コードレビュー

- 対象: PR #10 `Integrate Streamer.bot with settings and notifications support`（`other-commenttool` → `main`）
- レビュー日: 2026-11-20
- 対象差分: 19 ファイル / +1138 −55（レビュー開始時点）
- 手法: 差分の全読、Streamer.bot 公式ドキュメントおよび公式 TypeScript クライアントとのプロトコル突き合わせ、`dotnet build`、自動テスト（xUnit v3）
- 重要度の定義
  - **High**: 主要機能が動かない、または既存機能を破壊する
  - **Medium**: 誤動作する、または運用上の重大な穴がある
  - **Low**: 保守性・軽微な不備・仕様の明文化不足

## 1. サマリ

| 重要度 | 初回レビュー | 修正後 |
| --- | --- | --- |
| High | 2 | **0** |
| Medium | 6 | **0** |
| Low（修正済み） | 9 | 0 |
| Low（受容・文書化） | - | 12 |

初回レビューで High / Medium とした項目はすべて修正済みです。残るのは仕様上の制約や利用者判断に委ねる Low のみです（第 5 章）。

## 2. High

### CR-01 直接接続の通知が Streamer.bot のフィルターで停止する（修正済み）

`TwitchChatOverlay/Services/ToastNotificationService.cs` `ShouldShow`

- 変更前は `NotificationType.Chat` が `ShowStreamerBotTwitchChat` だけで判定され、`ShowReward` / `ShowFollow` などが `&& ShowStreamerBotTwitchNotifications` で結合されていました。
- そのため **Streamer.bot を使わない利用者**でも、Streamer.bot タブのチェックを外すと Twitch EventSub / YouTube gRPC の通知が表示されなくなります。通知経路の区別がないことが原因です。
- 対応:
  - `OverlayNotification.ViaStreamerBot` を追加し、`StreamerBotService` が生成する通知にのみ `true` を設定。
  - `ShouldShow` を「Streamer.bot 経由」と「直接接続」で完全に分岐。直接接続側は従来の判定を維持。
  - 判定ロジックを `internal static` に切り出し、`NotificationFilterTests` で回帰を防止。

### CR-02 イベント payload のフィールド名が実装と一致せず、通知が「(unknown)」になる（修正済み）

`TwitchChatOverlay/Services/StreamerBotService.cs` `MapTwitchEvent` / `MapYouTubeEvent` / `MapKickEvent`

- 変更前は EventSub の snake_case（`user.display_name`, `from_broadcaster_user_name` など）や推測した階層（`data.message.displayName`）を Streamer.bot の payload に適用していました。
- Streamer.bot 公式の型定義では:
  - `Twitch.Follow` は `user_name` / `user_login` / `user_id`（トップレベル、snake_case）
  - `Twitch.Sub` / `ReSub` は `displayName` / `subTier` / `cumulativeMonths`（ネストした `user` や `sub.planName` は無い）
  - `Twitch.GiftSub` は `recipientDisplayName`
  - `YouTube.Message` は `data.message` が**文字列**、ユーザーは `data.user.name`
- 結果として、チャット以外の多くのイベントでユーザー名が `(unknown)`、本文が空になっていました。さらに `message` がオブジェクトではなく**文字列**のイベント（YouTube `Message`、Twitch `Sub` / `ReSub`）では、`JValue` に対するインデクサーアクセスが `InvalidOperationException` を投げ、イベント自体が破棄されていました。**主要機能が実質動作しない**ため High と判定。
- 対応: 公式型（`Streamerbot/client` の `twitch.types.ts` / `events.const.ts`）に合わせてマッピングを修正。オブジェクト以外へのアクセスは `Child()` ヘルパーで安全化。公式に payload が未定義の YouTube SuperChat / Kick 系は、複数の既知形状を許容する抽出ヘルパー（`GetUserName` / `GetChatMessage` / `GetAmountString` など）で寛容に解釈。

## 3. Medium

### CR-03 購読結果を検証しておらず、失敗しても「接続済み・通知ゼロ」になる（修正済み）

- 変更前は `Subscribe` の応答をログに出すだけで `status` を確認していませんでした。
- また Kick 未設定の Streamer.bot では Kick を含む購読がエラーになる可能性があり、その場合 Twitch / YouTube も含めて一切通知されません。
- 対応: `Subscribe` の `status` を検証し、失敗時は Kick を除外して 1 回だけ再試行。それでも失敗する場合は接続失敗として理由を表示。

### CR-04 切断後に再接続しない（修正済み）

`MainWindowViewModel`

- OBS 連携には 10 秒間隔の自動再接続がありますが、Streamer.bot には無く、一度切れると手動接続が必要でした。
- 対応: `_streamerBotReconnectTimer` を追加し、OBS と同じ 10 秒間隔で再接続。手動切断・無効化時は停止。起動時の自動接続も同じ経路に統一。

### CR-05 受信ループが再接続後のソケットを参照するレース（修正済み）

- 変更前はループ先頭で毎回 `this._webSocket` を読み直していました。再接続が走ると旧ループが新しいソケットを読み、状態やイベントが二重になり得ます。
- 対応: 受信ループにソケットを引数で渡し、`finally` では `this._webSocket == socket` のときだけ状態を落とすように変更。

### CR-06 接続先の検証不足（修正済み）

- `new Uri($"ws://{host}:{port}/")` は IPv6（`::1`）で壊れ、`host` に URL やパスを入れると意図しない接続先になります。
- 対応: `Uri.CheckHostName` とポート範囲の検証、`UriBuilder` による組み立てに変更。不正時は `ConfigurationError` として日本語メッセージを返す。

### CR-07 認証まわりのプロトコル解釈が公式実装と不一致（修正済み）

- 変更前は Hello に `authentication` があるとパスワード必須と決め打ちしていましたが、Streamer.bot の既定は「`SendMessage` のみ認証必須」であり、未認証でも購読できます。
- 公式クライアントは、パスワード未指定時に `GetInfo` が通るかで「認証が必須か」を判定します。
- 対応: パスワード未指定かつ Hello に認証情報がある場合は `GetInfo` を送り、成功すれば未認証のまま購読、失敗すれば「パスワードを設定してください」と表示。パスワード指定時は `Authenticate` の応答 `status` を検証。

### CR-08 接続失敗の理由がそのまま UI に出る（修正済み）

- 変更前は `ex.Message` をそのまま `StreamerBotStatusMessage` に表示していました（英語の Win32 メッセージや内部例外名を含む）。
- 対応: `StreamerBotConnectionResult`（`FailureReason` / `Message` / `ShouldRetry`）を導入し、日本語の利用者向けメッセージに変換。

## 4. Low（修正済み）

| ID | 内容 | 対応 |
| --- | --- | --- |
| CL-01 | リクエスト ID が `tco-auth-1` など固定 | 接続ごとの連番に変更 |
| CL-02 | 受信メッセージ長の上限なし | 512 KB で打ち切り（`MaxMessageBytes`） |
| CL-03 | JSON 解析失敗時に受信内容をそのままログ出力 | `source` / `type` のみ記録 |
| CL-04 | `MemberMilestoneChat` という存在しないイベント名を購読 | 公式名 `MemberMileStone` に修正（旧名もマッピングで許容） |
| CL-05 | Kick の `MassGiftSubscription` 未対応 | 購読・マッピングを追加 |
| CL-06 | README の `## プライバシーと認可管理` 見出しが消え、OBS 設定表が重複 | 見出しと表の位置を修正 |
| CL-07 | 「Streamer.bot を有効にすると EventSub / YouTube 接続は不要」という記述が実装と不一致 | 追加経路であること、二重通知の回避方法を明記 |
| CL-08 | テストが無く、プロトコル実装の検証手段が無い | `tests/TwitchChatOverlay.Tests` を追加（16 テスト） |
| CL-09 | Streamer.bot と無関係な `ObsWebSocketService.CreateFailureResult` のリファクタリング（ネスト三項演算子化）が差分に含まれる | 機能変更がないため `origin/main` の実装に戻し、PR の差分を Streamer.bot 関連に限定 |

## 5. Low（受容・文書化）

| ID | 内容 | 判断 |
| --- | --- | --- |
| CA-01 | パスワード欄が平文の `TextBox`（OBS タブと同一） | ローカル設定画面の肩越し閲覧のみ。既存 UI と統一。受容 |
| CA-02 | `ws://` のみで TLS 非対応 | Streamer.bot の WebSocket Server が TLS を持たないため。UI と README に「127.0.0.1 で待ち受けること」を明記 |
| CA-03 | 直接接続と Streamer.bot を併用すると二重通知 | 利用者がフィルターで選択する仕様。README と設定タブに明記 |
| CA-04 | 切断時に WebSocket の Close ハンドシェイクをしない | 既存の OBS 実装と同じ（Cancel + Dispose）。実害なし |
| CA-05 | DI シングルトンをアプリ終了時に破棄しない | プロセス終了で解放される。既存サービスも同様 |
| CA-06 | 接続中に Host / Port を変更しても再接続しない | 変更後に「切断 → 接続」で反映。仕様として明記 |
| CA-07 | Kick の Gift / MassGift の payload が公式未定義 | 複数形状を許容する抽出で対応。実機ログでの確認を推奨 |
| CA-08 | YouTube SuperChat / NewSponsor の payload が公式未定義 | 同上 |
| CA-09 | `Ping` / `Pong` は公式仕様に記載が無い | サーバが送ってきた場合のみ応答する防御的実装。無害 |
| CA-10 | 表示フィルターの組み合わせ（Streamer.bot の Twitch 通知 × 各イベント種別） | README の設定表に記載 |
| CA-11 | ポート入力が XAML バインディング任せ | 既存の OBS タブと同じ。不正値は接続時に `ConfigurationError` |
| CA-12 | リポジトリ全体の `SECURITY_REVIEW.md` に PR 対象外の High が残る | 別スコープ（第 8 章）。`main` をマージした時点で Action の SHA 固定は解消済み |

## 6. 再レビュー（修正後の確認）

修正後の全差分を再読し、以下を確認しました。

- High / Medium の再発なし。フィルター分岐は `ViaStreamerBot` の 1 箇所に集約され、直接接続側のロジックは変更前と等価。
- **2 回目のレビューで追加の不具合を検出**: `message` が文字列の payload に対する `JValue` のインデクサー例外（CR-02 に統合）。`Child()` ヘルパーで修正し、実際の payload 形状に合わせたテストで回帰を防止。
- ハンドシェイクは公式ドキュメントの順序（Hello → Authenticate → Subscribe）と一致。`Authenticate` の応答待ちは公式 TypeScript クライアントの `request()` と同じ `id` 照合。
- 受信ループの例外処理はキャンセル時と異常時を分離し、通常切断で警告ログが出ない。
- 新規の警告は 0（`dotnet build` で確認）。

## 7. プロトコル仕様の確認（Streamer.bot 側）

Streamer.bot の本体 C# ソースは公開されていません（`Streamerbot/Streamer.bot` はリリースとドキュメントの置き場のみ）。そのため、以下を一次情報として突き合わせました。

- 公式ドキュメント: `Streamerbot/docs`
  - `streamerbot/3.api/4.websocket/0.guide/4.authentication.md`（Hello / Authenticate / ハッシュ手順）
  - `streamerbot/3.api/4.websocket/3.requests.md`（`Subscribe` / `GetInfo` / レスポンス形式）
  - `streamerbot/3.api/4.websocket/0.guide/3.events.md`（イベントエンベロープ）
- 公式 TypeScript クライアント: `Streamerbot/client`
  - `ws/StreamerbotClient.ts`（`handshake()` / `authenticate()` / `request()` / `subscribe()`）
  - `ws/types/events/events.const.ts`、`ws/types/events/twitch.types.ts`
- 旧公式 Wiki: `Streamerbot/streamerbot-wiki`（イベント payload の実例）

確認した内容と実装の対応:

| 項目 | 仕様（一次情報） | 実装 |
| --- | --- | --- |
| Hello | 接続直後に `request: "Hello"` + `info`、認証有効時のみ `authentication: { salt, challenge }` | `PerformHandshakeAsync` |
| Authenticate | `base64(SHA256(base64(SHA256(password + salt)) + challenge))` を送信し、`id` 一致の応答 `status` を確認 | `BuildAuthentication` |
| 認証必須の判定 | 公式クライアントはパスワード未指定時に `GetInfo` の成否で判定 | 同じ挙動に修正 |
| Subscribe | `{ request, id, events: { Twitch: [...], YouTube: [...], Kick: [...] } }`、応答は `status` と購読内容の echo | `BuildSubscribeRequest` / `SubscribeAsync` |
| イベント | `{ timeStamp, event: { source, type }, data }` | `HandleMessage` 相当の受信ループ |
| Twitch payload | `Follow` はトップレベル `user_name`、`Sub` は `subTier` / `displayName` など | `MapTwitchEvent` |
| YouTube payload | `Message` は `data.message` が文字列、ユーザーは `data.user.name` | `MapYouTubeEvent` |
| Kick | v1.0.0 以降。イベント型名のみ公式定義、payload は未定義 | `MapKickEvent`（複数形状を許容） |

未検証（実機確認が望ましい）:

- YouTube `SuperChat` / `SuperSticker` / `NewSponsor`、Kick 全般の `data` フィールド（公式定義なし）
- `Ping` / `Pong`（公式資料に記載なし。防御的に応答のみ実装）

## 8. 対象外（参考）

リポジトリ全体を対象とした既存の `SECURITY_REVIEW.md`（2026-11-05）には、PR #10 の差分に含まれない High が 2 件ありました。`main` をマージした時点で、1 つ目は解消されています。

- `sync-to-gitlab.yml` のサードパーティ Action を `@main` で参照 → **`main` 側でコミット SHA 固定と `permissions` 指定が入り解消**
- 開発機の `NuGet.Config` に GitHub PAT を平文保存 → 残存（トークンの失効・再発行が必要なため本 PR では扱わない）

2 つ目は利用者本人によるトークン失効が必要なため、本レビューの対象外です。詳細と対応案は当該文書を参照してください。

## 9. 検証

| 項目 | コマンド | 結果 |
| --- | --- | --- |
| ビルド（アプリ + テスト） | `dotnet build TwitchChatOverlay.slnx -c Debug` | 成功 / 警告 0 / エラー 0 |
| 自動テスト | `dotnet run --project tests/TwitchChatOverlay.Tests/TwitchChatOverlay.Tests.csproj` | 16 件成功 / 失敗 0 |
| CI | `.github/workflows/ci.yml` に Test ステップを追加 | `dotnet run` で実行 |

テストの内訳:

- 認証あり / なしのハンドシェイク、誤パスワード、認証必須時のパスワード未設定
- Kick 購読失敗時の Twitch / YouTube へのフォールバック
- 到達不能サーバー（`Unavailable` / `ShouldRetry`）、不正ホスト（`ConfigurationError`）
- Twitch / YouTube / Kick のイベント変換（ユーザー名・本文・色・金額・サブスク種別）
- 未知イベントの無視、サーバー Ping への応答、切断時の状態遷移
- 表示フィルターの独立性（直接接続と Streamer.bot、チャットと通知、Kick）
