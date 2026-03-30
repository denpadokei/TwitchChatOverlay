# TwitchChatOverlay

TwitchのチャンネルイベントをWindowsデスクトップにトースト通知として表示するオーバーレイアプリです。

## 機能

- **EventSub WebSocket** でTwitchイベントをリアルタイム受信
- 以下のイベントをトースト通知で表示：
  - チャットメッセージ
  - チャンネルポイント交換
  - レイド
  - フォロー
  - サブスク / ギフトサブ / リサブ
  - ハイプトレイン開始・終了
- **Device Authorization Flow** による安全なOAuth認可（リダイレクトURL不要）
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
| [PowerShell](https://github.com/PowerShell/PowerShell) | 7.x 以降（スクリプト実行に必要） |

### ローカルビルド（シークレットなし）

```bash
git clone https://github.com/denpadokei/TwitchChatOverlay.git
cd TwitchChatOverlay
dotnet build TwitchChatOverlay/TwitchChatOverlay.csproj
```

またはソリューションファイル `TwitchChatOverlay.slnx` を Visual Studio 2026 以降で開いてビルド。

> シークレットなしのビルドでは `BuildSecrets.ClientId` / `BuildSecrets.ClientSecret` が空文字になります。  
> 動作確認には後述の「ローカルシークレット設定」が必要です。

### ローカルシークレット設定

フォークして開発する場合は、Twitch Developer Portal でアプリを登録し、取得した ClientId / ClientSecret を以下の手順で設定してください。

```powershell
# テンプレートをコピー
Copy-Item build/local.props.example build/local.props
```

`build/local.props` を開いて値を入力します:

```xml
<Project>
  <PropertyGroup>
    <TwitchClientId>your_client_id_here</TwitchClientId>
    <TwitchClientSecret>your_client_secret_here</TwitchClientSecret>
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
| `TWITCH_CLIENT_SECRET` | Twitch Developer Portal の Client Secret |

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
