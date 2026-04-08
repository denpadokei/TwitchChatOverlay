using Microsoft.Win32;
using Prism.Commands;
using Prism.Mvvm;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using TwitchChatOverlay.Models;
using TwitchChatOverlay.Services;
using WinForms = System.Windows.Forms;

namespace TwitchChatOverlay.ViewModels
{
    public class MainWindowViewModel : BindableBase
    {
        private const string YouTubeTermsUrl = "https://www.youtube.com/t/terms";
        private const string GooglePrivacyPolicyUrl = "https://policies.google.com/privacy";
        private const string GoogleSecurityPermissionsUrl = "https://security.google.com/settings/security/permissions";
        private const string GitHubDiscussionsUrl = "https://github.com/denpadokei/TwitchChatOverlay/discussions";

        private double _toastBackgroundOpacity = 0.8;

        public ObservableCollection<string> MonitorList { get; } = [];
        public ObservableCollection<AudioOutputDeviceOption> AudioOutputDevices { get; } = [];

        public ObservableCollection<string> RecentChannels { get; } = [];

        public bool HasRecentChannels => this.RecentChannels.Count > 0;

        private string _updateDownloadUrl;
        private string _updateChecksumUrl;
        private string _updateReleasePageUrl;

        public bool IsUpdateAvailable
        {
            get;
            set => this.SetProperty(ref field, value);
        }

        public bool IsUpdating
        {
            get;
            set => this.SetProperty(ref field, value);
        }

        public int UpdateProgressPercent
        {
            get;
            set => this.SetProperty(ref field, value);
        }

        public string LatestVersion
        {
            get;
            set => this.SetProperty(ref field, value);
        }

        private readonly TwitchApiService _apiService;
        private readonly TwitchEventSubService _eventSubService;
        private readonly ToastNotificationService _toastService;
        private readonly SettingsService _settingsService;
        private readonly UpdateService _updateService;
        private readonly NotificationSoundService _notificationSoundService;
        private readonly YouTubeOAuthService _youTubeOAuthService;
        private readonly YouTubeLiveChatService _youTubeLiveChatService;
        private readonly ObsWebSocketService _obsWebSocketService;
        private Timer _tokenRefreshTimer;
        private Timer _youtubeTokenRefreshTimer;
        private int _isHandlingYouTubeConnectionLost;
        private Timer _validateTimer;
        /// <summary>次回タイマー設定用: refresh 直後の expires_in（秒）。0 のときはデフォルト値を使用。</summary>
        private int _nextRefreshExpiresIn;
        /// <summary>前回 YouTube 接続時の waitForObsSignalBeforePolling オプション。サイレントリフレッシュ再接続に使用。</summary>
        private bool _lastYouTubeWaitForObsSignal;

        public string Title
        {
            get;
            set => this.SetProperty(ref field, value);
        } = "Twitch Chat Overlay";

        public string ChannelName
        {
            get;
            set => this.SetProperty(ref field, value);
        }

        public string OAuthToken
        {
            get;
            set => this.SetProperty(ref field, value);
        }

        /// <summary>Device Auth フロー中に表示するユーザーコード</summary>
        public string DeviceUserCode
        {
            get;
            set => this.SetProperty(ref field, value);
        }

        public string StatusMessage
        {
            get;
            set => this.SetProperty(ref field, value);
        } = "未接続";

        public bool IsAuthorizingOAuth
        {
            get;
            set => this.SetProperty(ref field, value);
        }

        /// <summary>保存済みトークンのログイン名と保存日を表示する文字列</summary>
        public string TokenInfo
        {
            get;
            set
            {
                _ = this.SetProperty(ref field, value);
                this.HasToken = !string.IsNullOrEmpty(value);
            }
        }

        /// <summary>有効なトークン情報があるか（UI表示制御用）</summary>
        public bool HasToken
        {
            get;
            set => this.SetProperty(ref field, value);
        }

        public bool ShowReward
        {
            get;
            set => this.SetProperty(ref field, value);
        } = true;

        public bool ShowRaid
        {
            get;
            set => this.SetProperty(ref field, value);
        } = true;

        public bool ShowFollow
        {
            get;
            set => this.SetProperty(ref field, value);
        } = true;

        public bool ShowSubscribe
        {
            get;
            set => this.SetProperty(ref field, value);
        } = true;

        public bool ShowGiftSubscribe
        {
            get;
            set => this.SetProperty(ref field, value);
        } = true;

        public bool ShowResub
        {
            get;
            set => this.SetProperty(ref field, value);
        } = true;

        public bool ShowHypeTrainBegin
        {
            get;
            set => this.SetProperty(ref field, value);
        } = true;

        public bool ShowHypeTrainEnd
        {
            get;
            set => this.SetProperty(ref field, value);
        } = true;

        public int ToastDurationSeconds
        {
            get;
            set => this.SetProperty(ref field, value);
        } = 5;

        public int ToastMaxCount
        {
            get;
            set => this.SetProperty(ref field, value);
        } = 5;

        public int ToastPositionIndex
        {
            get;
            set => this.SetProperty(ref field, value);
        } = 0;

        public int ToastMonitorIndex
        {
            get;
            set => this.SetProperty(ref field, value);
        } = 0;

        public double ToastFontSize
        {
            get;
            set => this.SetProperty(ref field, value);
        } = 12;

        public double ToastWidth
        {
            get;
            set => this.SetProperty(ref field, value);
        } = 380;

        public double ToastBackgroundOpacity
        {
            get => this._toastBackgroundOpacity;
            set
            {
                _ = this.SetProperty(ref this._toastBackgroundOpacity, value);
                this.RaisePropertyChanged(nameof(this.ToastBackgroundOpacityPercent));
            }
        }

        /// <summary>UI用: 透過率を 0〜100 の整数で表示・入力する。</summary>
        public int ToastBackgroundOpacityPercent
        {
            get => (int)Math.Round(this._toastBackgroundOpacity * 100);
            set
            {
                var clamped = Math.Clamp(value, 0, 100);
                if (this.SetProperty(ref this._toastBackgroundOpacity, clamped / 100.0))
                {
                    this.RaisePropertyChanged(nameof(this.ToastBackgroundOpacity));
                }
            }
        }

        public string ToastFontFamily
        {
            get;
            set => this.SetProperty(ref field, value);
        } = "";

        public int ToastBackgroundModeIndex
        {
            get;
            set
            {
                _ = this.SetProperty(ref field, value);
                this.RaisePropertyChanged(nameof(this.IsCustomBackgroundColor));
            }
        } = 0;

        public bool IsCustomBackgroundColor => this.ToastBackgroundModeIndex == 3;

        public string ToastCustomBackgroundColor
        {
            get;
            set => this.SetProperty(ref field, value);
        } = "#1A1A2E";

        public int ToastFontColorModeIndex
        {
            get;
            set
            {
                _ = this.SetProperty(ref field, value);
                this.RaisePropertyChanged(nameof(this.IsCustomFontColor));
            }
        } = 0;

        public bool IsCustomFontColor => this.ToastFontColorModeIndex == 1;

        public string ToastCustomFontColor
        {
            get;
            set => this.SetProperty(ref field, value);
        } = "#FFFFFF";

        public int NotificationSoundSourceModeIndex
        {
            get;
            set
            {
                _ = this.SetProperty(ref field, value);
                this.RaisePropertyChanged(nameof(this.IsCustomNotificationSoundFile));
            }
        } = 0;

        public bool IsCustomNotificationSoundFile => this.NotificationSoundSourceModeIndex == 1;

        public bool NotificationSoundEnabled
        {
            get;
            set => this.SetProperty(ref field, value);
        } = true;

        public string NotificationSoundFilePath
        {
            get;
            set => this.SetProperty(ref field, value);
        } = "";

        public int NotificationSoundVolumePercent
        {
            get;
            set => this.SetProperty(ref field, Math.Clamp(value, 0, 100));
        } = 100;

        public string NotificationSoundOutputDeviceId
        {
            get;
            set => this.SetProperty(ref field, value ?? "");
        } = "";

        public System.Collections.Generic.List<string> FontFamilyPresets { get; } =
        [
            "",
            "Meiryo UI",
            "Yu Gothic UI",
            "MS UI Gothic",
            "BIZ UDGothic",
            "HGPGothicE",
            "Segoe UI",
            "Arial",
            "Consolas",
            "Impact",
        ];

        public ICommand ConnectCommand { get; }
        public ICommand DisconnectCommand { get; }
        public ICommand AuthorizeOAuthCommand { get; }
        public ICommand SaveSettingsCommand { get; }
        public ICommand SelectRecentChannelCommand { get; }
        public ICommand UpdateCommand { get; }
        public ICommand OpenReleasePageCommand { get; }
        public ICommand BrowseNotificationSoundFileCommand { get; }
        public ICommand PreviewNotificationSoundCommand { get; }
        public ICommand PreviewCommonCommentCommand { get; }
        public ICommand PreviewTwitchCommentCommand { get; }
        public ICommand PreviewYouTubeCommentCommand { get; }
        public ICommand AuthorizeYouTubeOAuthCommand { get; }
        public ICommand ConnectYouTubeCommand { get; }
        public ICommand DisconnectYouTubeCommand { get; }
        public ICommand OpenPrivacyPolicyCommand { get; }
        public ICommand OpenTermsOfUseCommand { get; }
        public ICommand OpenYouTubeTermsCommand { get; }
        public ICommand OpenGooglePrivacyPolicyCommand { get; }
        public ICommand OpenGooglePermissionsCommand { get; }
        public ICommand OpenSupportCommand { get; }
        public ICommand ClearYouTubeAuthorizationCommand { get; }
        public ICommand RevokeYouTubeAuthorizationCommand { get; }

