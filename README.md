# TwitchChatOverlay

Twitch / YouTube のチャンネルイベントを Windows デスクトップにトースト通知として表示するオーバーレイアプリです。

## 機能

- **EventSub WebSocket** で Twitch イベントをリアルタイム受信
- YouTube は**配信検出（liveChatId解決）をポーリング**し、**チャット / イベント受信は gRPC ストリーム**で処理
- `liveBroadcasts` による配信確認で YouTube の配信開始を待機
- 以下のイベントをトースト通知で表示：
  - チャットメッセージ
  - チャンネルポイント交換
  - レイド
  - フォロー
  - サブスク / ギフトサブ / リサブ
  - ハイプトレイン開始・終了
- YouTubeコメント / Super Chat / メンバーシップ通知
- **Device Authorization Flow** による安全なOAuth認可（リダイレクトURL不要・クライアントシークレット不要）
- **YouTube OAuth (PKCE)** による認可（ローカルコールバック使用）
- **リフレッシュトークンによる自動更新**（アクセストークン期限切れ時に自動更新・再接続）
- 起動時に保存済みトークンを自動検証し、チャンネルへ自動接続
- 接続中に切断された場合、自動でトークン更新・再接続
- 接続履歴の保存（最大10件）とワンクリック切り替え
- Twitch エモート表示対応（静止画・アニメーション GIF）
- コメント通知音に対応
  - 通知音 ON/OFF
  - 埋め込み音源 / 外部ファイル切り替え
  - `wav` / `mp3` / `ogg` の再生
  - 音量調整
  - 出力音声デバイス選択
- プレビュー機能に対応
  - 共通タブから通知音プレビュー
  - 共通 / Twitch / YouTube タブからコメント表示プレビュー
- 設定はフォーマット識別子付きで暗号化して保存（Windows DPAPI + 旧形式自動移行）

## 動作環境

| 項目 | 内容 |
|------|------|
| OS | Windows 10 / 11 |
| ランタイム | .NET 10.0 (Windows) |

## ビルド方法

### 必要なもの

