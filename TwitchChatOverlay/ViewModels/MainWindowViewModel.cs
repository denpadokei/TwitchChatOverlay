using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Prism.Commands;
using Prism.Mvvm;
using TwitchChatOverlay.Services;
using WinForms = System.Windows.Forms;

namespace TwitchChatOverlay.ViewModels
{
    public class MainWindowViewModel : BindableBase
    {
        private string _title = "Twitch Chat Overlay";
        private string _channelName;
        private string _oauthToken;
        private string _statusMessage = "未接続";
        private string _deviceUserCode;
        private bool _isAuthorizingOAuth;
        private string _tokenInfo;
        private bool _hasToken;

        private bool _showReward = true;
        private bool _showRaid = true;
        private bool _showFollow = true;
        private bool _showSubscribe = true;
        private bool _showGiftSubscribe = true;
        private bool _showResub = true;
        private bool _showHypeTrainBegin = true;
        private bool _showHypeTrainEnd = true;
        private int _toastDurationSeconds = 5;
        private int _toastMaxCount = 5;
        private int _toastPositionIndex = 0;
        private int _toastMonitorIndex = 0;
        private double _toastFontSize = 12;
        private double _toastWidth = 380;
        private double _toastBackgroundOpacity = 0.8;
        private string _toastFontFamily = "";
        private int _toastBackgroundModeIndex = 0;
        private string _toastCustomBackgroundColor = "#1A1A2E";
        private int _toastFontColorModeIndex = 0;
        private string _toastCustomFontColor = "#FFFFFF";

        public ObservableCollection<string> MonitorList { get; } = new();

        public ObservableCollection<string> RecentChannels { get; } = new();

        public bool HasRecentChannels => RecentChannels.Count > 0;

        private bool _isUpdateAvailable;
        private bool _isUpdating;
        private int _updateProgressPercent;
        private string _latestVersion;
        private string _updateDownloadUrl;
        private string _updateChecksumUrl;
        private string _updateReleasePageUrl;

        public bool IsUpdateAvailable
        {
            get => _isUpdateAvailable;
            set => SetProperty(ref _isUpdateAvailable, value);
        }

        public bool IsUpdating
        {
            get => _isUpdating;
            set => SetProperty(ref _isUpdating, value);
        }

        public int UpdateProgressPercent
        {
            get => _updateProgressPercent;
            set => SetProperty(ref _updateProgressPercent, value);
        }

        public string LatestVersion
        {
            get => _latestVersion;
            set => SetProperty(ref _latestVersion, value);
        }

        private readonly TwitchApiService _apiService;
        private readonly TwitchEventSubService _eventSubService;
        private readonly ToastNotificationService _toastService;
        private readonly SettingsService _settingsService;
        private readonly UpdateService _updateService;
        private Timer _tokenRefreshTimer;
        /// <summary>次回タイマー設定用: refresh 直後の expires_in（秒）。0 のときはデフォルト値を使用。</summary>
        private int _nextRefreshExpiresIn;

        public string Title
        {
            get => _title;
            set => SetProperty(ref _title, value);
        }

        public string ChannelName
        {
            get => _channelName;
            set => SetProperty(ref _channelName, value);
        }

        public string OAuthToken
        {
            get => _oauthToken;
            set => SetProperty(ref _oauthToken, value);
        }