        public int SelectedTabIndex
        {
            get;
            set => this.SetProperty(ref field, value);
        }

        public bool ShowYouTubeChat
        {
            get;
            set => this.SetProperty(ref field, value);
        } = true;

        public bool ShowYouTubeSuperChat
        {
            get;
            set => this.SetProperty(ref field, value);
        } = true;

        public bool ShowYouTubeMembership
        {
            get;
            set => this.SetProperty(ref field, value);
        } = true;

        public string YouTubeTokenInfo
        {
            get;
            set => this.SetProperty(ref field, value);
        } = "未認可";

        public string YouTubeStatusMessage
        {
            get;
            set => this.SetProperty(ref field, value);
        } = "未接続";

        public bool YouTubeLegalConsentAccepted
        {
            get;
            set => this.SetProperty(ref field, value);
        }

        public bool ObsWebSocketEnabled
        {
            get;
            set => this.SetProperty(ref field, value);
        }

        public string ObsWebSocketHost
        {
            get;
            set => this.SetProperty(ref field, value);
        } = "127.0.0.1";

        public int ObsWebSocketPort
        {
            get;
            set => this.SetProperty(ref field, value);
        } = 4455;

        public string ObsWebSocketPassword
        {
            get;
            set => this.SetProperty(ref field, value);
        } = "";

        public MainWindowViewModel(
            SettingsService settingsService,
            TwitchApiService apiService,
            TwitchEventSubService eventSubService,
            ToastNotificationService toastService,
            UpdateService updateService,
            NotificationSoundService notificationSoundService,
            YouTubeOAuthService youTubeOAuthService,
            YouTubeLiveChatService youTubeLiveChatService,
            ObsWebSocketService obsWebSocketService)
        {
            this._settingsService = settingsService;
            this._apiService = apiService;
            this._eventSubService = eventSubService;
            this._toastService = toastService;
            this._updateService = updateService;
            this._notificationSoundService = notificationSoundService;
            this._youTubeOAuthService = youTubeOAuthService;
            this._youTubeLiveChatService = youTubeLiveChatService;
            this._obsWebSocketService = obsWebSocketService;

            this.ConnectCommand = new DelegateCommand(this.Connect, this.CanConnect);
            this.DisconnectCommand = new DelegateCommand(this.Disconnect, this.CanDisconnect);
            this.AuthorizeOAuthCommand = new DelegateCommand(this.AuthorizeOAuth);
            this.SaveSettingsCommand = new DelegateCommand(this.SaveSettings);
            this.SelectRecentChannelCommand = new DelegateCommand<string>(ch => this.ChannelName = ch);
            this.UpdateCommand = new DelegateCommand(this.ExecuteUpdate, () => this.IsUpdateAvailable && !this.IsUpdating)
                .ObservesProperty(() => this.IsUpdateAvailable)
                .ObservesProperty(() => this.IsUpdating);
            this.OpenReleasePageCommand = new DelegateCommand(
                () => this._updateService.OpenReleasePage(this._updateReleasePageUrl),
                () => !string.IsNullOrEmpty(this._updateReleasePageUrl))
                .ObservesProperty(() => this.IsUpdateAvailable);
            this.BrowseNotificationSoundFileCommand = new DelegateCommand(this.BrowseNotificationSoundFile);
            this.PreviewNotificationSoundCommand = new DelegateCommand(this.PreviewNotificationSound);
            this.PreviewCommonCommentCommand = new DelegateCommand(this.PreviewCommonComment);
            this.PreviewTwitchCommentCommand = new DelegateCommand(this.PreviewTwitchComment);
            this.PreviewYouTubeCommentCommand = new DelegateCommand(this.PreviewYouTubeComment);
            this.AuthorizeYouTubeOAuthCommand = new DelegateCommand(this.AuthorizeYouTubeOAuth, this.CanAuthorizeYouTubeOAuth)
                .ObservesProperty(() => this.YouTubeLegalConsentAccepted);
            this.ConnectYouTubeCommand = new DelegateCommand(this.ConnectYouTube, this.CanConnectYouTube)
                .ObservesProperty(() => this.YouTubeLegalConsentAccepted);
            this.DisconnectYouTubeCommand = new DelegateCommand(this.DisconnectYouTube);
            this.OpenPrivacyPolicyCommand = new DelegateCommand(this.OpenPrivacyPolicy);
            this.OpenTermsOfUseCommand = new DelegateCommand(this.OpenTermsOfUse);
            this.OpenYouTubeTermsCommand = new DelegateCommand(this.OpenYouTubeTerms);
            this.OpenGooglePrivacyPolicyCommand = new DelegateCommand(this.OpenGooglePrivacyPolicy);
            this.OpenGooglePermissionsCommand = new DelegateCommand(this.OpenGooglePermissions);
            this.OpenSupportCommand = new DelegateCommand(this.OpenSupport);
            this.ClearYouTubeAuthorizationCommand = new DelegateCommand(this.ClearYouTubeAuthorization);
            this.RevokeYouTubeAuthorizationCommand = new DelegateCommand(this.RevokeYouTubeAuthorization);

            this._toastService.Initialize(this._eventSubService, this._youTubeLiveChatService);

            // 予期しない切断（トークン期限切れ等）時に自動再接続
            this._eventSubService.ConnectionLost += this.OnConnectionLost;
            this._youTubeLiveChatService.ConnectionLost += this.OnYouTubeConnectionLost;
            this._youTubeLiveChatService.BroadcastDetected += this.OnYouTubeBroadcastDetected;
            this._youTubeLiveChatService.WaitingForBroadcastStarted += this.OnYouTubeWaitingForBroadcastStarted;
            this._youTubeLiveChatService.BroadcastEnded += this.OnYouTubeBroadcastEnded;
            this._obsWebSocketService.StreamingStateChanged += this.OnObsStreamingStateChanged;

            // モニター一覧を構築
            var screens = WinForms.Screen.AllScreens;
            for (var i = 0; i < screens.Length; i++)
            {
                var s = screens[i];
                var label = $"モニター {i + 1}{(s.Primary ? " (プライマリ)" : "")} {s.Bounds.Width}x{s.Bounds.Height}";
                this.MonitorList.Add(label);
            }

            this.ReloadAudioOutputDevices();

            this.LoadSettings();
            _ = this.ValidateSavedTokenAsync();
            _ = this.AutoConnectYouTubeAsync();
            _ = this.CheckForUpdateAsync();
        }

        private async Task AutoConnectYouTubeAsync()
        {
            var settings = this._settingsService.LoadSettings();

            if (!settings.YouTubeLegalConsentAccepted)
            {
                if (!string.IsNullOrWhiteSpace(settings.YouTubeOAuthToken))
                {
                    this.YouTubeStatusMessage = "YouTube の利用条件への同意が未完了のため、自動接続を停止しています";
                }

                return;
            }

            if (!settings.YouTubeAutoConnectEnabled)
            {
                this.YouTubeStatusMessage = "未接続";
                return;
            }

            if (string.IsNullOrWhiteSpace(settings.YouTubeOAuthToken))
            {
                if (!string.IsNullOrEmpty(settings.YouTubeTokenInfo))
                {
                    this.YouTubeTokenInfo = settings.YouTubeTokenInfo;
                }

                this.YouTubeStatusMessage = "未接続";
                return;
            }

            try
            {
                this.YouTubeStatusMessage = "YouTube自動接続中...";
                var (UseObsForDetection, ObsConnected) = await this.TryPrepareObsAsync(settings);
                var waitForObsSignal = UseObsForDetection;
                this._lastYouTubeWaitForObsSignal = waitForObsSignal;
                await this._youTubeLiveChatService.ConnectAsync(
                    settings.YouTubeOAuthToken,
                    checkImmediately: !waitForObsSignal,
                    waitForObsSignalBeforePolling: waitForObsSignal);
                this.YouTubeStatusMessage = this._youTubeLiveChatService.IsWaitingForBroadcast
                    ? BuildYouTubeWaitingMessage(waitForObsSignal)
                    : BuildYouTubeConnectedMessage("✅ YouTube自動接続完了");
                this.UpdateYouTubeTokenRefreshTimerState();

                settings.YouTubeAutoConnectEnabled = true;
                this._settingsService.SaveSettings(settings);
            }
            catch
            {
                if (string.IsNullOrWhiteSpace(settings.YouTubeRefreshToken))
                {
                    this.YouTubeStatusMessage = "YouTube自動接続に失敗しました（再認可してください）";
                    return;
                }

                try
                {
                    var refreshed = await this._youTubeOAuthService.RefreshTokenAsync(BuildSecrets.YouTubeClientId, settings.YouTubeRefreshToken);
                    settings.YouTubeOAuthToken = refreshed.AccessToken;
                    settings.YouTubeRefreshToken = refreshed.RefreshToken;
                    settings.YouTubeTokenInfo = $"✅ 認可済み | 更新日: {DateTime.Now:yyyy/MM/dd HH:mm}";
                    this._settingsService.SaveSettings(settings);

                    this.YouTubeTokenInfo = settings.YouTubeTokenInfo;
                    var (UseObsForDetection, ObsConnected) = await this.TryPrepareObsAsync(settings);
                    var waitForObsSignal = UseObsForDetection;
                    this._lastYouTubeWaitForObsSignal = waitForObsSignal;
                    await this._youTubeLiveChatService.ConnectAsync(
                        settings.YouTubeOAuthToken,
                        checkImmediately: !waitForObsSignal,
                        waitForObsSignalBeforePolling: waitForObsSignal);
                    this.YouTubeStatusMessage = this._youTubeLiveChatService.IsWaitingForBroadcast
                        ? BuildYouTubeWaitingMessage(waitForObsSignal)
                        : BuildYouTubeConnectedMessage("✅ YouTube自動接続完了");
                    this.UpdateYouTubeTokenRefreshTimerState();

                    settings.YouTubeAutoConnectEnabled = true;
                    this._settingsService.SaveSettings(settings);
                }
                catch (Exception ex)
                {
                    LogService.Warning("YouTube自動接続に失敗しました", ex);
                    this.YouTubeStatusMessage = "YouTube自動接続に失敗しました（手動接続してください）";
                }
            }
        }

