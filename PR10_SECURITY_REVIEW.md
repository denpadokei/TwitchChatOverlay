# PR #10 セキュリティレビュー

- 対象: PR #10 `Integrate Streamer.bot with settings and notifications support`（`other-commenttool` → `main`）
- レビュー日: 2026-11-20
- 対象範囲: 本 PR の差分（Streamer.bot WebSocket 連携、設定タブ、通知フィルター、CI 変更）
- 手法: 差分の全読、データフロー追跡（WebSocket → JSON → 通知 → WPF 描画 / ログ / 設定ファイル）、Streamer.bot 公式プロトコルとの突き合わせ、依存パッケージ監査
- 重要度の定義
  - **High**: 認証情報の漏洩、リモートからの任意コード実行、権限昇格など
  - **Medium**: ローカルまたは同一ネットワーク上の攻撃者、あるいは細工されたサーバー応答で実害が出る
  - **Low**:  hardening、情報露出の軽微なもの、仕様上の制約

## 1. サマリ

| 重要度 | 初回 | 修正後 |
| --- | --- | --- |
| High | 0 | **0** |
| Medium | 2 | **0** |
| Low | 7 | **7（受容・緩和済み）** |
| Info | 2 | 2 |

Streamer.bot 連携は「クライアントから接続しに行く」構成であり、アプリ自身は待ち受けポートを開きません。受信データは `JObject` として扱われ、型逆シリアライズやスクリプト評価は行いません。表示は WPF の `TextBlock` で、HTML / マークアップとして解釈されません。このため攻撃面は「接続先 WebSocket サーバーの応答」と「ローカル設定ファイル」に限定されます。

## 2. Medium（修正済み）

### SEC-01 受信したイベント本文をログに残していた（修正済み）

`StreamerBotService.HandleMessage`（変更前）

- JSON 解析に失敗すると、受信した生メッセージの先頭 200 文字を警告ログに出力していました。
- チャット本文・ユーザー名・ユーザー ID が `%APPDATA%\TwitchChatOverlay\logs` に平文で残り、配布用ログやサポート依頼への添付で意図せず漏洩する恐れがあります。
- 対応: ログには `event.source` / `event.type` のみを記録し、本文は出力しない。

### SEC-02 受信メッセージ長に上限が無かった（修正済み）

`StreamerBotService.ReceiveLoopAsync`（変更前）

- 1 メッセージを `StringBuilder` に無制限に連結していました。接続先サーバーが巨大なフレームを送り続けるとメモリを枯渇させられます（ローカルまたは同一ネットワーク上の攻撃者、あるいは侵害された Streamer.bot）。
- 対応: 512 KB を超えた時点で `StreamerBotProtocolException` を投げ、受信ループを終了。

## 3. Low（受容・緩和）

### SEC-03 `ws://` 平文通信（Low）

- Streamer.bot の WebSocket Server は TLS を提供しないため、`wss://` は選択できません。
- パスワード自体は送信されず、`salt` + `challenge` に対する SHA-256 のチャレンジレスポンスのみが流れます。ただし同一ネットワーク上では通信内容（チャット等）を傍受できます。
- 緩和: 設定タブと README に「WebSocket Server は `127.0.0.1` で待ち受けること」「通信は暗号化されないこと」を明記。接続先ホストの検証（`Uri.CheckHostName`）も追加。

### SEC-04 設定ファイル内のパスワード（Low / 問題なし）

- `StreamerBotPassword` は既存の `SettingsService` により DPAPI（`CurrentUser`）で暗号化して保存されます。平文保存はありません。
- 復号できるのは同一 Windows ユーザーのみです。既存の OAuth トークンと同じ扱いです。

### SEC-05 パスワード欄が平文表示（Low）

- Streamer.bot タブのパスワードは `TextBox` で表示されます（既存の OBS パスワード欄と同じ）。
-  shoulder-surfing / 画面共有時の露出のみ。ローカル設定画面の範囲であり、受容。

### SEC-06 通知フラッド（Low）

- 接続先がイベントを大量送信するとトーストが連続表示されます。
- 既存の `ToastMaxCount` と表示時間で同時表示数は制限されます。音声も既存の設定で無効化可能。実害は限定的。

### SEC-07 接続先ホストの任意指定（Low）

- Host は利用者が自由入力でき、`ws://` の接続先を任意に変更できます（SSRF に近い性質）。
- ただし接続は利用者の明示操作（接続ボタン / 起動時設定）で行われ、行き先は利用者自身が指定します。ホスト名として不正な値（URL・パス）は拒否する検証を追加済み。
- 受容。

### SEC-08 イベント文字列の描画（Low / 問題なし）

- `Username` / `DisplayText` / `SubText` は WPF `TextBlock.Text` にバインドされ、HTML や XAML マークアップとして評価されません。
- `UserColor` は `#RRGGBB` 形式のみ受け付ける既存の検証（`ToastNotificationViewModel`）を通ります。書式外は白で描画。
- 参考: 通知経由で任意の表示文字列を出せますが、コード実行・ファイル操作には至りません。

### SEC-09 ログに接続先ホスト名が残る（Low）

- 接続失敗時の例外ログにホスト名・ポートが含まれます。認証情報は含みません。

## 4. Info

- **SEC-10** 新規の実行時依存パッケージはありません（`xunit.v3` はテストプロジェクトのみ）。`dotnet list package --vulnerable --include-transitive` で既知脆弱性なしを確認。
- **SEC-11** テスト用の擬似 Streamer.bot サーバーは `127.0.0.1` の動的ポートにのみバインドします。

## 5. 修正後の再評価

- 受信データの取り扱い（ログ・サイズ上限・描画）を修正した結果、Medium は 0 件。
- 残る Low は「Streamer.bot 側が TLS を持たない」「ローカル設定画面の表示」など、アプリ側だけでは解消できない制約、または利用者判断に委ねるものです。
- 認証情報の取り扱いは既存の DPAPI 保存を踏襲しており、新たな漏洩経路は追加していません。

## 6. 対象外（参考）

リポジトリ全体の `SECURITY_REVIEW.md`（2026-11-05）には、本 PR の差分外に High が 2 件残っています。

1. `.github/workflows/sync-to-gitlab.yml` がサードパーティ Action を `@main` で参照（CI サプライチェーン）
2. 開発機の `%APPDATA%\NuGet\NuGet.Config` に GitHub PAT を平文保存（トークンの失効・再発行が必要）

1 は CI 運用の変更、2 は利用者本人によるトークン失効が必要なため、本 PR のセキュリティレビューでは対象外としています。詳細と対応案は当該文書を参照してください。

## 7. 検証

| 項目 | コマンド | 結果 |
| --- | --- | --- |
| 依存パッケージの脆弱性 | `dotnet list TwitchChatOverlay/TwitchChatOverlay.csproj package --vulnerable --include-transitive` | 既知の脆弱性なし |
| ビルド | `dotnet build TwitchChatOverlay.slnx -c Debug` | 成功 / 警告 0 |
| 自動テスト | `dotnet run --project tests/TwitchChatOverlay.Tests/TwitchChatOverlay.Tests.csproj` | 16 件成功 |
