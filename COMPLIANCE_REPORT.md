# TwitchChatOverlay Compliance Report

## 対象

- YouTube API Services Terms of Service
- YouTube Developer Policies
- Google Privacy Policy

対象範囲は、このリポジトリ内の実装と同梱文書です。Google Cloud Console などの外部運用設定は、コード内から確認できないため別途チェックリストとして扱います。

## 修正前の主要ギャップ

- 公開プライバシーポリシーが存在しない
- API クライアント独自の利用条件が存在しない
- YouTube 利用規約と Google Privacy Policy への常設導線がない
- YouTube OAuth 実行前の説明と同意導線がない
- ローカル保存済み YouTube 認可情報の削除導線がない
- Google 側の同意取り消し導線がない
- サポート窓口の案内がアプリ内にない

## 実施した修正

- 同梱文書として Privacy Policy と Terms of Use を追加
- アプリ出力へ Docs 配下を同梱するよう設定
- YouTube タブに以下を追加
  - プライバシーポリシー、利用条件、YouTube 利用規約、Google Privacy Policy、Google 権限管理ページへの導線
  - 読み取り専用スコープと保存方法の説明
  - 利用条件確認チェックボックス
  - ローカル削除と revoke の操作
  - サポート窓口への導線
- 共通タブにポリシーとサポート導線を追加
- YouTube OAuth トークン revoke 処理を追加
- ローカル保存済み YouTube トークンと自動接続設定を削除する処理を追加

## 現在の判定

### 満たしている項目

- API クライアント内でプライバシーポリシーへ到達できる
- API クライアント内で独自利用条件へ到達できる
- YouTube 利用規約と Google Privacy Policy への導線がある
- YouTube 機能が YouTube API Services を利用していることを明示している
- YouTube 権限が読み取り専用であることを認可前に説明している
- 保存済み YouTube 認可情報を削除する導線がある
- Google 側の権限を取り消す導線がある
- プライバシー問い合わせ先への導線がある

### リポジトリ外の運用確認が必要な項目

- Google Cloud Console の OAuth 同意画面に、実際のプロダクト名、サポート連絡先、プライバシーポリシー URL が設定されていること
- OAuth 同意画面で要求スコープが実装どおり readonly のみであること
- リダイレクト URI が実装と一致していること
- 公開運用時に、同梱文書または同等の公開 URL を審査手続きで提示できること

## 根拠ファイル

- TwitchChatOverlay/Docs/PrivacyPolicy.html
- TwitchChatOverlay/Docs/TermsOfUse.html
- TwitchChatOverlay/Services/YouTubeOAuthService.cs
- TwitchChatOverlay/Services/SettingsService.cs
- TwitchChatOverlay/ViewModels/MainWindowViewModel.cs
- TwitchChatOverlay/ViewModels/CommonSettingsTabViewModel.cs
- TwitchChatOverlay/ViewModels/YouTubeSettingsTabViewModel.cs
- TwitchChatOverlay/Views/Tabs/CommonSettingsTabView.xaml
- TwitchChatOverlay/Views/Tabs/YouTubeSettingsTabView.xaml
- TwitchChatOverlay/TwitchChatOverlay.csproj
- README.md

## 残タスク

コード内で対応できるギャップは解消済みです。残るのは Google Cloud Console などの外部運用設定確認のみです。