        private async Task AutoConnectAsync()
        {
            if (string.IsNullOrWhiteSpace(this.ChannelName) ||
                string.IsNullOrWhiteSpace(this.OAuthToken))
            {
                return;
            }

            try
            {
                this.StatusMessage = "自動接続中...";
                var settings = this._settingsService.LoadSettings();
                var broadcasterUserId = settings.BroadcasterUserId;
                var userId = settings.UserId;

                if (string.IsNullOrEmpty(broadcasterUserId) || string.IsNullOrEmpty(userId))
                {
                    return;
                }

                await this._eventSubService.ConnectAsync(this.OAuthToken, BuildSecrets.ClientId, broadcasterUserId, userId);
                LogService.Info($"EventSub自動接続完了: {this.ChannelName}");
                this.StatusMessage = $"✅ 接続完了 ({this.ChannelName})";
                this.StartTokenRefreshTimer();
                this.StartValidateTimer();
                ((DelegateCommand)this.ConnectCommand).RaiseCanExecuteChanged();
                ((DelegateCommand)this.DisconnectCommand).RaiseCanExecuteChanged();
            }
            catch (Exception ex)
            {
                LogService.Error("自動接続エラー", ex);
                this.StatusMessage = $"自動接続エラー: {ex.Message}";
            }
        }

        /// <summary>接続完了後にトークンリフレッシュタイマーを開始する。expires_in が既知の場合はそれに基づいて間隔を決定する。</summary>
        private void StartTokenRefreshTimer()
        {
            this._tokenRefreshTimer?.Dispose();
#if DEBUG
            // デバッグ時は5分ごとにリフレッシュ（Twitch APIレート制限内で動作確認用）
            var interval = TimeSpan.FromMinutes(5);
#else
            TimeSpan interval;
            int expiresIn = _nextRefreshExpiresIn;
            if (expiresIn > 0)
            {
                // 有効期限の10分前にリフレッシュ。最低60秒、最大3時間に収める
                int seconds = Math.Clamp(expiresIn - 600, 60, (int)TimeSpan.FromHours(3).TotalSeconds);
                interval = TimeSpan.FromSeconds(seconds);
                var nextAt = DateTime.Now.Add(interval);
                LogService.Debug($"[TokenRefreshTimer] expires_in={expiresIn}s に基づき {interval.TotalMinutes:F0} 分後にリフレッシュ (次回予定: {nextAt:yyyy/MM/dd HH:mm:ss})");
            }
            else
            {
                // expires_in 不明時は3時間をデフォルトとする
                interval = TimeSpan.FromHours(3);
                var nextAt = DateTime.Now.Add(interval);
                LogService.Debug($"[TokenRefreshTimer] expires_in 不明のため {interval.TotalHours:F0} 時間後にリフレッシュ (次回予定: {nextAt:yyyy/MM/dd HH:mm:ss})");
            }
            _nextRefreshExpiresIn = 0;
#endif
            this._tokenRefreshTimer = new Timer(
                _ => _ = this.RefreshTokenSilentlyAsync(),
                null,
                interval,
                interval);
        }

        private void StopTokenRefreshTimer()
        {
            this._tokenRefreshTimer?.Dispose();
            this._tokenRefreshTimer = null;
        }

        /// <summary>YouTube接続完了後に50分ごとのトークン予防的リフレッシュタイマーを開始する。</summary>
        private void StartYouTubeTokenRefreshTimer()
        {
            this._youtubeTokenRefreshTimer?.Dispose();
#if DEBUG
            // デバッグ時は3分ごとにリフレッシュ（動作確認用）
            var interval = TimeSpan.FromMinutes(3);
#else
            // 50分ごとに実行（YouTube アクセストークンは約3600秒（1時間）で期限切れ）
            var interval = TimeSpan.FromMinutes(50);
#endif
            this._youtubeTokenRefreshTimer = new Timer(
                _ => _ = this.RefreshYouTubeTokenSilentlyAsync(),
                null,
                interval,
                interval);
        }

        private void StopYouTubeTokenRefreshTimer()
        {
            this._youtubeTokenRefreshTimer?.Dispose();
            this._youtubeTokenRefreshTimer = null;
        }

        private void UpdateYouTubeTokenRefreshTimerState()
        {
            if (this._youTubeLiveChatService.IsWaitingForBroadcast)
            {
                this.StopYouTubeTokenRefreshTimer();
                return;
            }

            if (this._youTubeLiveChatService.IsConnected)
            {
                this.StartYouTubeTokenRefreshTimer();
            }
        }

        /// <summary>バックグラウンドでYouTubeトークンをリフレッシュし、接続中なら再接続する。</summary>
        private async Task RefreshYouTubeTokenSilentlyAsync()
        {
            var settings = this._settingsService.LoadSettings();
            if (string.IsNullOrEmpty(settings.YouTubeRefreshToken))
            {
                LogService.Debug("[YouTube SilentRefresh] リフレッシュトークンがないためスキップ");
                return;
            }

            try
            {
                LogService.Debug("[YouTube SilentRefresh] バックグラウンド更新を開始");
                var refreshed = await this._youTubeOAuthService.RefreshTokenAsync(BuildSecrets.YouTubeClientId, settings.YouTubeRefreshToken);
                settings.YouTubeOAuthToken = refreshed.AccessToken;
                settings.YouTubeRefreshToken = refreshed.RefreshToken;
                settings.YouTubeTokenInfo = $"✅ 認可済み | 更新日: {DateTime.Now:yyyy/MM/dd HH:mm}";
                this._settingsService.SaveSettings(settings);

                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher != null)
                {
                    _ = await dispatcher.InvokeAsync(() => this.YouTubeTokenInfo = settings.YouTubeTokenInfo);
                }
                else
                {
                    this.YouTubeTokenInfo = settings.YouTubeTokenInfo;
                }

                LogService.Debug($"[YouTube SilentRefresh] トークン更新成功");

                // 接続中の場合は前回と同じ接続オプションで再接続
                if (this._youTubeLiveChatService.IsConnected || this._youTubeLiveChatService.IsWaitingForBroadcast)
                {
                    var waitForObsSignal = this._lastYouTubeWaitForObsSignal;
                    LogService.Debug("[YouTube SilentRefresh] 接続中のため新トークンで再接続");
                    await this._youTubeLiveChatService.ConnectAsync(
                        refreshed.AccessToken,
                        checkImmediately: !waitForObsSignal,
                        waitForObsSignalBeforePolling: waitForObsSignal);
                    this.UpdateYouTubeTokenRefreshTimerState();
                }
            }
            catch (Exception ex)
            {
                LogService.Warning("[YouTube SilentRefresh] バックグラウンド更新に失敗", ex);
            }
        }

        /// <summary>毎時トークン検証タイマーを開始する。</summary>
        private void StartValidateTimer()
        {
            this._validateTimer?.Dispose();
#if DEBUG
            var interval = TimeSpan.FromMinutes(2);
#else
            var interval = TimeSpan.FromHours(1);
#endif
            this._validateTimer = new Timer(
                _ => _ = this.ValidateTokenPeriodicallyAsync(),
                null,
                interval,
                interval);
            LogService.Debug($"[ValidateTimer] {interval.TotalMinutes:F0} 分ごとの定期 validate を開始");
        }

        private void StopValidateTimer()
        {
            this._validateTimer?.Dispose();
            this._validateTimer = null;
        }