        /// <summary>Device Auth フロー中に表示するユーザーコード</summary>
        public string DeviceUserCode
        {
            get => _deviceUserCode;
            set => SetProperty(ref _deviceUserCode, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public bool IsAuthorizingOAuth
        {
            get => _isAuthorizingOAuth;
            set => SetProperty(ref _isAuthorizingOAuth, value);
        }

        /// <summary>保存済みトークンのログイン名と保存日を表示する文字列</summary>
        public string TokenInfo
        {
            get => _tokenInfo;
            set
            {
                SetProperty(ref _tokenInfo, value);
                HasToken = !string.IsNullOrEmpty(value);
            }
        }

        /// <summary>有効なトークン情報があるか（UI表示制御用）</summary>
        public bool HasToken
        {
            get => _hasToken;
            set => SetProperty(ref _hasToken, value);
        }

        public bool ShowReward
        {
            get => _showReward;
            set => SetProperty(ref _showReward, value);
        }

        public bool ShowRaid
        {
            get => _showRaid;
            set => SetProperty(ref _showRaid, value);
        }

        public bool ShowFollow
        {
            get => _showFollow;
            set => SetProperty(ref _showFollow, value);
        }

        public bool ShowSubscribe
        {
            get => _showSubscribe;
            set => SetProperty(ref _showSubscribe, value);
        }

        public bool ShowGiftSubscribe
        {
            get => _showGiftSubscribe;
            set => SetProperty(ref _showGiftSubscribe, value);
        }

        public bool ShowResub
        {
            get => _showResub;
            set => SetProperty(ref _showResub, value);
        }

        public bool ShowHypeTrainBegin
        {
            get => _showHypeTrainBegin;
            set => SetProperty(ref _showHypeTrainBegin, value);
        }

        public bool ShowHypeTrainEnd
        {
            get => _showHypeTrainEnd;
            set => SetProperty(ref _showHypeTrainEnd, value);
        }

        public int ToastDurationSeconds
        {
            get => _toastDurationSeconds;
            set => SetProperty(ref _toastDurationSeconds, value);
        }

        public int ToastMaxCount
        {
            get => _toastMaxCount;
            set => SetProperty(ref _toastMaxCount, value);
        }

        public int ToastPositionIndex
        {
            get => _toastPositionIndex;
            set => SetProperty(ref _toastPositionIndex, value);
        }

        public int ToastMonitorIndex
        {
            get => _toastMonitorIndex;
            set => SetProperty(ref _toastMonitorIndex, value);
        }

        public double ToastFontSize
        {
            get => _toastFontSize;
            set => SetProperty(ref _toastFontSize, value);
        }

        public double ToastWidth
        {
            get => _toastWidth;
            set => SetProperty(ref _toastWidth, value);
        }

        public double ToastBackgroundOpacity
        {
            get => _toastBackgroundOpacity;
            set
            {
                SetProperty(ref _toastBackgroundOpacity, value);
                RaisePropertyChanged(nameof(ToastBackgroundOpacityPercent));
            }
        }

        /// <summary>UI用: 透過率を 0〜100 の整数で表示・入力する。</summary>
        public int ToastBackgroundOpacityPercent
        {
            get => (int)Math.Round(_toastBackgroundOpacity * 100);
            set
            {
                int clamped = Math.Clamp(value, 0, 100);
                if (SetProperty(ref _toastBackgroundOpacity, clamped / 100.0))
                    RaisePropertyChanged(nameof(ToastBackgroundOpacity));
            }
        }

        public string ToastFontFamily
        {
            get => _toastFontFamily;
            set => SetProperty(ref _toastFontFamily, value);
        }

        public int ToastBackgroundModeIndex
        {
            get => _toastBackgroundModeIndex;
            set
            {
                SetProperty(ref _toastBackgroundModeIndex, value);
                RaisePropertyChanged(nameof(IsCustomBackgroundColor));
            }
        }

        public bool IsCustomBackgroundColor => _toastBackgroundModeIndex == 3;

        public string ToastCustomBackgroundColor
        {
            get => _toastCustomBackgroundColor;
            set => SetProperty(ref _toastCustomBackgroundColor, value);
        }

        public int ToastFontColorModeIndex
        {
            get => _toastFontColorModeIndex;
            set
            {
                SetProperty(ref _toastFontColorModeIndex, value);
                RaisePropertyChanged(nameof(IsCustomFontColor));
            }
        }

        public bool IsCustomFontColor => _toastFontColorModeIndex == 1;

        public string ToastCustomFontColor
        {
            get => _toastCustomFontColor;
            set => SetProperty(ref _toastCustomFontColor, value);
        }

        public System.Collections.Generic.List<string> FontFamilyPresets { get; } = new()
        {
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
        };

        public ICommand ConnectCommand { get; }
        public ICommand DisconnectCommand { get; }
        public ICommand AuthorizeOAuthCommand { get; }
        public ICommand SaveSettingsCommand { get; }
        public ICommand SelectRecentChannelCommand { get; }
        public ICommand UpdateCommand { get; }
        public ICommand OpenReleasePageCommand { get; }

        public MainWindowViewModel(
            SettingsService settingsService,
            TwitchApiService apiService,
            TwitchEventSubService eventSubService,
            ToastNotificationService toastService,
            UpdateService updateService)
        {
            _settingsService = settingsService;
            _apiService = apiService;
            _eventSubService = eventSubService;
            _toastService = toastService;
            _updateService = updateService;

            ConnectCommand = new DelegateCommand(Connect, CanConnect);
            DisconnectCommand = new DelegateCommand(Disconnect, CanDisconnect);
            AuthorizeOAuthCommand = new DelegateCommand(AuthorizeOAuth);
            SaveSettingsCommand = new DelegateCommand(SaveSettings);
            SelectRecentChannelCommand = new DelegateCommand<string>(ch => ChannelName = ch);
            UpdateCommand = new DelegateCommand(ExecuteUpdate, () => IsUpdateAvailable && !IsUpdating)
                .ObservesProperty(() => IsUpdateAvailable)
                .ObservesProperty(() => IsUpdating);
            OpenReleasePageCommand = new DelegateCommand(
                () => _updateService.OpenReleasePage(_updateReleasePageUrl),
                () => !string.IsNullOrEmpty(_updateReleasePageUrl))
                .ObservesProperty(() => IsUpdateAvailable);

            _toastService.Initialize(_eventSubService);

            // 予期しない切断（トークン期限切れ等）時に自動再接続
            _eventSubService.ConnectionLost += OnConnectionLost;

            // モニター一覧を構築
            var screens = WinForms.Screen.AllScreens;
            for (int i = 0; i < screens.Length; i++)
            {
                var s = screens[i];
                string label = $"モニター {i + 1}{(s.Primary ? " (プライマリ)" : "")} {s.Bounds.Width}x{s.Bounds.Height}";
                MonitorList.Add(label);
            }

            LoadSettings();
            _ = ValidateSavedTokenAsync();
            _ = CheckForUpdateAsync();
        }

        private async Task AutoConnectAsync()
        {
            if (string.IsNullOrWhiteSpace(ChannelName) ||
                string.IsNullOrWhiteSpace(OAuthToken))
                return;

            try
            {
                StatusMessage = "自動接続中...";
                var settings = _settingsService.LoadSettings();
                string broadcasterUserId = settings.BroadcasterUserId;
                string userId = settings.UserId;

                if (string.IsNullOrEmpty(broadcasterUserId) || string.IsNullOrEmpty(userId))
                    return;

                await _eventSubService.ConnectAsync(OAuthToken, BuildSecrets.ClientId, broadcasterUserId, userId);
                LogService.Info($"EventSub自動接続完了: {ChannelName}");
                StatusMessage = $"✅ 接続完了 ({ChannelName})"; 
                StartTokenRefreshTimer();
                ((DelegateCommand)ConnectCommand).RaiseCanExecuteChanged();
                ((DelegateCommand)DisconnectCommand).RaiseCanExecuteChanged();
            }
            catch (Exception ex)
            {
                LogService.Error("自動接続エラー", ex);
                StatusMessage = $"自動接続エラー: {ex.Message}";
            }
        }

        /// <summary>接続完了後にトークンリフレッシュタイマーを開始する。expires_in が既知の場合はそれに基づいて間隔を決定する。</summary>
        private void StartTokenRefreshTimer()
        {
            _tokenRefreshTimer?.Dispose();
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
            _tokenRefreshTimer = new Timer(
                _ => _ = RefreshTokenSilentlyAsync(),
                null,
                interval,
                interval);
        }

        private void StopTokenRefreshTimer()
        {
            _tokenRefreshTimer?.Dispose();
            _tokenRefreshTimer = null;
        }

        /// <summary>バックグラウンドでトークンをリフレッシュし、接続中なら再接続する。</summary>
        private async Task RefreshTokenSilentlyAsync()
        {
            var settings = _settingsService.LoadSettings();
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

                OAuthToken = newToken.AccessToken;
                settings.OAuthToken = newToken.AccessToken;
                settings.RefreshToken = newToken.RefreshToken;
                settings.OAuthTokenSavedAt = DateTime.UtcNow;
                _settingsService.SaveSettings(settings);

                string savedAt = DateTime.Now.ToString("yyyy/MM/dd HH:mm");
                TokenInfo = $"✅ {settings.OAuthTokenLogin ?? "(不明)"}  |  更新日: {savedAt}";
                string expiryInfo = newToken.ExpiresIn > 0
                    ? $"expires_in={newToken.ExpiresIn}s (期限: {DateTime.Now.AddSeconds(newToken.ExpiresIn):yyyy/MM/dd HH:mm:ss})"
                    : "expires_in 不明";
                LogService.Debug($"[SilentRefresh] 更新成功 user={settings.OAuthTokenLogin ?? "(不明)"} {expiryInfo}");

                // 接続中であればトークンを更新して再接続
                if (_eventSubService.IsConnected)
                {
                    LogService.Debug("[SilentRefresh] 接続中のため再接続を実行");
                    _eventSubService.Disconnect();
                    await Task.Delay(500);
                    _nextRefreshExpiresIn = newToken.ExpiresIn;
                    await AutoConnectAsync();
                    LogService.Debug("[SilentRefresh] 再接続完了");
                }
            }
            catch (TwitchTokenRefreshException ex) when (ex.IsInvalidRefreshToken)
            {
                InvalidateTwitchRefreshToken(settings, "SilentRefresh");
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
            StopTokenRefreshTimer();
            ((DelegateCommand)ConnectCommand).RaiseCanExecuteChanged();
            ((DelegateCommand)DisconnectCommand).RaiseCanExecuteChanged();

            var settings = _settingsService.LoadSettings();
            if (string.IsNullOrEmpty(settings.RefreshToken))
            {
                StatusMessage = "⚠️ 接続が切断されました。再接続するにはOAuth再認可が必要です";
                return;
            }

            try
            {
                StatusMessage = "🔄 接続が切断されました。トークンを更新して再接続中...";
                LogService.Warning("予期しない切断が発生しました。トークン更新と再接続を試みます");
                await Task.Delay(2000); // 少し待ってから再接続

                var oauthServer = new TwitchOAuthServer(BuildSecrets.ClientId);
                var newToken = await oauthServer.RefreshTokenAsync(settings.RefreshToken);

                OAuthToken = newToken.AccessToken;
                settings.OAuthToken = newToken.AccessToken;
                settings.RefreshToken = newToken.RefreshToken;
                settings.OAuthTokenSavedAt = DateTime.UtcNow;
                _settingsService.SaveSettings(settings);

                string savedAt = DateTime.Now.ToString("yyyy/MM/dd HH:mm");
                TokenInfo = $"✅ {settings.OAuthTokenLogin ?? "(不明)"}  |  更新日: {savedAt}";
                string expiryInfo = newToken.ExpiresIn > 0
                    ? $"expires_in={newToken.ExpiresIn}s (期限: {DateTime.Now.AddSeconds(newToken.ExpiresIn):yyyy/MM/dd HH:mm:ss})"
                    : "expires_in 不明";
                LogService.Debug($"[OnConnectionLost] トークン更新成功。再接続を開始 user={settings.OAuthTokenLogin ?? "(不明)"} {expiryInfo}");

                _nextRefreshExpiresIn = newToken.ExpiresIn;
                await AutoConnectAsync();
            }
            catch (TwitchTokenRefreshException ex) when (ex.IsInvalidRefreshToken)
            {
                InvalidateTwitchRefreshToken(settings, "OnConnectionLost");
                LogService.Error("再接続エラー。無効なリフレッシュトークンのため再認可が必要です", ex);
                StatusMessage = "⚠️ リフレッシュトークンが無効です。再認可してください";
                TokenInfo = "⚠️ トークンの更新に失敗しました。再認可してください";
                OAuthToken = "";
            }
            catch (Exception ex)
            {
                LogService.Error("再接続エラー。再認可が必要です", ex);
                StatusMessage = $"⚠️ 再接続失敗: {ex.Message}　再認可してください";
                TokenInfo = "⚠️ トークンの更新に失敗しました。再認可してください";
                OAuthToken = "";
            }
        }

        private async Task ValidateSavedTokenAsync()
        {
            var settings = _settingsService.LoadSettings();

            // まず保存済みアクセストークンを検証し、有効ならそのまま接続する
            if (!string.IsNullOrEmpty(OAuthToken))
            {
                try
                {
                    // 残り600秒（10分）未満なら有効でも先行リフレッシュする
                    const int RefreshThresholdSeconds = 600;

                    var (isValid, login, userId, expiresIn) = await _apiService.ValidateTokenAsync(OAuthToken);
                    if (isValid && expiresIn >= RefreshThresholdSeconds)
                    {
                        string savedAt = settings.OAuthTokenSavedAt.HasValue
                            ? settings.OAuthTokenSavedAt.Value.ToLocalTime().ToString("yyyy/MM/dd HH:mm")
                            : "不明";
                        TokenInfo = $"✅ {login ?? settings.OAuthTokenLogin ?? "(不明)"}  |  取得日: {savedAt}";
                        LogService.Debug($"[Startup] トークン有効 expires_in={expiresIn}s (期限: {DateTime.Now.AddSeconds(expiresIn):yyyy/MM/dd HH:mm:ss})");

                        // UserIdが未保存なら補完
                        if (!string.IsNullOrEmpty(userId) && string.IsNullOrEmpty(settings.UserId))
                        {
                            settings.UserId = userId;
                            settings.BroadcasterUserId = userId;
                            _settingsService.SaveSettings(settings);
                        }

                        await AutoConnectAsync();
                        return;
                    }

                    if (isValid)
                        LogService.Debug($"[Startup] アクセストークンの残り有効期限が {expiresIn} 秒のため先行リフレッシュします");
                    else
                        LogService.Debug("[Startup] 保存済みアクセストークンは無効でした。リフレッシュを試行します");

                    OAuthToken = "";
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
                    TokenInfo = "🔄 トークンを更新中...";
                    var oauthServer = new TwitchOAuthServer(BuildSecrets.ClientId);
                    var newToken = await oauthServer.RefreshTokenAsync(settings.RefreshToken);

                    OAuthToken = newToken.AccessToken;
                    settings.OAuthToken = newToken.AccessToken;
                    settings.RefreshToken = newToken.RefreshToken;
                    settings.OAuthTokenSavedAt = DateTime.UtcNow;
                    _settingsService.SaveSettings(settings);

                    string savedAt = DateTime.Now.ToString("yyyy/MM/dd HH:mm");
                    TokenInfo = $"✅ {settings.OAuthTokenLogin ?? "(不明)"}  |  更新日: {savedAt}";
                    string expiryInfo = newToken.ExpiresIn > 0
                        ? $"expires_in={newToken.ExpiresIn}s (期限: {DateTime.Now.AddSeconds(newToken.ExpiresIn):yyyy/MM/dd HH:mm:ss})"
                        : "expires_in 不明";
                    LogService.Debug($"[Startup] 起動時トークン更新成功 user={settings.OAuthTokenLogin ?? "(不明)"} {expiryInfo}");
                    _nextRefreshExpiresIn = newToken.ExpiresIn;
                    await AutoConnectAsync();
                    return;
                }
                catch (TwitchTokenRefreshException ex) when (ex.IsInvalidRefreshToken)
                {
                    InvalidateTwitchRefreshToken(settings, "Startup");
                    TokenInfo = "⚠️ トークンの有効期限が切れました。再認可してください";
                    OAuthToken = "";
                    LogService.Warning("起動時のトークン更新に失敗しました。リフレッシュトークンが無効です", ex);
                    return;
                }
                catch (Exception ex)
                {
                    LogService.Warning("起動時のトークン更新に失敗しました", ex);
                }
            }

            if (string.IsNullOrEmpty(OAuthToken))
                TokenInfo = null;
        }

        private async Task CheckForUpdateAsync()
        {
            try
            {
                var result = await _updateService.CheckForUpdateAsync();
                if (result.IsUpdateAvailable)
                {
                    LatestVersion = result.LatestVersion;
                    _updateDownloadUrl = result.DownloadUrl;
                    _updateChecksumUrl = result.ChecksumUrl;
                    _updateReleasePageUrl = result.ReleasePageUrl;
                    IsUpdateAvailable = true;
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
            if (string.IsNullOrEmpty(_updateDownloadUrl))
            {
                if (!string.IsNullOrEmpty(_updateReleasePageUrl))
                    _updateService.OpenReleasePage(_updateReleasePageUrl);
                return;
            }

            IsUpdating = true;
            UpdateProgressPercent = 0;

            try
            {
                var progress = new Progress<int>(p => UpdateProgressPercent = p);
                string filePath = await _updateService.DownloadUpdateAsync(_updateDownloadUrl, _updateChecksumUrl, progress);
                _updateService.LaunchInstaller(filePath);
                // LaunchInstaller が Shutdown を呼ぶため、ここには通常到達しない。
                // .zip の自己更新バッチ起動後も Shutdown するため同様。
            }
            catch (Exception ex)
            {
                LogService.Error("アップデートの実行に失敗しました", ex);
                StatusMessage = $"更新の失敗: {ex.Message}";
            }
            finally
            {
                IsUpdating = false;
                UpdateProgressPercent = 0;
            }
        }

        private async void AuthorizeOAuth()
        {
            try
            {
                IsAuthorizingOAuth = true;
                DeviceUserCode = "";
                StatusMessage = "デバイス認可を開始中...";

                var oauthServer = new TwitchOAuthServer(BuildSecrets.ClientId);
                var tokenResponse = await oauthServer.AuthorizeAsync((userCode, verUri) =>
                {
                    DeviceUserCode = userCode;
                    StatusMessage = $"ブラウザで {verUri} を開き、コード [{userCode}] を入力してください";
                });

                OAuthToken = tokenResponse.AccessToken;
                DeviceUserCode = "";

                StatusMessage = "ユーザー情報を取得中...";
                var (userId, login) = await _apiService.GetCurrentUserAsync(OAuthToken, BuildSecrets.ClientId);

                var settings = _settingsService.LoadSettings();
                settings.UserId = userId;
                settings.BroadcasterUserId = userId;
                settings.OAuthToken = OAuthToken;
                settings.RefreshToken = tokenResponse.RefreshToken;
                settings.OAuthTokenSavedAt = DateTime.UtcNow;
                settings.OAuthTokenLogin = login;
                _settingsService.SaveSettings(settings);

                string savedAt = DateTime.Now.ToString("yyyy/MM/dd HH:mm");
                TokenInfo = $"✅ {login}  |  取得日: {savedAt}";
                LogService.Info($"OAuth認可完了: login={login}");
                StatusMessage = $"✅ OAuth認可完了！ログイン: {login}";
            }
            catch (Exception ex)
            {
                LogService.Error("OAuth認可エラー", ex);
                StatusMessage = $"OAuth認可エラー: {ex.Message}";
                DeviceUserCode = "";
            }
            finally
            {
                IsAuthorizingOAuth = false;
            }
        }

        private async void Connect()
        {
            if (string.IsNullOrWhiteSpace(ChannelName) ||
                string.IsNullOrWhiteSpace(OAuthToken))
            {
                StatusMessage = "チャンネル名とOAuthトークンを入力してください";
                return;
            }

            try
            {
                StatusMessage = "EventSubに接続中...";
                var settings = _settingsService.LoadSettings();

                // 接続時にチャンネル名を自動保存
                settings.ChannelName = ChannelName;

                // 接続履歴を更新 (最大10件、重複を排除して先頭に追加)
                settings.RecentChannels.Remove(ChannelName);
                settings.RecentChannels.Insert(0, ChannelName);
                if (settings.RecentChannels.Count > 10)
                    settings.RecentChannels.RemoveAt(settings.RecentChannels.Count - 1);

                _settingsService.SaveSettings(settings);

                // UI の履歴リストを更新
                RecentChannels.Clear();
                foreach (var ch in settings.RecentChannels)
                    RecentChannels.Add(ch);
                RaisePropertyChanged(nameof(HasRecentChannels));

                string broadcasterUserId = settings.BroadcasterUserId;
                string userId = settings.UserId;

                if (string.IsNullOrEmpty(broadcasterUserId) || string.IsNullOrEmpty(userId))
                {
                    StatusMessage = "まずOAuth認可を行ってください";
                    return;
                }

                await _eventSubService.ConnectAsync(OAuthToken, BuildSecrets.ClientId, broadcasterUserId, userId);
                LogService.Info($"EventSub接続完了: {ChannelName}");
                StatusMessage = $"✅ 接続完了 ({ChannelName})";
                StartTokenRefreshTimer();
                ((DelegateCommand)ConnectCommand).RaiseCanExecuteChanged();
                ((DelegateCommand)DisconnectCommand).RaiseCanExecuteChanged();
            }
            catch (Exception ex)
            {
                LogService.Error("EventSub接続エラー", ex);
                StatusMessage = $"接続エラー: {ex.Message}";
            }
        }

        private void Disconnect()
        {
            LogService.Info("EventSub切断要求");
            StopTokenRefreshTimer();
            _eventSubService.Disconnect();
            StatusMessage = "切断しました";
            ((DelegateCommand)ConnectCommand).RaiseCanExecuteChanged();
            ((DelegateCommand)DisconnectCommand).RaiseCanExecuteChanged();
        }

        private bool CanConnect() => !_eventSubService.IsConnected;
        private bool CanDisconnect() => _eventSubService.IsConnected;

        private void SaveSettings()
        {
            try
            {
                var settings = _settingsService.LoadSettings();
                settings.ChannelName = ChannelName;
                settings.OAuthToken = OAuthToken;
                settings.ShowReward = ShowReward;
                settings.ShowRaid = ShowRaid;
                settings.ShowFollow = ShowFollow;
                settings.ShowSubscribe = ShowSubscribe;
                settings.ShowGiftSubscribe = ShowGiftSubscribe;
                settings.ShowResub = ShowResub;
                settings.ShowHypeTrainBegin = ShowHypeTrainBegin;
                settings.ShowHypeTrainEnd = ShowHypeTrainEnd;
                settings.ToastDurationSeconds = ToastDurationSeconds;
                settings.ToastMaxCount = ToastMaxCount;
                settings.ToastPosition = (Services.ToastPosition)ToastPositionIndex;
                settings.ToastMonitorIndex = ToastMonitorIndex;
                settings.ToastFontSize = ToastFontSize;
                settings.ToastWidth = ToastWidth;
                settings.ToastBackgroundOpacity = ToastBackgroundOpacity;
                settings.ToastFontFamily = ToastFontFamily;
                settings.ToastBackgroundMode = (Services.ToastBackgroundMode)ToastBackgroundModeIndex;
                settings.ToastCustomBackgroundColor = ToastCustomBackgroundColor;
                settings.ToastFontColorMode = (Services.ToastFontColorMode)ToastFontColorModeIndex;
                settings.ToastCustomFontColor = ToastCustomFontColor;

                _settingsService.SaveSettings(settings);
                StatusMessage = "✅ 設定を保存しました";
            }
            catch (Exception ex)
            {
                LogService.Error("設定の保存に失敗しました", ex);
                StatusMessage = $"設定の保存に失敗: {ex.Message}";
            }
        }

        private void LoadSettings()
        {
            try
            {
                var settings = _settingsService.LoadSettings();
                ChannelName = settings.ChannelName ?? "";
                OAuthToken = settings.OAuthToken ?? "";

                RecentChannels.Clear();
                foreach (var ch in settings.RecentChannels)
                    RecentChannels.Add(ch);
                RaisePropertyChanged(nameof(HasRecentChannels));
                ShowReward = settings.ShowReward;
                ShowRaid = settings.ShowRaid;
                ShowFollow = settings.ShowFollow;
                ShowSubscribe = settings.ShowSubscribe;
                ShowGiftSubscribe = settings.ShowGiftSubscribe;
                ShowResub = settings.ShowResub;
                ShowHypeTrainBegin = settings.ShowHypeTrainBegin;
                ShowHypeTrainEnd = settings.ShowHypeTrainEnd;
                ToastDurationSeconds = settings.ToastDurationSeconds > 0 ? settings.ToastDurationSeconds : 5;
                ToastMaxCount = settings.ToastMaxCount > 0 ? settings.ToastMaxCount : 5;
                ToastPositionIndex = (int)settings.ToastPosition;
                ToastMonitorIndex = (settings.ToastMonitorIndex >= 0 && settings.ToastMonitorIndex < WinForms.Screen.AllScreens.Length)
                    ? settings.ToastMonitorIndex : 0;
                ToastFontSize = settings.ToastFontSize > 0 ? settings.ToastFontSize : 12;
                ToastWidth = settings.ToastWidth > 0 ? settings.ToastWidth : 380;
                ToastBackgroundOpacity = settings.ToastBackgroundOpacity >= 0 ? settings.ToastBackgroundOpacity : 0.8;
                ToastFontFamily = settings.ToastFontFamily ?? "";
                ToastBackgroundModeIndex = (int)settings.ToastBackgroundMode;
                ToastCustomBackgroundColor = string.IsNullOrEmpty(settings.ToastCustomBackgroundColor)
                    ? "#1A1A2E"
                    : settings.ToastCustomBackgroundColor;
                ToastFontColorModeIndex = (int)settings.ToastFontColorMode;
                ToastCustomFontColor = string.IsNullOrEmpty(settings.ToastCustomFontColor)
                    ? "#FFFFFF"
                    : settings.ToastCustomFontColor;
            }
            catch (Exception ex)
            {
                LogService.Error("設定の読み込みに失敗しました", ex);
                StatusMessage = $"設定の読み込みに失敗: {ex.Message}";
            }
        }

        private void InvalidateTwitchRefreshToken(AppSettings settings, string context)
        {
            if (settings == null)
                return;

            if (string.IsNullOrEmpty(settings.RefreshToken))
                return;

            settings.RefreshToken = "";
            _settingsService.SaveSettings(settings);
            LogService.Warning($"[{context}] 無効なリフレッシュトークンを検知したため、保存済みリフレッシュトークンをクリアしました");
        }
    }
}