| ツール | バージョン |
|--------|-----------|
| [Visual Studio 2026](https://visualstudio.microsoft.com/) 以降 | **.NET デスクトップ開発** ワークロードを含むこと |
| [.NET SDK 10.0](https://dotnet.microsoft.com/download/dotnet/10.0) 以降 | VS 2026 に同梱 |
| Windows PowerShell | 5.1 以降（スクリプト実行に必要） |

### ローカルビルド（シークレットなし）

```bash
git clone https://github.com/denpadokei/TwitchChatOverlay.git
cd TwitchChatOverlay
dotnet build TwitchChatOverlay/TwitchChatOverlay.csproj
```

またはソリューションファイル `TwitchChatOverlay.slnx` を Visual Studio 2026 以降で開いてビルド。

> シークレットなしのビルドでは `BuildSecrets.ClientId` / `BuildSecrets.YouTubeClientId` が空文字になります。  
> 動作確認には後述の「ローカルシークレット設定」が必要です。

### ローカルシークレット設定

フォークして開発する場合は、Twitch Developer Portal と Google Cloud Console でアプリを登録し、取得した Client ID を以下の手順で設定してください。

#### Twitch Client ID の取得手順（詳細）

1. Twitch Developer Console を開く
  - URL: https://dev.twitch.tv/console/apps
  - Twitchアカウントでログインします。
2. `Register Your Application` をクリック
3. アプリ情報を入力
  - Name: 任意（例: TwitchChatOverlay-Local）
  - OAuth Redirect URLs: `http://localhost`（本アプリはDevice Flowなので実質未使用ですが、登録時に必須）
  - Category: `Application Integration` など任意
4. `Create` でアプリを作成
5. 作成したアプリ詳細画面で `Client ID` をコピー
6. 必要なら `New Secret` で Client Secret を発行
  - 本アプリ実装では Client Secret は使用しませんが、将来拡張用に保持するのは可

補足:
- Twitch の認可は Device Authorization Flow を使うため、Client ID のみで動作します。
- OAuth Redirect URLs はTwitchの仕様上、アプリ登録時に入力が必要です。

#### YouTube Client ID の取得手順（詳細）

1. Google Cloud Console を開く
  - URL: https://console.cloud.google.com/
  - Googleアカウントでログインします。
2. プロジェクトを作成または選択
  - 例: `twitch-chat-overlay-local`
3. YouTube Data API v3 を有効化
  - `API とサービス` → `ライブラリ`
  - `YouTube Data API v3` を検索して `有効にする`
4. OAuth 同意画面を設定
  - `API とサービス` → `OAuth 同意画面`
  - User Type は通常 `外部`
  - App name / User support email / Developer contact を入力
  - スコープに `.../auth/youtube.readonly` を追加
  - テストユーザーを使う場合は自分のGoogleアカウントを追加
5. OAuth クライアント ID を作成
  - `API とサービス` → `認証情報` → `認証情報を作成` → `OAuth クライアント ID`
  - アプリケーションの種類: `デスクトップアプリ`
  - 作成後に表示される `クライアント ID` をコピー
6. ローカルコールバックを確認
  - 本アプリは `http://127.0.0.1:18765/callback/` で待ち受けます。
  - デスクトップアプリ種別なら通常は loopback redirect が許可されます。

補足:
- 公開前の OAuth 同意画面（テスト状態）では、テストユーザー以外は認可できません。
- `access blocked` が出る場合は、OAuth 同意画面の必須項目とテストユーザー設定を確認してください。
- API未有効化だと YouTube 接続時に 403 系エラーになります。

```powershell
# テンプレートをコピー
Copy-Item build/local.props.example build/local.props
```

`build/local.props` を開いて値を入力します:

```xml
<Project>
  <PropertyGroup>
    <TwitchClientId>your_client_id_here</TwitchClientId>
    <YouTubeClientId>your_youtube_client_id_here</YouTubeClientId>
    <YouTubeClientSecret>your_youtube_client_secret_here</YouTubeClientSecret>
  </PropertyGroup>
</Project>
```

> `build/local.props` は `.gitignore` に含まれているため git にはコミットされません。

### GitHub Actions でのリリースビルド

タグ（`vX.X.X`）を push するとリリースが自動作成されます。  
その際、リポジトリシークレットとして以下を登録してください:

**Settings → Secrets and variables → Actions → New repository secret**

| Name | Value |
|------|-------|
| `TWITCH_CLIENT_ID` | Twitch Developer Portal の Client ID |
| `YOUTUBE_CLIENT_ID` | Google Cloud の OAuth Client ID |
| `YOUTUBE_CLIENT_SECRET` | Google Cloud の OAuth Client Secret |

## セットアップ

### 1. Twitchで認可する

アプリを起動し、**「🌐 Twitchで認可する」** ボタンをクリックします。

1. ブラウザが自動的に `https://www.twitch.tv/activate` を開きます
2. 画面に表示されたコードを入力してください
3. 認可が完了するとトークンが保存されます

> トークンは `%APPDATA%\TwitchChatOverlay\settings.json` に暗号化して保存されます。  
> リフレッシュトークンも一緒に保存され、アクセストークンの期限切れ時に自動更新されます。

### 2. チャンネルに接続する

チャンネル名を入力し、**「▶ 接続」** ボタンをクリックします。  
2 回目以降は起動時に自動接続します。

### 3. YouTubeで認可する

`YouTube` タブを開き、**「🌐 YouTubeで認可する」** ボタンをクリックします。

認可前に、同じタブから次の文書を確認してください。

- 同梱のプライバシーポリシー
- 同梱の利用条件
- YouTube 利用規約
- Google Privacy Policy

1. ブラウザでGoogle認可画面が開きます
2. 権限を許可するとローカルコールバックでトークンを保存します
3. 認可が完了したら **「▶ 接続」** を押します

> YouTube 接続には配信中の `liveChatId` が必要です。チャット受信自体は gRPC ストリームを使用し、配信がまだ開始されていない場合のみ `liveBroadcasts` を 30 秒間隔で確認して待機します。

> YouTube OAuth は `https://www.googleapis.com/auth/youtube.readonly` のみを要求します。

### 4. Google Cloud 側の設定

- OAuth クライアントタイプはデスクトップアプリ推奨
- ループバックURIを許可（実装では `http://127.0.0.1:18765/callback/` を使用）
- YouTube Data API v3 を有効化

### 5. OBS 連携設定（任意）

YouTube の配信開始検出を OBS WebSocket と連携できます（任意）。

- OBS を使う場合:
  - OBS 側で WebSocket サーバーを有効化（通常ポート: `4455`）
  - 必要ならパスワードを設定
  - アプリ側で OBS 設定を有効にすると、配信開始イベント検出後に YouTube の配信確認（30 秒間隔）を開始
- OBS を使わない場合:
  - YouTube の **「▶ 接続」** 実行時に 1 回だけ即時配信確認
  - 未配信時はその後 30 秒間隔で配信確認し、配信検出後に gRPC ストリームへ接続

現在の OBS 設定値は `%APPDATA%\TwitchChatOverlay\settings.json` に保存されます（暗号化保存）。

## プライバシーと認可管理

- YouTube OAuth トークンとリフレッシュトークンは `%APPDATA%\TwitchChatOverlay\settings.json` に暗号化保存されます
- YouTube タブから、ローカル保存済みの認可情報削除と Google 側の権限取り消しを実行できます
- Google 側で手動管理したい場合は `https://security.google.com/settings/security/permissions` からアクセス権を取り消せます
- 配布物には同梱文書として `Docs/PrivacyPolicy.html` と `Docs/TermsOfUse.html` が含まれます
- 質問やプライバシーに関する連絡先は GitHub Discussions です: `https://github.com/denpadokei/TwitchChatOverlay/discussions`

| キー | 説明 | 既定値 |
|------|------|--------|
| `ObsWebSocketEnabled` | OBS 連携を有効化するか | `false` |
| `ObsWebSocketHost` | OBS WebSocket の接続先ホスト | `127.0.0.1` |
| `ObsWebSocketPort` | OBS WebSocket の接続先ポート | `4455` |
| `ObsWebSocketPassword` | OBS WebSocket の接続パスワード | 空文字 |

## 設定項目

### 通知設定

| 設定 | 説明 |
|------|------|
| チャンネル名 | 監視するTwitchチャンネル名（例: `ninja`） |
| 各イベントの ON/OFF | チャット / 報酬 / レイド / フォロー / サブスク / ギフトサブ / リサブ / ハイプトレイン |
| YouTube通知 ON/OFF | YouTube Chat / Super Chat / Membership の表示切替 |
| 表示時間 (秒) | トーストが消えるまでの秒数（1〜30秒） |
| 最大同時表示 | 同時に表示するトーストの最大数（1〜10件） |
| 通知音 ON/OFF | コメント受信時の通知音を有効 / 無効に切り替え |
| 通知音プレビュー | 現在の音源・音量・出力デバイス設定で試聴 |

### トースト外観設定

| 設定 | 説明 |
|------|------|
| 表示位置 | 右上 / 左上 / 右下 / 左下から選択 |
| モニター | マルチモニター環境で表示先を選択 |
| 文字サイズ | 8〜24pt（スライダーまたは直接入力） |
| 横幅 | 200〜600px（スライダーまたは直接入力） |
| 背景の濃さ | 0〜100%（スライダーまたは直接入力） |
| フォント | プリセットから選択または直接入力（空白でシステムデフォルト） |
| 背景色 | ダーク / ライト / システム設定 / カスタム（HEX指定）から選択 |
| 表示プレビュー | 共通 / Twitch / YouTube 各タブから現在の見え方を確認 |

### 通知音設定

| 設定 | 説明 |
|------|------|
| 音源 | 埋め込み音源または外部ファイルを選択 |
| 音源ファイル | 任意の `wav` / `mp3` / `ogg` ファイルを指定 |
| 音量 | 0〜100% で調整 |
| 出力デバイス | 既定の出力デバイスまたは任意の音声デバイスを選択 |

設定変更後は **「💾 設定を保存」** ボタンで保存してください。

## 必要なOAuthスコープ

| スコープ | 用途 |
|----------|------|
| `user:read:chat` | チャットイベント受信 |
| `channel:read:redemptions` | チャンネルポイント交換 |
| `moderator:read:followers` | フォロー通知 |
| `channel:read:subscriptions` | サブスク通知 |
| `channel:read:hype_train` | ハイプトレイン通知 |

## 使用ライブラリ

| ライブラリ | バージョン | 用途 |
|-----------|-----------|------|
| [Grpc.Net.Client](https://github.com/grpc/grpc-dotnet) | 2.70.0 | YouTube Live Chat gRPC クライアント |
| [Google.Protobuf](https://github.com/protocolbuffers/protobuf) | 3.28.0 | Protocol Buffers メッセージ定義 |
| [NAudio](https://github.com/naudio/NAudio) | 2.2.1 | 通知音再生 / 音声デバイス選択 |
| [NAudio.Vorbis](https://github.com/naudio/Vorbis) | 1.5.0 | OGG 再生 |
| [Prism.DryIoc](https://github.com/PrismLibrary/Prism) | 8.1.97 | MVVM / DI |
| [Newtonsoft.Json](https://www.newtonsoft.com/json) | 13.0.3 | JSON解析 |
| [XamlAnimatedGif](https://github.com/XamlAnimatedGif/XamlAnimatedGif) | 2.3.1 | アニメーションGIF表示 |