        /// <summary>定期 validate。トークンが無効になっていれば切断して再認可を促す。</summary>
        private async Task ValidateTokenPeriodicallyAsync()
        {
            // 開始時点のトークンをキャプチャして、その値に対してのみ validate を行う
            var tokenAtStart = this.OAuthToken;
            if (string.IsNullOrEmpty(tokenAtStart))
            {
                return;
            }

            LogService.Debug("[ValidateTimer] 定期トークン検証を開始");
            try
            {
                var (isValid, _, _, expiresIn) = await this._apiService.ValidateTokenAsync(tokenAtStart);
                if (isValid)
                {
                    LogService.Debug($"[ValidateTimer] トークン有効 expires_in={expiresIn}s (期限: {DateTime.Now.AddSeconds(expiresIn):yyyy/MM/dd HH:mm:ss})");
                    return;
                }

                // validate 中にトークンが更新されていないか確認し、stale 結果であれば無視する
                if (!string.Equals(this.OAuthToken, tokenAtStart, StringComparison.Ordinal))
                {
                    LogService.Debug("[ValidateTimer] validate 結果は古いトークンに対するもののため、セッション終了をスキップします");
                    return;
                }

                // トークンが無効になっていた場合はセッションを終了する
                LogService.Warning("[ValidateTimer] トークンが無効になっています。セッションを終了します");
                this.StopTokenRefreshTimer();
                this.StopValidateTimer();
                this._eventSubService.Disconnect();

                this.OAuthToken = "";
                this.TokenInfo = "⚠️ トークンが無効になりました。再認可してください";
                this.StatusMessage = "⚠️ トークンが無効になりました。再認可してください";
                ((DelegateCommand)this.ConnectCommand).RaiseCanExecuteChanged();
                ((DelegateCommand)this.DisconnectCommand).RaiseCanExecuteChanged();
            }
            catch (Exception ex)
            {
                // ネットワークエラーはスキップ（次の周期で再試行）
                LogService.Warning("[ValidateTimer] 定期トークン検証中にエラーが発生しました（次の周期で再試行）", ex);
            }
        }

        /// <summary>バックグラウンドでトークンをリフレッシュし、接続中なら再接続する。</summary>
        private async Task RefreshTokenSilentlyAsync()
        {
            var settings = this._settingsService.LoadSettings();
            if (string.IsNullOrEmpty(settings.RefreshToken))
            {
                LogService.Debug("[SilentRefresh] リフレッシュトークンがないためスキップ");
                return;
            }

            LogService.Debug("[SilentRefresh] タイマーによるトークン更新を開始");
            try
            {
                var oauthServer = new TwitchOAuthServer(BuildSecrets.ClientId);
                var newToken = await oauthServer.RefreshTokenAsync(settings.RefreshToken);

                this.OAuthToken = newToken.AccessToken;
                settings.OAuthToken = newToken.AccessToken;
                settings.RefreshToken = newToken.RefreshToken;
                settings.OAuthTokenSavedAt = DateTime.UtcNow;
                this._settingsService.SaveSettings(settings);

                var savedAt = DateTime.Now.ToString("yyyy/MM/dd HH:mm");
                this.TokenInfo = $"✅ {settings.OAuthTokenLogin ?? "(不明)"}  |  更新日: {savedAt}";
                var expiryInfo = newToken.ExpiresIn > 0
                    ? $"expires_in={newToken.ExpiresIn}s (期限: {DateTime.Now.AddSeconds(newToken.ExpiresIn):yyyy/MM/dd HH:mm:ss})"
                    : "expires_in 不明";
                LogService.Debug($"[SilentRefresh] 更新成功 user={settings.OAuthTokenLogin ?? "(不明)"} {expiryInfo}");

                // 接続中であればトークンを更新して再接続
                if (this._eventSubService.IsConnected)
                {
                    LogService.Debug("[SilentRefresh] 接続中のため再接続を実行");
                    this._eventSubService.Disconnect();
                    await Task.Delay(500);
                    this._nextRefreshExpiresIn = newToken.ExpiresIn;
                    await this.AutoConnectAsync();
                    LogService.Debug("[SilentRefresh] 再接続完了");
                }
            }
            catch (TwitchTokenRefreshException ex) when (ex.IsInvalidRefreshToken)
            {
                this.InvalidateTwitchRefreshToken(settings, "SilentRefresh");
                LogService.Warning("トークンのサイレントリフレッシュに失敗しました（無効なリフレッシュトークン）", ex);
            }
            catch (Exception ex)
            {
                // サイレントなのでUI表示はしない（切断イベントで再試行される）
                LogService.Warning("トークンのサイレントリフレッシュに失敗しました", ex);
            }
        }

        /// <summary>予期しない切断時に呼ばれる。トークンをリフレッシュして再接続を試みる。</summary>
        private async void OnConnectionLost(object sender, EventArgs e)
        {
            this.StopTokenRefreshTimer();
            this.StopValidateTimer();
            ((DelegateCommand)this.ConnectCommand).RaiseCanExecuteChanged();
            ((DelegateCommand)this.DisconnectCommand).RaiseCanExecuteChanged();

            var settings = this._settingsService.LoadSettings();
            if (string.IsNullOrEmpty(settings.RefreshToken))
            {
                this.StatusMessage = "⚠️ 接続が切断されました。再接続するにはOAuth再認可が必要です";
                return;
            }

            try
            {
                this.StatusMessage = "🔄 接続が切断されました。トークンを更新して再接続中...";
                LogService.Warning("予期しない切断が発生しました。トークン更新と再接続を試みます");
                await Task.Delay(2000); // 少し待ってから再接続

                var oauthServer = new TwitchOAuthServer(BuildSecrets.ClientId);
                var newToken = await oauthServer.RefreshTokenAsync(settings.RefreshToken);

                this.OAuthToken = newToken.AccessToken;
                settings.OAuthToken = newToken.AccessToken;
                settings.RefreshToken = newToken.RefreshToken;
                settings.OAuthTokenSavedAt = DateTime.UtcNow;
                this._settingsService.SaveSettings(settings);

                var savedAt = DateTime.Now.ToString("yyyy/MM/dd HH:mm");
                this.TokenInfo = $"✅ {settings.OAuthTokenLogin ?? "(不明)"}  |  更新日: {savedAt}";
                var expiryInfo = newToken.ExpiresIn > 0
                    ? $"expires_in={newToken.ExpiresIn}s (期限: {DateTime.Now.AddSeconds(newToken.ExpiresIn):yyyy/MM/dd HH:mm:ss})"
                    : "expires_in 不明";
                LogService.Debug($"[OnConnectionLost] トークン更新成功。再接続を開始 user={settings.OAuthTokenLogin ?? "(不明)"} {expiryInfo}");

                this._nextRefreshExpiresIn = newToken.ExpiresIn;
                await this.AutoConnectAsync();
            }
            catch (TwitchTokenRefreshException ex) when (ex.IsInvalidRefreshToken)
            {
                this.InvalidateTwitchRefreshToken(settings, "OnConnectionLost");
                LogService.Error("再接続エラー。無効なリフレッシュトークンのため再認可が必要です", ex);
                this.StatusMessage = "⚠️ リフレッシュトークンが無効です。再認可してください";
                this.TokenInfo = "⚠️ トークンの更新に失敗しました。再認可してください";
                this.OAuthToken = "";
            }
            catch (Exception ex)
            {
                LogService.Error("再接続エラー。再認可が必要です", ex);
                this.StatusMessage = $"⚠️ 再接続失敗: {ex.Message}　再認可してください";
                this.TokenInfo = "⚠️ トークンの更新に失敗しました。再認可してください";
                this.OAuthToken = "";
            }
        }

