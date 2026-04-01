# TwitchChatOverlay

Twitch / YouTube のチャンネルイベントをWindowsデスクトップにトースト通知として表示するオーバーレイアプリです。

## 機能

- **EventSub WebSocket** でTwitchイベントをリアルタイム受信
- **YouTube Live Chat API** のポーリングでYouTubeイベントを受信
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
- **リフレッシュトークンによる自動更新**（アクセストークン期限切れ時に自動再認証・再接続）
- 起動時に保存済みトークンを自動検証し、チャンネルへ自動接続
- 接続中に切断された場合、自動でトークン更新・再接続
- 接続履歴の保存（最大10件）とワンクリック切り替え
- Twitchエモート表示対応（静止画・アニメーションGIF）
- 設定はAES-256-CBCで暗号化して保存

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

フォークして開発する場合は、Twitch Developer Portal と Google Cloud Console でアプリを登録し、取得した ClientId を以下の手順で設定してください。

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
6. 必要なら `New Secret` でClient Secretを発行
  - 本アプリ実装ではClient Secretは使用しませんが、将来拡張用に保持するのは可

補足:
- Twitchの認可はDevice Authorization Flowを使うため、Client IDのみで動作します。
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
4. OAuth同意画面を設定
  - `API とサービス` → `OAuth 同意画面`
  - User Type は通常 `外部`
  - App name / User support email / Developer contact を入力
  - スコープに `.../auth/youtube.readonly` を追加
  - テストユーザーを使う場合は自分のGoogleアカウントを追加
5. OAuthクライアントIDを作成
  - `API とサービス` → `認証情報` → `認証情報を作成` → `OAuth クライアント ID`
  - アプリケーションの種類: `デスクトップアプリ`
  - 作成後に表示される `クライアント ID` をコピー
6. ローカルコールバックを確認
  - 本アプリは `http://127.0.0.1:18765/callback/` で待ち受けます。
  - デスクトップアプリ種別なら通常はloopback redirectが許可されます。

補足:
- 公開前のOAuth同意画面（テスト状態）では、テストユーザー以外は認可できません。
- `access blocked` が出る場合は、OAuth同意画面の必須項目とテストユーザー設定を確認してください。
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
2回目以降は起動時に自動接続します。

### 3. YouTubeで認可する

`YouTube` タブを開き、**「🌐 YouTubeで認可する」** ボタンをクリックします。

1. ブラウザでGoogle認可画面が開きます
2. 権限を許可するとローカルコールバックでトークンを保存します
3. YouTubeの接続先（チャンネルID / ハンドル）を入力して **「▶ 接続」** を押します

> YouTubeは配信中（liveBroadcast active）の liveChatId が必要です。配信が開始されていない場合は接続できません。

### 4. Google Cloud 側の設定

- OAuth クライアントタイプはデスクトップアプリ推奨
- ループバックURIを許可（実装では `http://127.0.0.1:18765/callback/` を使用）
- YouTube Data API v3 を有効化

## 設定項目

### 通知設定

| 設定 | 説明 |
|------|------|
| チャンネル名 | 監視するTwitchチャンネル名（例: `ninja`） |
| 各イベントの ON/OFF | チャット / 報酬 / レイド / フォロー / サブスク / ギフトサブ / リサブ / ハイプトレイン |
| 表示時間 (秒) | トーストが消えるまでの秒数（1〜30秒） |
| 最大同時表示 | 同時に表示するトーストの最大数（1〜10件） |

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
| [Prism.DryIoc](https://github.com/PrismLibrary/Prism) | 8.1.97 | MVVM / DI |
| [Newtonsoft.Json](https://www.newtonsoft.com/json) | 13.0.3 | JSON解析 |
| [XamlAnimatedGif](https://github.com/XamlAnimatedGif/XamlAnimatedGif) | 2.3.1 | アニメーションGIF表示 |
