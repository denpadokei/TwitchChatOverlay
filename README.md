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
- YouTube Live Chat の重複抑止メッセージキャッシュ件数を UI から調整可能
- **Device Authorization Flow** による安全なOAuth認可（リダイレクトURL不要・クライアントシークレット不要）
- **YouTube OAuth (PKCE)** による認可（ローカルコールバック使用）
- YouTube 利用前のポリシー確認と同意ゲート
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
- YouTube 認可情報のローカル削除と Google 側権限取り消しに対応
- 設定はフォーマット識別子付きで暗号化して保存（Windows DPAPI + 旧形式自動移行）

## 動作環境

| 項目 | 内容 |
|------|------|
| OS | Windows 10 / 11 |
| ランタイム | .NET 10.0 (Windows) |
| MSIX 版の対応 OS | Windows 10 1809 (build 17763) 以降 |

## インストール

配布形式は ZIP と MSIX の 2 種類です。

| 形式 | 内容 |
|------|------|
| ZIP (`TwitchChatOverlay-vX.X.X-win-x64.zip`) | 任意のフォルダへ展開し `TwitchChatOverlay.exe` を実行 |
| MSIX (`TwitchChatOverlay-X.X.X-x64.msix`) | Windows のアプリとして登録（スタートメニュー登録・アンインストール・更新に対応） |

### MSIX 版のインストール

1. `TwitchChatOverlay-X.X.X-x64.msix` をダウンロードします。
2. 自己署名証明書で署名されている場合は、初回のみ証明書を信頼させます（管理者権限が必要）。

   ```powershell
   Import-Certificate -FilePath .\TwitchChatOverlay-X.X.X-x64.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople
   ```

3. `.msix` をダブルクリックします（または `Add-AppxPackage .\TwitchChatOverlay-X.X.X-x64.msix`）。

```powershell
# インストール済みか確認
Get-AppxPackage denpadokei.TwitchChatOverlay

# アンインストール
Get-AppxPackage denpadokei.TwitchChatOverlay | Remove-AppxPackage
```

> 証明書を信頼していない状態でインストールすると `0x800B0109`（証明書チェーンが信頼されていない）で失敗します。
> 配布元が証明書を更新した場合は、新しい `.cer` で信頼をやり直してください。

### MSIX 版の注意点

- 設定とログは従来どおり `%APPDATA%\TwitchChatOverlay` を読み書きします。MSIX の書き込み仮想化により、実際のファイルは `%LOCALAPPDATA%\Packages\denpadokei.TwitchChatOverlay_<publisherId>\LocalCache\Roaming\TwitchChatOverlay` に保存されます（読み取りは実ファイルへフォールバックするため、ZIP 版の設定はそのまま引き継がれます）。
  - アンインストールすると仮想化先のファイルは削除されますが、ZIP 版の設定ファイル自体は残ります。
- 更新ファイルは仮想化の影響を受けない `%USERPROFILE%\Downloads\TwitchChatOverlay\updates` に保存されます（1 日以上前の古いファイルは自動削除されます）。
- アプリ内の「更新する」ボタンは、パッケージ版では MSIX をダウンロードして Windows のインストーラー（App Installer）に引き渡します。
  - MSIX が公開されていないリリースでは、自動更新せずリリースページを開きます。
- インストール先は読み取り専用のため、ZIP 版のようなファイル差し替えによる自己更新は行いません。
- 自動更新を成立させるには、すべてのリリースを**同じ署名証明書**で署名してください（証明書が変わると上書き更新できません）。

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

### MSIX パッケージのビルド

MSIX は Visual Studio を必要とせず、Windows SDK の `MakeAppx.exe` / `signtool.exe` だけで作成できます。

| ツール | 用途 |
|--------|------|
| .NET SDK 10.0 | アプリの publish |
| Windows SDK 10 (`MakeAppx.exe`, `signtool.exe`) | パッケージ化と署名 |
| PowerShell 7.0 以降 (`pwsh`) | ビルドスクリプトの実行 |

```powershell
# 自己署名証明書で署名して artifacts/msix に出力
pwsh -File build/Build-Msix.ps1

# リリース用証明書で署名（パスワードは環境変数 MSIX_CERTIFICATE_PASSWORD で渡す）
pwsh -File build/Build-Msix.ps1 -Version 0.5.0 -CertificatePath .\mycert.pfx
```

出力:

| ファイル | 内容 |
|----------|------|
| `artifacts/msix/TwitchChatOverlay-X.X.X-x64.msix` | 署名済みパッケージ |
| `artifacts/msix/TwitchChatOverlay-X.X.X-x64.msix.sha256` | SHA256（`<hash>  <filename>` 形式） |
| `artifacts/msix/TwitchChatOverlay-X.X.X-x64.cer` | 自己署名時の公開証明書 |

主なオプション:

| オプション | 説明 |
|------------|------|
| `-Version` | MSIX のバージョン（省略時は csproj の `<Version>`） |
| `-CertificatePath` / `-CertificatePassword` | リリース署名用 PFX |
| `-CertificateThumbprint` | 証明書ストア（CurrentUser\My）の証明書で署名 |
| `-Publisher` | Identity の Publisher（既定 `CN=denpadokei`。証明書を指定した場合は証明書の Subject を使用） |
| `-Runtime` | `win-x64`（既定）/ `win-arm64` |
| `-NoSign` | 署名しない（検証用） |
| `-KeepLayout` | パッケージ化に使ったレイアウトを残す |

> Identity の `Publisher` は署名証明書の Subject と完全に一致している必要があります。
> `-CertificatePath` / `-CertificateThumbprint` を指定した場合、スクリプトは証明書の Subject を自動的に使用します。

アイコンを差し替えた場合は、タイル画像を再生成してください（生成物は `packaging/Assets` にコミットします）。

```powershell
pwsh -File build/New-MsixAssets.ps1
```

証明書の管理・バージョン規則・手動でのパッケージ化手順は [`packaging/README.md`](packaging/README.md) を参照してください。

### GitHub Actions でのリリースビルド

タグ（`vX.X.X`）を push するとリリースが自動作成されます。  
その際、リポジトリシークレットとして以下を登録してください:

**Settings → Secrets and variables → Actions → New repository secret**

| Name | Value |
|------|-------|
| `TWITCH_CLIENT_ID` | Twitch Developer Portal の Client ID |
| `YOUTUBE_CLIENT_ID` | Google Cloud の OAuth Client ID |
| `YOUTUBE_CLIENT_SECRET` | Google Cloud の OAuth Client Secret |
| `MSIX_CERTIFICATE_BASE64` | MSIX 署名用 PFX を base64 エンコードした文字列（未設定なら MSIX は作成されません） |
| `MSIX_CERTIFICATE_PASSWORD` | PFX のパスワード |

MSIX 署名用 PFX の base64 化:

```powershell
[Convert]::ToBase64String([IO.File]::ReadAllBytes('.\mycert.pfx')) | Set-Clipboard
```

> 証明書は**リリース間で共通**のものを使用してください。証明書が変わると、インストール済みの MSIX を上書き更新できません。

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

YouTube の認可と接続は、ポリシー確認後に同意チェックを有効にしない限り実行できません。

1. ブラウザでGoogle認可画面が開きます
2. 権限を許可するとローカルコールバックでトークンを保存します
3. 認可が完了したら **「▶ 接続」** を押します

> YouTube 接続には配信中の `liveChatId` が必要です。チャット受信自体は gRPC ストリームを使用し、配信がまだ開始されていない場合のみ `liveBroadcasts` を 30 秒間隔で確認して待機します。
- YouTube の gRPC proto は完全版スキーマに追従しており、Live Chat / Super Chat / メンバーシップ系イベントの詳細フィールドを将来拡張しやすい形で保持しています。

> YouTube OAuth は `https://www.googleapis.com/auth/youtube.readonly` のみを要求します。

### YouTube 接続トラブル: クォーター枯渇

YouTube への接続が急にできなくなった場合、アプリ設定や認可ミスではなく Google 側のクォーター枯渇が原因のことがあります。

- 参考: https://issuetracker.google.com/issues/476293143
- 要するに Google 側の都合なので、くそくらえです。

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
- 同意チェックが未完了の場合、YouTube の新規認可、手動接続、自動接続は実行されません
- Google 側で手動管理したい場合は `https://security.google.com/settings/security/permissions` からアクセス権を取り消せます
- 配布物には同梱文書として `Docs/PrivacyPolicy.html` / `Docs/TermsOfUse.html` と、審査提出向けの `Docs/PrivacyPolicy.en.html` / `Docs/TermsOfUse.en.html` が含まれます
- GitHub Actions の Release / CI では、publish 出力にこれらの文書が含まれていることを検証しています
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
| YouTube重複抑止キャッシュ件数 | 再接続時の旧コメント再表示を抑えるために保持する YouTube メッセージID件数。100以上で任意設定可能 |
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