        private async Task ValidateSavedTokenAsync()
        {
            var settings = this._settingsService.LoadSettings();

            // まず保存済みアクセストークンを検証し、有効ならそのまま接続する
            if (!string.IsNullOrEmpty(this.OAuthToken))
            {
                try
                {
                    // 残り600秒（10分）未満なら有効でも先行リフレッシュする
                    const int RefreshThresholdSeconds = 600;

                    var (isValid, login, userId, expiresIn) = await this._apiService.ValidateTokenAsync(this.OAuthToken);
                    if (isValid && expiresIn >= RefreshThresholdSeconds)
                    {
                        var savedAt = settings.OAuthTokenSavedAt.HasValue
                            ? settings.OAuthTokenSavedAt.Value.ToLocalTime().ToString("yyyy/MM/dd HH:mm")
                            : "不明";
                        this.TokenInfo = $"✅ {login ?? settings.OAuthTokenLogin ?? "(不明)"}  |  取得日: {savedAt}";
                        LogService.Debug($"[Startup] トークン有効 expires_in={expiresIn}s (期限: {DateTime.Now.AddSeconds(expiresIn):yyyy/MM/dd HH:mm:ss})");

                        // UserIdが未保存なら補完
                        if (!string.IsNullOrEmpty(userId) && string.IsNullOrEmpty(settings.UserId))
                        {
                            settings.UserId = userId;
                            settings.BroadcasterUserId = userId;
                            this._settingsService.SaveSettings(settings);
                        }

                        this._nextRefreshExpiresIn = expiresIn;
                        await this.AutoConnectAsync();
                        return;
                    }

                    if (isValid)
                    {
                        LogService.Debug($"[Startup] アクセストークンの残り有効期限が {expiresIn} 秒のため先行リフレッシュします");
                    }
                    else
                    {
                        LogService.Debug("[Startup] 保存済みアクセストークンは無効でした。リフレッシュを試行します");
                    }

                    this.OAuthToken = "";
                }
                catch (Exception ex)
                {
                    // ネットワークエラー時はリフレッシュトークンがあれば続けて試行する
                    LogService.Warning("トークン検証中にエラーが発生しました。リフレッシュにフォールバックします", ex);
                }
            }

            // アクセストークンが使えない場合のみ、リフレッシュトークンで更新する
            if (!string.IsNullOrEmpty(settings.RefreshToken))
            {
                LogService.Debug("[Startup] リフレッシュトークンで更新を試行します");
                try
                {
                    this.TokenInfo = "🔄 トークンを更新中...";
                    var oauthServer = new TwitchOAuthServer(BuildSecrets.ClientId);
                    var newToken = await oauthServer.RefreshTokenAsync(settings.RefreshToken);

                    this.OAuthToken = newToken.AccessToken;
                    settings.OAuthToken = newToken.AccessToken;
                    settings.RefreshToken = newToken.RefreshToken;
                    settings.OAuthTokenSavedAt = DateTime.UtcNow;
                    this._settingsService.SaveSettings(settings);

                    var savedAt = DateTime.Now.ToString("yyyy/MM/dd HH:mm");
                    this.TokenInfo = $"✅ {settings.OAuthTokenLogin ?? "(不明)"}  |  更新日: {savedAt}";
                    var expiryInfo = newToken.ExpiresIn > 0
                        ? $"expires_in={newToken.ExpiresIn}s (期限: {DateTime.Now.AddSeconds(newToken.ExpiresIn):yyyy/MM/dd HH:mm:ss})"
                        : "expires_in 不明";
                    LogService.Debug($"[Startup] 起動時トークン更新成功 user={settings.OAuthTokenLogin ?? "(不明)"} {expiryInfo}");
                    this._nextRefreshExpiresIn = newToken.ExpiresIn;
                    await this.AutoConnectAsync();
                    return;
                }
                catch (TwitchTokenRefreshException ex) when (ex.IsInvalidRefreshToken)
                {
                    this.InvalidateTwitchRefreshToken(settings, "Startup");
                    this.TokenInfo = "⚠️ トークンの有効期限が切れました。再認可してください";
                    this.OAuthToken = "";
                    LogService.Warning("起動時のトークン更新に失敗しました。リフレッシュトークンが無効です", ex);
                    return;
                }
                catch (Exception ex)
                {
                    LogService.Warning("起動時のトークン更新に失敗しました", ex);
                }
            }

            if (string.IsNullOrEmpty(this.OAuthToken))
            {
                this.TokenInfo = null;
            }
        }

        private async Task CheckForUpdateAsync()
        {
            try
            {
                var result = await this._updateService.CheckForUpdateAsync();
                if (result.IsUpdateAvailable)
                {
                    this.LatestVersion = result.LatestVersion;
                    this._updateDownloadUrl = result.DownloadUrl;
                    this._updateChecksumUrl = result.ChecksumUrl;
                    this._updateReleasePageUrl = result.ReleasePageUrl;
                    this.IsUpdateAvailable = true;
                }
            }
            catch (Exception ex)
            {
                // ネットワークエラーは無視（更新チェックは必須ではない）
                LogService.Warning("アップデートチェック中にエラーが発生しました（無視）", ex);
            }
        }

        private async void ExecuteUpdate()
        {
            if (string.IsNullOrEmpty(this._updateDownloadUrl))
            {
                if (!string.IsNullOrEmpty(this._updateReleasePageUrl))
                {
                    this._updateService.OpenReleasePage(this._updateReleasePageUrl);
                }

                return;
            }

            this.IsUpdating = true;
            this.UpdateProgressPercent = 0;

            try
            {
                var progress = new Progress<int>(p => this.UpdateProgressPercent = p);
                var filePath = await this._updateService.DownloadUpdateAsync(this._updateDownloadUrl, this._updateChecksumUrl, progress);
                this._updateService.LaunchInstaller(filePath);
                // LaunchInstaller が Shutdown を呼ぶため、ここには通常到達しない。
                // .zip の自己更新バッチ起動後も Shutdown するため同様。
            }
            catch (Exception ex)
            {
                LogService.Error("アップデートの実行に失敗しました", ex);
                this.StatusMessage = $"更新の失敗: {ex.Message}";
            }
            finally
            {
                this.IsUpdating = false;
                this.UpdateProgressPercent = 0;
            }
        }

        private async void AuthorizeOAuth()
        {
            try
            {
                this.IsAuthorizingOAuth = true;
                this.DeviceUserCode = "";
                this.StatusMessage = "デバイス認可を開始中...";

                var oauthServer = new TwitchOAuthServer(BuildSecrets.ClientId);
                var tokenResponse = await oauthServer.AuthorizeAsync((userCode, verUri) =>
                {
                    this.DeviceUserCode = userCode;
                    this.StatusMessage = $"ブラウザで {verUri} を開き、コード [{userCode}] を入力してください";
                });

                this.OAuthToken = tokenResponse.AccessToken;
                this.DeviceUserCode = "";

                this.StatusMessage = "ユーザー情報を取得中...";
                var (userId, login) = await this._apiService.GetCurrentUserAsync(this.OAuthToken, BuildSecrets.ClientId);

                var settings = this._settingsService.LoadSettings();
                settings.UserId = userId;
                settings.BroadcasterUserId = userId;
                settings.OAuthToken = this.OAuthToken;
                settings.RefreshToken = tokenResponse.RefreshToken;
                settings.OAuthTokenSavedAt = DateTime.UtcNow;
                settings.OAuthTokenLogin = login;
                this._settingsService.SaveSettings(settings);

                var savedAt = DateTime.Now.ToString("yyyy/MM/dd HH:mm");
                this.TokenInfo = $"✅ {login}  |  取得日: {savedAt}";
                LogService.Info($"OAuth認可完了: login={login}");
                this.StatusMessage = $"✅ OAuth認可完了！ログイン: {login}";
            }
            catch (Exception ex)
            {
                LogService.Error("OAuth認可エラー", ex);
                this.StatusMessage = $"OAuth認可エラー: {ex.Message}";
                this.DeviceUserCode = "";
            }
            finally
            {
                this.IsAuthorizingOAuth = false;
            }
        }

        private async void Connect()
        {
            if (string.IsNullOrWhiteSpace(this.ChannelName) ||
                string.IsNullOrWhiteSpace(this.OAuthToken))
            {
                this.StatusMessage = "チャンネル名とOAuthトークンを入力してください";
                return;
            }

            try
            {
                this.StatusMessage = "EventSubに接続中...";
                var settings = this._settingsService.LoadSettings();

                // 接続時にチャンネル名を自動保存
                settings.ChannelName = this.ChannelName;

                // 接続履歴を更新 (最大10件、重複を排除して先頭に追加)
                _ = settings.RecentChannels.Remove(this.ChannelName);
                settings.RecentChannels.Insert(0, this.ChannelName);
                if (settings.RecentChannels.Count > 10)
                {
                    settings.RecentChannels.RemoveAt(settings.RecentChannels.Count - 1);
                }

                this._settingsService.SaveSettings(settings);

                // UI の履歴リストを更新
                this.RecentChannels.Clear();
                foreach (var ch in settings.RecentChannels)
                {
                    this.RecentChannels.Add(ch);
                }

                this.RaisePropertyChanged(nameof(this.HasRecentChannels));

                var broadcasterUserId = settings.BroadcasterUserId;
                var userId = settings.UserId;

                if (string.IsNullOrEmpty(broadcasterUserId) || string.IsNullOrEmpty(userId))
                {
                    this.StatusMessage = "まずOAuth認可を行ってください";
                    return;
                }

                await this._eventSubService.ConnectAsync(this.OAuthToken, BuildSecrets.ClientId, broadcasterUserId, userId);
                LogService.Info($"EventSub接続完了: {this.ChannelName}");
                this.StatusMessage = $"✅ 接続完了 ({this.ChannelName})";
                this.StartTokenRefreshTimer();
                this.StartValidateTimer();
                ((DelegateCommand)this.ConnectCommand).RaiseCanExecuteChanged();
                ((DelegateCommand)this.DisconnectCommand).RaiseCanExecuteChanged();
            }
            catch (Exception ex)
            {
                LogService.Error("EventSub接続エラー", ex);
                this.StatusMessage = $"接続エラー: {ex.Message}";
            }
        }

        private void Disconnect()
        {
            LogService.Info("EventSub切断要求");
            this.StopTokenRefreshTimer();
            this.StopValidateTimer();
            this._eventSubService.Disconnect();
            this.StatusMessage = "切断しました";
            ((DelegateCommand)this.ConnectCommand).RaiseCanExecuteChanged();
            ((DelegateCommand)this.DisconnectCommand).RaiseCanExecuteChanged();
        }

        private bool CanConnect()
        {
            return !this._eventSubService.IsConnected;
        }

