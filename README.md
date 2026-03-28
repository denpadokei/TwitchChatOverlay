# TwitchChatOverlay

TwitchのチャンネルイベントをWindowsデスクトップにトースト通知として表示するオーバーレイアプリです。

## 機能

- **EventSub WebSocket** でTwitchイベントをリアルタイム受信
- 以下のイベントをトースト通知で表示：
  - チャンネルポイント交換
  - レイド
  - フォロー
  - サブスク / ギフトサブ / リサブ
  - ハイプトレイン開始・終了
- **Device Authorization Flow** による安全なOAuth認可（リダイレクトURL不要）
- 起動時に保存済みトークンを自動検証し、チャンネルへ自動接続
- 接続履歴の保存（最大10件）とワンクリック切り替え
- トースト表示位置を4隅から選択（右上 / 左上 / 右下 / 左下）
- 設定はAES-256-CBCで暗号化して保存

## 動作環境

| 項目 | 内容 |
|------|------|
| OS | Windows 10 / 11 |
| ランタイム | .NET 6.0 (Windows) |

## ビルド方法

```bash
dotnet build TwitchChatOverlay/TwitchChatOverlay.csproj
```

または Visual Studio 2022 でソリューションファイル `TwitchChatOverlay.slnx` を開いてビルド。

## セットアップ

### 1. Twitchで認可する

アプリを起動し、**「🌐 Twitchで認可する」** ボタンをクリックします。

1. ブラウザが自動的に `https://www.twitch.tv/activate` を開きます
2. 画面に表示されたコードを入力してください
3. 認可が完了するとトークンが保存されます

> トークンは `%APPDATA%\TwitchChatOverlay\settings.json` に暗号化して保存されます。

### 2. チャンネルに接続する

チャンネル名を入力し、**「▶ 接続」** ボタンをクリックします。  
2回目以降は起動時に自動接続します。

## 設定項目

| 設定 | 説明 |
|------|------|
| チャンネル名 | 監視するTwitchチャンネル名（例: `ninja`） |
| 通知設定 | 各イベント種別ごとに表示/非表示を選択 |
| 表示時間 (秒) | トーストが消えるまでの秒数（1〜30秒） |
| 最大同時表示 | 同時に表示するトーストの最大数（1〜10件） |
| 表示位置 | 右上 / 左上 / 右下 / 左下から選択 |

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
