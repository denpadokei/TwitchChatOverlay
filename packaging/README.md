# MSIX パッケージ

TwitchChatOverlay を MSIX として配布するためのファイルと手順です。

## 構成

| パス | 内容 |
|------|------|
| `packaging/AppxManifest.xml` | マニフェストのテンプレート（二重中括弧のプレースホルダーをビルド時に置換） |
| `packaging/Assets/*.png` | タイル / ストアロゴ（`build/New-MsixAssets.ps1` で `icon.ico` から生成） |
| `build/Build-Msix.ps1` | publish → マニフェスト配置 → `MakeAppx` → 署名 → SHA256 出力 |
| `build/New-MsixAssets.ps1` | タイル画像の再生成 |

ビルドには .NET SDK 10 と Windows SDK 10（`MakeAppx.exe` / `signtool.exe`）、PowerShell 7 だけが必要で、Visual Studio は使いません。

## 基本

```powershell
# 自己署名で作成（開発・検証用）
pwsh -File build/Build-Msix.ps1

# リリース署名（パスワードは環境変数で渡す）
$env:MSIX_CERTIFICATE_PASSWORD = '<pfx password>'
pwsh -File build/Build-Msix.ps1 -Version 0.5.0 -CertificatePath .\mycert.pfx
```

出力は `artifacts/msix/` です。

| ファイル | 内容 |
|----------|------|
| `TwitchChatOverlay-<version>-<arch>.msix` | 署名済みパッケージ |
| `TwitchChatOverlay-<version>-<arch>.msix.sha256` | `<hash>  <filename>` 形式のチェックサム（アプリ内更新が検証に使用） |
| `TwitchChatOverlay-<version>-<arch>.cer` | 自己署名時の公開証明書 |

## Identity と Publisher

`AppxManifest.xml` の `Identity` は次の値で生成されます。

| 属性 | 値 | 備考 |
|------|----|------|
| `Name` | `denpadokei.TwitchChatOverlay` | `-IdentityName` で変更可能。変更すると別アプリ扱いになり、更新も設定の引き継ぎも行われない |
| `Publisher` | 署名証明書の Subject | 証明書を指定しない場合は `CN=denpadokei`（`-Publisher` で変更） |
| `Version` | `x.y.z.0` | `-Version` または csproj の `<Version>` |
| `ProcessorArchitecture` | `x64` / `arm64` | `-Runtime` から決定 |

**Publisher は署名証明書の Subject と完全一致している必要があります。** 証明書を指定した場合、スクリプトは証明書の Subject をそのまま使うため、通常は意識する必要はありません。

## 証明書

### リリース用

1. コード署名用の証明書（PFX）を用意します。CA 発行の証明書か、組織内で配布する自己署名証明書を使用してください。
2. PFX を base64 にして GitHub のシークレット `MSIX_CERTIFICATE_BASE64` に登録し、パスワードを `MSIX_CERTIFICATE_PASSWORD` に登録します。

   ```powershell
   [Convert]::ToBase64String([IO.File]::ReadAllBytes('.\mycert.pfx')) | Set-Clipboard
   ```

3. リリースタグを push すると、署名済み MSIX が Release に添付されます。

署名には PFX を一時的に `Cert:\CurrentUser\My` へインポートし、`signtool /sha1` で署名したあと削除する方式を使います（PFX のパスワードを signtool の引数に載せないため）。署名証明書の公開鍵は常に `.cer` として出力され、Release にも添付されます。

**証明書はリリース間で共通にしてください。** 証明書が変わると Publisher が変わり、インストール済みのパッケージを上書き更新できません（いったんアンインストールが必要になります）。証明書の有効期限が切れる前に更新し、タイムスタンプ署名（既定で `http://timestamp.digicert.com`）を有効にしておくと、期限切れ後も既存パッケージの署名検証が通りやすくなります。

### 自己署名（開発・検証用）

証明書を指定しない場合、`CN=denpadokei` の自己署名証明書を `Cert:\CurrentUser\My` に作成（2 回目以降は再利用）し、公開証明書を `.cer` として出力します。

インストール先の PC で証明書を信頼させる必要があります（管理者権限）。