        private bool CanDisconnect()
        {
            return this._eventSubService.IsConnected;
        }

        private void SaveSettings()
        {
            try
            {
                var settings = this._settingsService.LoadSettings();
                settings.SelectedTabIndex = this.SelectedTabIndex;
                settings.ChannelName = this.ChannelName;
                settings.OAuthToken = this.OAuthToken;
                settings.YouTubeTokenInfo = this.YouTubeTokenInfo;
                settings.ShowReward = this.ShowReward;
                settings.ShowRaid = this.ShowRaid;
                settings.ShowFollow = this.ShowFollow;
                settings.ShowSubscribe = this.ShowSubscribe;
                settings.ShowGiftSubscribe = this.ShowGiftSubscribe;
                settings.ShowResub = this.ShowResub;
                settings.ShowHypeTrainBegin = this.ShowHypeTrainBegin;
                settings.ShowHypeTrainEnd = this.ShowHypeTrainEnd;
                settings.ShowYouTubeChat = this.ShowYouTubeChat;
                settings.ShowYouTubeSuperChat = this.ShowYouTubeSuperChat;
                settings.ShowYouTubeMembership = this.ShowYouTubeMembership;
                settings.YouTubeLegalConsentAccepted = this.YouTubeLegalConsentAccepted;
                settings.ObsWebSocketEnabled = this.ObsWebSocketEnabled;
                settings.ObsWebSocketHost = this.ObsWebSocketHost;
                settings.ObsWebSocketPort = this.ObsWebSocketPort;
                settings.ObsWebSocketPassword = this.ObsWebSocketPassword;
                settings.ToastDurationSeconds = this.ToastDurationSeconds;
                settings.ToastMaxCount = this.ToastMaxCount;
                settings.ToastPosition = (Services.ToastPosition)this.ToastPositionIndex;
                settings.ToastMonitorIndex = this.ToastMonitorIndex;
                settings.ToastFontSize = this.ToastFontSize;
                settings.ToastWidth = this.ToastWidth;
                settings.ToastBackgroundOpacity = this.ToastBackgroundOpacity;
                settings.ToastFontFamily = this.ToastFontFamily;
                settings.ToastBackgroundMode = (Services.ToastBackgroundMode)this.ToastBackgroundModeIndex;
                settings.ToastCustomBackgroundColor = this.ToastCustomBackgroundColor;
                settings.ToastFontColorMode = (Services.ToastFontColorMode)this.ToastFontColorModeIndex;
                settings.ToastCustomFontColor = this.ToastCustomFontColor;
                settings.NotificationSoundSourceMode = (Services.NotificationSoundSourceMode)this.NotificationSoundSourceModeIndex;
                settings.NotificationSoundFilePath = this.NotificationSoundFilePath ?? "";
                settings.NotificationSoundVolumePercent = this.NotificationSoundVolumePercent;
                settings.NotificationSoundOutputDeviceId = this.NotificationSoundOutputDeviceId ?? "";
                settings.NotificationSoundEnabled = this.NotificationSoundEnabled;

                this._settingsService.SaveSettings(settings);
                this.StatusMessage = "✅ 設定を保存しました";
            }
            catch (Exception ex)
            {
                LogService.Error("設定の保存に失敗しました", ex);
                this.StatusMessage = $"設定の保存に失敗: {ex.Message}";
            }
        }

        private void LoadSettings()
        {
            try
            {
                var settings = this._settingsService.LoadSettings();
                this.SelectedTabIndex = settings.SelectedTabIndex;
                this.ChannelName = settings.ChannelName ?? "";
                this.OAuthToken = settings.OAuthToken ?? "";
                this.YouTubeTokenInfo = string.IsNullOrEmpty(settings.YouTubeTokenInfo) ? "未認可" : settings.YouTubeTokenInfo;

                this.RecentChannels.Clear();
                foreach (var ch in settings.RecentChannels)
                {
                    this.RecentChannels.Add(ch);
                }

                this.RaisePropertyChanged(nameof(this.HasRecentChannels));
                this.ShowReward = settings.ShowReward;
                this.ShowRaid = settings.ShowRaid;
                this.ShowFollow = settings.ShowFollow;
                this.ShowSubscribe = settings.ShowSubscribe;
                this.ShowGiftSubscribe = settings.ShowGiftSubscribe;
                this.ShowResub = settings.ShowResub;
                this.ShowHypeTrainBegin = settings.ShowHypeTrainBegin;
                this.ShowHypeTrainEnd = settings.ShowHypeTrainEnd;
                this.ShowYouTubeChat = settings.ShowYouTubeChat;
                this.ShowYouTubeSuperChat = settings.ShowYouTubeSuperChat;
                this.ShowYouTubeMembership = settings.ShowYouTubeMembership;
                this.YouTubeLegalConsentAccepted = settings.YouTubeLegalConsentAccepted;
                this.ObsWebSocketEnabled = settings.ObsWebSocketEnabled;
                this.ObsWebSocketHost = string.IsNullOrWhiteSpace(settings.ObsWebSocketHost) ? "127.0.0.1" : settings.ObsWebSocketHost;
                this.ObsWebSocketPort = settings.ObsWebSocketPort > 0 ? settings.ObsWebSocketPort : 4455;
                this.ObsWebSocketPassword = settings.ObsWebSocketPassword ?? "";
                this.ToastDurationSeconds = settings.ToastDurationSeconds > 0 ? settings.ToastDurationSeconds : 5;
                this.ToastMaxCount = settings.ToastMaxCount > 0 ? settings.ToastMaxCount : 5;
                this.ToastPositionIndex = (int)settings.ToastPosition;
                this.ToastMonitorIndex = (settings.ToastMonitorIndex >= 0 && settings.ToastMonitorIndex < WinForms.Screen.AllScreens.Length)
                    ? settings.ToastMonitorIndex : 0;
                this.ToastFontSize = settings.ToastFontSize > 0 ? settings.ToastFontSize : 12;
                this.ToastWidth = settings.ToastWidth > 0 ? settings.ToastWidth : 380;
                this.ToastBackgroundOpacity = settings.ToastBackgroundOpacity >= 0 ? settings.ToastBackgroundOpacity : 0.8;
                this.ToastFontFamily = settings.ToastFontFamily ?? "";
                this.ToastBackgroundModeIndex = (int)settings.ToastBackgroundMode;
                this.ToastCustomBackgroundColor = string.IsNullOrEmpty(settings.ToastCustomBackgroundColor)
                    ? "#1A1A2E"
                    : settings.ToastCustomBackgroundColor;
                this.ToastFontColorModeIndex = (int)settings.ToastFontColorMode;
                this.ToastCustomFontColor = string.IsNullOrEmpty(settings.ToastCustomFontColor)
                    ? "#FFFFFF"
                    : settings.ToastCustomFontColor;
                this.NotificationSoundSourceModeIndex = (int)settings.NotificationSoundSourceMode;
                this.NotificationSoundFilePath = settings.NotificationSoundFilePath ?? "";
                this.NotificationSoundVolumePercent = Math.Clamp(settings.NotificationSoundVolumePercent, 0, 100);
                this.NotificationSoundOutputDeviceId = this.ResolveAudioOutputDeviceId(settings.NotificationSoundOutputDeviceId);
                this.NotificationSoundEnabled = settings.NotificationSoundEnabled;
            }
            catch (Exception ex)
            {
                LogService.Error("設定の読み込みに失敗しました", ex);
                this.StatusMessage = $"設定の読み込みに失敗: {ex.Message}";
            }
        }

        private void ReloadAudioOutputDevices()
        {
            this.AudioOutputDevices.Clear();
            foreach (var device in this._notificationSoundService.GetOutputDevices())
            {
                this.AudioOutputDevices.Add(device);
            }
        }

        private string ResolveAudioOutputDeviceId(string deviceId)
        {
            if (string.IsNullOrWhiteSpace(deviceId))
            {
                return "";
            }

            foreach (var device in this.AudioOutputDevices)
            {
                if (string.Equals(device.Id, deviceId, StringComparison.Ordinal))
                {
                    return deviceId;
                }
            }

            return "";
        }

        private void BrowseNotificationSoundFile()
        {
            var dialog = new OpenFileDialog
            {
                Title = "通知音ファイルを選択",
                Filter = "音声ファイル (*.wav;*.mp3;*.ogg)|*.wav;*.mp3;*.ogg|WAV (*.wav)|*.wav|MP3 (*.mp3)|*.mp3|OGG (*.ogg)|*.ogg",
                CheckFileExists = true,
                Multiselect = false,
            };

            if (!string.IsNullOrWhiteSpace(this.NotificationSoundFilePath))
            {
                dialog.FileName = this.NotificationSoundFilePath;
            }

            var result = dialog.ShowDialog();
            if (result != true)
            {
                return;
            }

            this.NotificationSoundFilePath = dialog.FileName;
            this.NotificationSoundSourceModeIndex = 1;
            this.StatusMessage = "通知音ファイルを選択しました。設定を保存すると反映されます。";
        }

        private void PreviewNotificationSound()
        {
            try
            {
                this._notificationSoundService.PlayPreviewSound(new AppSettings
                {
                    NotificationSoundEnabled = this.NotificationSoundEnabled,
                    NotificationSoundSourceMode = (Services.NotificationSoundSourceMode)this.NotificationSoundSourceModeIndex,
                    NotificationSoundFilePath = this.NotificationSoundFilePath ?? "",
                    NotificationSoundVolumePercent = this.NotificationSoundVolumePercent,
                    NotificationSoundOutputDeviceId = this.NotificationSoundOutputDeviceId ?? "",
                });
                this.StatusMessage = "通知音をプレビュー再生しました";
            }
            catch (Exception ex)
            {
                LogService.Error("通知音プレビューの再生に失敗しました", ex);
                this.StatusMessage = $"通知音プレビューの再生に失敗: {ex.Message}";
            }
        }

        private void PreviewCommonComment()
        {
            this.PreviewComment(CreateCommonPreviewNotification(), "共通設定の表示プレビューを表示しました");
        }

        private void PreviewTwitchComment()
        {
            this.PreviewComment(CreateTwitchPreviewNotification(), "Twitch コメントプレビューを表示しました");
        }

        private void PreviewYouTubeComment()
        {
            this.PreviewComment(CreateYouTubePreviewNotification(), "YouTube コメントプレビューを表示しました");
        }

        private void PreviewComment(OverlayNotification notification, string successMessage)
        {
            try
            {
                this._toastService.ShowPreviewNotification(notification);
                this.StatusMessage = successMessage;
            }
            catch (Exception ex)
            {
                LogService.Error("コメントプレビューの表示に失敗しました", ex);
                this.StatusMessage = $"コメントプレビューの表示に失敗: {ex.Message}";
            }
        }

        private static OverlayNotification CreateCommonPreviewNotification()
        {
            return new OverlayNotification
            {
                SourcePlatform = "Preview",
                Type = NotificationType.Chat,
                Username = "Overlay Preview",
                DisplayText = "共通設定のフォントや背景がこの見た目で反映されます。",
                UserColor = "#4CAF50",
                Fragments =
                [
                    new TextFragment { Text = "共通設定のフォントや背景がこの見た目で反映されます。" }
                ]
            };
        }

        private static OverlayNotification CreateTwitchPreviewNotification()
        {
            return new OverlayNotification
            {
                SourcePlatform = "Twitch",
                Type = NotificationType.Chat,
                Username = "twitch_user_01",
                DisplayText = "今日は配信ありがとう！このコメント表示で確認できます。",
                UserColor = "#9146FF",
                Fragments =
                [
                    new TextFragment { Text = "今日は配信ありがとう！このコメント表示で確認できます。" }
                ]
            };
        }

        private static OverlayNotification CreateYouTubePreviewNotification()
        {
            return new OverlayNotification
            {
                SourcePlatform = "YouTube",
                Type = NotificationType.Chat,
                Username = "YouTube Viewer",
                DisplayText = "このプレビューで YouTube コメントの見え方を確認できます。",
                UserColor = "#FF3B30",
                Fragments =
                [
                    new TextFragment { Text = "このプレビューで YouTube コメントの見え方を確認できます。" }
                ]
            };
        }

        private async void AuthorizeYouTubeOAuth()
        {
            try
            {
                if (!this.YouTubeLegalConsentAccepted)
                {
                    this.YouTubeStatusMessage = "プライバシーポリシーと利用条件を確認し、同意してから認可してください";
                    return;
                }

                this.YouTubeStatusMessage = "YouTube OAuth 認可を開始しています...";
                var token = await this._youTubeOAuthService.AuthorizeAsync(BuildSecrets.YouTubeClientId);

                var settings = this._settingsService.LoadSettings();
                settings.YouTubeOAuthToken = token.AccessToken;
                settings.YouTubeRefreshToken = token.RefreshToken;
                settings.YouTubeTokenInfo = $"✅ 認可済み | 取得日: {DateTime.Now:yyyy/MM/dd HH:mm}";
                this._settingsService.SaveSettings(settings);

                this.YouTubeTokenInfo = settings.YouTubeTokenInfo;
                this.YouTubeStatusMessage = "✅ YouTube OAuth 認可が完了しました";
            }
            catch (YouTubeOAuthException ex) when (ex.IsUserDenied)
            {
                LogService.Info("YouTube OAuth認可はユーザーによりキャンセルされました");
                this.YouTubeStatusMessage = ex.Message;
            }
            catch (YouTubeOAuthException ex) when (ex.IsTimedOut)
            {
                LogService.Warning("YouTube OAuth認可はタイムアウトしました");
                this.YouTubeStatusMessage = ex.Message;
            }
            catch (Exception ex)
            {
                LogService.Error("YouTube OAuth認可エラー", ex);
                this.YouTubeStatusMessage = $"YouTube OAuth エラー: {ex.Message}";
            }
        }

        private bool CanAuthorizeYouTubeOAuth()
        {
            return this.YouTubeLegalConsentAccepted;
        }

        private bool CanConnectYouTube()
        {
            return this.YouTubeLegalConsentAccepted;
        }

        private async void ConnectYouTube()
        {
            if (!this.YouTubeLegalConsentAccepted)
            {
                this.YouTubeStatusMessage = "プライバシーポリシーと利用条件を確認し、同意してから接続してください";
                return;
            }

            var settings = this._settingsService.LoadSettings();
            if (string.IsNullOrWhiteSpace(settings.YouTubeOAuthToken))
            {
                this.YouTubeStatusMessage = "先にYouTube OAuth認可を実行してください";
                return;
            }

            try
            {
                this.YouTubeStatusMessage = "YouTube Live Chat に接続中...";
                this._settingsService.SaveSettings(settings);
                var (UseObsForDetection, ObsConnected) = await this.TryPrepareObsAsync(settings);
                var waitForObsSignal = UseObsForDetection;
                this._lastYouTubeWaitForObsSignal = waitForObsSignal;

                try
                {
                    await this._youTubeLiveChatService.ConnectAsync(
                        settings.YouTubeOAuthToken,
                        checkImmediately: !waitForObsSignal,
                        waitForObsSignalBeforePolling: waitForObsSignal);
                }
                catch (Exception ex) when (IsYouTubeUnauthorized(ex))
                {
                    if (string.IsNullOrWhiteSpace(settings.YouTubeRefreshToken))
                    {
                        throw;
                    }

                    var refreshed = await this._youTubeOAuthService.RefreshTokenAsync(BuildSecrets.YouTubeClientId, settings.YouTubeRefreshToken);
                    settings.YouTubeOAuthToken = refreshed.AccessToken;
                    settings.YouTubeRefreshToken = refreshed.RefreshToken;
                    settings.YouTubeTokenInfo = $"✅ 認可済み | 更新日: {DateTime.Now:yyyy/MM/dd HH:mm}";
                    this._settingsService.SaveSettings(settings);

                    this.YouTubeTokenInfo = settings.YouTubeTokenInfo;
                    await this._youTubeLiveChatService.ConnectAsync(
                        settings.YouTubeOAuthToken,
                        checkImmediately: !waitForObsSignal,
                        waitForObsSignalBeforePolling: waitForObsSignal);
                }

                this.YouTubeStatusMessage = this._youTubeLiveChatService.IsWaitingForBroadcast
                    ? BuildYouTubeWaitingMessage(waitForObsSignal)
                    : BuildYouTubeConnectedMessage("✅ YouTube Live Chat 接続完了");
                this.UpdateYouTubeTokenRefreshTimerState();
            }
            catch (Exception ex)
            {
                LogService.Error("YouTube接続エラー", ex);
                this.YouTubeStatusMessage = $"YouTube接続エラー: {ex.Message}";
            }
        }

        private void DisconnectYouTube()
        {
            this.StopYouTubeTokenRefreshTimer();
            this._youTubeLiveChatService.Disconnect();

            var settings = this._settingsService.LoadSettings();
            settings.YouTubeAutoConnectEnabled = false;
            this._settingsService.SaveSettings(settings);

            this.YouTubeStatusMessage = "YouTube接続を切断しました";
        }

        private void OpenPrivacyPolicy()
        {
            this.OpenBundledDocument(Path.Combine("Docs", "PrivacyPolicy.html"), "プライバシーポリシーを開きました");
        }

        private void OpenTermsOfUse()
        {
            this.OpenBundledDocument(Path.Combine("Docs", "TermsOfUse.html"), "利用条件を開きました");
        }

        private void OpenYouTubeTerms()
        {
            this.OpenExternalUrl(YouTubeTermsUrl, "YouTube 利用規約を開きました");
        }

        private void OpenGooglePrivacyPolicy()
        {
            this.OpenExternalUrl(GooglePrivacyPolicyUrl, "Google Privacy Policy を開きました");
        }

        private void OpenGooglePermissions()
        {
            this.OpenExternalUrl(GoogleSecurityPermissionsUrl, "Google 権限管理ページを開きました");
        }

        private void OpenSupport()
        {
            this.OpenExternalUrl(GitHubDiscussionsUrl, "サポート窓口を開きました");
        }