```powershell
Import-Certificate -FilePath .\TwitchChatOverlay-X.X.X-x64.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople
```

不要になったら削除します。

```powershell
Get-ChildItem Cert:\LocalMachine\TrustedPeople | Where-Object Subject -eq 'CN=denpadokei' | Remove-Item
```

## バージョン

- MSIX のバージョンは必ず 4 桁（`Major.Minor.Build.Revision`）です。3 桁で指定した場合は 4 桁目に `0` が補われます。
- 各要素は `0`〜`65535` です。
- インストール済みパッケージを更新するには、**以前のバージョンより大きい**必要があります。
- リリースタグ（`v0.5.0`）と csproj の `<Version>` を揃えてください。Release ワークフローはタグから `-Version` を渡します。

## 手動でのパッケージ化

スクリプトを使わずに確認したい場合の手順です。

```powershell
# 1. レイアウトを作成
dotnet publish TwitchChatOverlay/TwitchChatOverlay.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=false -o artifacts/msix/layout

# 2. マニフェストとアセットを配置
Copy-Item packaging/Assets artifacts/msix/layout/Assets -Recurse
# packaging/AppxManifest.xml のプレースホルダーを置換して layout/AppxManifest.xml に保存

# 3. パッケージ化と署名
& "${env:ProgramFiles(x86)}\Windows Kits\10\bin\10.0.26100.0\x64\makeappx.exe" pack /d artifacts/msix/layout /p out.msix /o
& "${env:ProgramFiles(x86)}\Windows Kits\10\bin\10.0.26100.0\x64\signtool.exe" sign /fd SHA256 /a out.msix
```

## 検証

```powershell
# パッケージの中身を確認（マニフェストの検証も同時に行われる）
& "${env:ProgramFiles(x86)}\Windows Kits\10\bin\10.0.26100.0\x64\makeappx.exe" unpack /p out.msix /d unpacked /o

# 署名の確認（証明書を信頼済みの場合）
& "${env:ProgramFiles(x86)}\Windows Kits\10\bin\10.0.26100.0\x64\signtool.exe" verify /pa out.msix

# インストール / アンインストール
Add-AppxPackage .\out.msix
Get-AppxPackage denpadokei.TwitchChatOverlay
Get-AppxPackage denpadokei.TwitchChatOverlay | Remove-AppxPackage
```

## アプリ側の MSIX 対応

| 項目 | 挙動 |
|------|------|
| 設定・ログ | `%APPDATA%\TwitchChatOverlay` を読み書きする。MSIX の書き込み仮想化により実体は `%LOCALAPPDATA%\Packages\<PFN>\LocalCache\Roaming\TwitchChatOverlay` に保存され、読み取りは実ファイルへフォールバックする（ZIP 版の設定はそのまま使われる） |
| 自動更新 | `AppPackaging.IsPackaged` で判定し、MSIX 版は `.msix` アセットのみをダウンロードして App Installer に引き渡す。MSIX が無いリリースではリリースページを開く |
| 更新ファイル | MSIX 版は仮想化対象外の `%USERPROFILE%\Downloads\TwitchChatOverlay\updates` に保存する（`%TEMP%` は仮想化され、パッケージ外の App Installer から見えないため） |
| 自己更新 | MSIX 版では exe / zip によるファイル差し替えを行わない（インストール先が読み取り専用のため） |

## トラブルシューティング

| 症状 | 原因と対処 |
|------|-----------|
| `0x800B0109` / 証明書エラー | 署名証明書が信頼されていない。`.cer` を `LocalMachine\TrustedPeople` にインポートする |
| `0x80073CF3` / 展開エラー | パッケージ内のファイルが壊れている、または既存インストールと Identity が競合している。`Get-AppxPackage` で確認し、必要ならアンインストールしてから再インストール |
| `0x80073CF9` / 登録エラー | 別ユーザーが同じパッケージをインストールしている、またはインストール先の空き容量不足 |
| 更新できない | 新しいパッケージの `Version` が古い、または `Publisher`（署名証明書）が異なる |
| `makeappx` が見つからない | Windows SDK をインストールするか `-WindowsSdkBin` で `MakeAppx.exe` のあるフォルダを指定する |