        private void ClearYouTubeAuthorization()
        {
            var result = MessageBox.Show(
                "この操作は、アプリに保存されている YouTube 認可情報を削除します。Google アカウント側の権限は取り消されません。続行しますか。",
                "YouTube 認可情報を削除",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            var settings = this._settingsService.LoadSettings();
            this.ResetYouTubeAuthorization(settings);
            this.YouTubeStatusMessage = "YouTube の保存済み認可情報を削除しました。Google 側の権限は権限管理ページから取り消してください";
        }

        private async void RevokeYouTubeAuthorization()
        {
            var result = MessageBox.Show(
                "この操作は、Google 側の YouTube API 権限取り消しを試行し、成功・失敗にかかわらずアプリに保存された認可情報も削除します。続行しますか。",
                "YouTube 権限を取り消して削除",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            var settings = this._settingsService.LoadSettings();
            var tokenToRevoke = string.IsNullOrWhiteSpace(settings.YouTubeRefreshToken)
                ? settings.YouTubeOAuthToken
                : settings.YouTubeRefreshToken;
            var revokeSucceeded = false;

            if (!string.IsNullOrWhiteSpace(tokenToRevoke))
            {
                try
                {
                    await this._youTubeOAuthService.RevokeTokenAsync(tokenToRevoke);
                    revokeSucceeded = true;
                }
                catch (Exception ex)
                {
                    LogService.Warning("YouTube OAuth 権限の取り消しに失敗しました。ローカル情報の削除は続行します", ex);
                }
            }

            this.ResetYouTubeAuthorization(settings);
            this.YouTubeStatusMessage = revokeSucceeded
                ? "YouTube 権限を取り消し、保存済み認可情報を削除しました"
                : "Google 側の権限取り消しは確認できませんでしたが、保存済み認可情報は削除しました。必要に応じて Google 権限管理ページも確認してください";
        }

        private void OpenBundledDocument(string relativePath, string successMessage)
        {
            try
            {
                var fullPath = Path.Combine(AppContext.BaseDirectory, relativePath);
                if (!File.Exists(fullPath))
                {
                    throw new FileNotFoundException($"文書ファイルが見つかりません: {fullPath}");
                }

                _ = Process.Start(new ProcessStartInfo(fullPath) { UseShellExecute = true });
                this.StatusMessage = successMessage;
            }
            catch (Exception ex)
            {
                LogService.Error("同梱文書の表示に失敗しました", ex);
                this.StatusMessage = $"文書を開けませんでした: {ex.Message}";
            }
        }

        private void OpenExternalUrl(string url, string successMessage)
        {
            try
            {
                _ = Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                this.StatusMessage = successMessage;
            }
            catch (Exception ex)
            {
                LogService.Error("外部リンクの表示に失敗しました", ex);
                this.StatusMessage = $"リンクを開けませんでした: {ex.Message}";
            }
        }

        private void ResetYouTubeAuthorization(AppSettings settings)
        {
            this.StopYouTubeTokenRefreshTimer();
            this._youTubeLiveChatService.Disconnect();

            settings.YouTubeOAuthToken = "";
            settings.YouTubeRefreshToken = "";
            settings.YouTubeTokenInfo = "";
            settings.YouTubeAutoConnectEnabled = false;
            this._settingsService.SaveSettings(settings);

            this.YouTubeTokenInfo = "未認可";
        }

        private async void OnYouTubeConnectionLost(object sender, YouTubeConnectionLostEventArgs e)
        {
            if (Interlocked.Exchange(ref this._isHandlingYouTubeConnectionLost, 1) == 1)
            {
                return;
            }

            try
            {
                this.StopYouTubeTokenRefreshTimer();
                var settings = this._settingsService.LoadSettings();

                if (e.IsUnauthorized && !string.IsNullOrWhiteSpace(settings.YouTubeRefreshToken))
                {
                    var refreshed = await this._youTubeOAuthService.RefreshTokenAsync(BuildSecrets.YouTubeClientId, settings.YouTubeRefreshToken);
                    settings.YouTubeOAuthToken = refreshed.AccessToken;
                    settings.YouTubeRefreshToken = refreshed.RefreshToken;
                    settings.YouTubeTokenInfo = $"✅ 認可済み | 更新日: {DateTime.Now:yyyy/MM/dd HH:mm}";
                    this._settingsService.SaveSettings(settings);
                    this.YouTubeTokenInfo = settings.YouTubeTokenInfo;
                }

                if (string.IsNullOrWhiteSpace(settings.YouTubeOAuthToken))
                {
                    this.YouTubeStatusMessage = "YouTube接続が切断されました（再認可が必要）";
                    return;
                }

                this.YouTubeStatusMessage = "YouTube再接続中...";
                var (UseObsForDetection, ObsConnected) = await this.TryPrepareObsAsync(settings);
                var waitForObsSignal = UseObsForDetection;
                this._lastYouTubeWaitForObsSignal = waitForObsSignal;
                await this._youTubeLiveChatService.ConnectAsync(
                    settings.YouTubeOAuthToken,
                    checkImmediately: !waitForObsSignal,
                    waitForObsSignalBeforePolling: waitForObsSignal);
                this.YouTubeStatusMessage = this._youTubeLiveChatService.IsWaitingForBroadcast
                    ? BuildYouTubeWaitingMessage(waitForObsSignal)
                    : BuildYouTubeConnectedMessage("✅ YouTube再接続完了");
                this.UpdateYouTubeTokenRefreshTimerState();
            }
            catch (Exception ex)
            {
                LogService.Warning("YouTube再接続に失敗しました", ex);
                this.YouTubeStatusMessage = $"YouTube再接続失敗: {ex.Message}";
            }
            finally
            {
                _ = Interlocked.Exchange(ref this._isHandlingYouTubeConnectionLost, 0);
            }
        }

        private async Task<(bool UseObsForDetection, bool ObsConnected)> TryPrepareObsAsync(AppSettings settings)
        {
            if (!settings.ObsWebSocketEnabled)
            {
                return (false, false);
            }

            var connected = this._obsWebSocketService.IsConnected || await this._obsWebSocketService.ConnectAsync(
                settings.ObsWebSocketHost,
                settings.ObsWebSocketPort,
                settings.ObsWebSocketPassword);

            if (!connected)
            {
                LogService.Warning("OBS連携が有効ですが接続できませんでした。即時配信確認にフォールバックします。");
                return (false, false);
            }

            return (true, true);
        }

        private void OnObsStreamingStateChanged(object sender, ObsStreamingStateChangedEventArgs e)
        {
            if (!e.IsStreaming)
            {
                return;
            }

            if (!this._youTubeLiveChatService.IsWaitingForBroadcast)
            {
                return;
            }

            this._youTubeLiveChatService.StartBroadcastPolling();
            _ = (Application.Current?.Dispatcher?.BeginInvoke(() =>
            {
                this.YouTubeStatusMessage = "⏳ OBS配信開始を検出。YouTube配信確認を開始しました...";
            }));
        }

        private void OnYouTubeBroadcastDetected(object sender, EventArgs e)
        {
            _ = (Application.Current?.Dispatcher?.BeginInvoke(() =>
            {
                this.YouTubeStatusMessage = BuildYouTubeConnectedMessage("✅ YouTube Live Chat 接続完了");
                this.UpdateYouTubeTokenRefreshTimerState();
            }));
        }

        private void OnYouTubeWaitingForBroadcastStarted(object sender, YouTubeWaitingForBroadcastEventArgs e)
        {
            var settings = this._settingsService.LoadSettings();
            var useObsForDetection = settings.ObsWebSocketEnabled && this._obsWebSocketService.IsConnected;

            _ = (Application.Current?.Dispatcher?.BeginInvoke(() =>
            {
                this.UpdateYouTubeTokenRefreshTimerState();
                this.YouTubeStatusMessage = BuildYouTubeWaitingMessage(useObsForDetection);
            }));
        }

        private void OnYouTubeBroadcastEnded(object sender, YouTubeBroadcastEndedEventArgs e)
        {
            _ = (Application.Current?.Dispatcher?.BeginInvoke(() =>
            {
                this.UpdateYouTubeTokenRefreshTimerState();
                this.YouTubeStatusMessage = $"ℹ️ {e.Message} 再開するには再接続してください";
            }));
        }

        private static string BuildYouTubeConnectedMessage(string prefix)
        {
            return $"{prefix} (gRPC ストリーム受信中)";
        }

        private static string BuildYouTubeWaitingMessage(bool waitForObsSignal)
        {
            return waitForObsSignal
                ? "⏳ 配信開始を待機中... (OBS の開始検出後に YouTube 配信確認)"
                : "⏳ 配信開始を待機中... (30秒間隔で配信確認)";
        }

        private static bool IsYouTubeUnauthorized(Exception ex)
        {
            return ex is YouTubeApiException apiEx && apiEx.StatusCode == 401;
        }

        private void InvalidateTwitchRefreshToken(AppSettings settings, string context)
        {
            if (settings == null)
            {
                return;
            }

            if (string.IsNullOrEmpty(settings.RefreshToken))
            {
                return;
            }

            settings.RefreshToken = "";
            this._settingsService.SaveSettings(settings);
            LogService.Warning($"[{context}] 無効なリフレッシュトークンを検知したため、保存済みリフレッシュトークンをクリアしました");
        }
    }
}
