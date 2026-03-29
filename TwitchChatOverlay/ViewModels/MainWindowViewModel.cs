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
        private string _clientId;
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

        private readonly TwitchApiService _apiService;
        private readonly TwitchEventSubService _eventSubService;
        private readonly ToastNotificationService _toastService;
        private readonly SettingsService _settingsService;
        private Timer _tokenRefreshTimer;

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

        public string ClientId
        {
            get => _clientId;
            set => SetProperty(ref _clientId, value);
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

        public MainWindowViewModel(
            SettingsService settingsService,
            TwitchApiService apiService,
            TwitchEventSubService eventSubService,
            ToastNotificationService toastService)
        {
            _settingsService = settingsService;
            _apiService = apiService;
            _eventSubService = eventSubService;
            _toastService = toastService;

            ConnectCommand = new DelegateCommand(Connect, CanConnect);
            DisconnectCommand = new DelegateCommand(Disconnect, CanDisconnect);
            AuthorizeOAuthCommand = new DelegateCommand(AuthorizeOAuth);
            SaveSettingsCommand = new DelegateCommand(SaveSettings);
            SelectRecentChannelCommand = new DelegateCommand<string>(ch => ChannelName = ch);

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
        }

        private async Task AutoConnectAsync()
        {
            if (string.IsNullOrWhiteSpace(ChannelName) ||
                string.IsNullOrWhiteSpace(OAuthToken) ||
                string.IsNullOrWhiteSpace(ClientId))
                return;

            try
            {
                StatusMessage = "自動接続中...";
                var settings = _settingsService.LoadSettings();
                string broadcasterUserId = settings.BroadcasterUserId;
                string userId = settings.UserId;

                if (string.IsNullOrEmpty(broadcasterUserId) || string.IsNullOrEmpty(userId))
                    return;

                await _eventSubService.ConnectAsync(OAuthToken, ClientId, broadcasterUserId, userId);
                StatusMessage = $"✅ 接続完了 ({ChannelName})";
                StartTokenRefreshTimer();
                ((DelegateCommand)ConnectCommand).RaiseCanExecuteChanged();
                ((DelegateCommand)DisconnectCommand).RaiseCanExecuteChanged();
            }
            catch (Exception ex)
            {
                StatusMessage = $"自動接続エラー: {ex.Message}";
            }
        }

        /// <summary>接続完了後に3時間ごとのトークン予防的リフレッシュタイマーを開始する。</summary>
        private void StartTokenRefreshTimer()
        {
            _tokenRefreshTimer?.Dispose();
            // 3時間ごとに実行（アクセストークンは約4時間で期限切れ）
            _tokenRefreshTimer = new Timer(
                _ => _ = RefreshTokenSilentlyAsync(),
                null,
                TimeSpan.FromHours(3),
                TimeSpan.FromHours(3));
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
                return;

            try
            {
                var oauthServer = new TwitchOAuthServer(ClientId);
                var newToken = await oauthServer.RefreshTokenAsync(settings.RefreshToken);

                OAuthToken = newToken.AccessToken;
                settings.OAuthToken = newToken.AccessToken;
                settings.RefreshToken = newToken.RefreshToken;
                settings.OAuthTokenSavedAt = DateTime.UtcNow;
                _settingsService.SaveSettings(settings);

                string savedAt = DateTime.Now.ToString("yyyy/MM/dd HH:mm");
                TokenInfo = $"✅ {settings.OAuthTokenLogin ?? "(不明)"}  |  更新日: {savedAt}";

                // 接続中であればトークンを更新して再接続
                if (_eventSubService.IsConnected)
                {
                    _eventSubService.Disconnect();
                    await Task.Delay(500);
                    await AutoConnectAsync();
                }
            }
            catch
            {
                // サイレントなのでエラーは無視（切断イベントで再試行される）
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
                await Task.Delay(2000); // 少し待ってから再接続

                var oauthServer = new TwitchOAuthServer(ClientId);
                var newToken = await oauthServer.RefreshTokenAsync(settings.RefreshToken);

                OAuthToken = newToken.AccessToken;
                settings.OAuthToken = newToken.AccessToken;
                settings.RefreshToken = newToken.RefreshToken;
                settings.OAuthTokenSavedAt = DateTime.UtcNow;
                _settingsService.SaveSettings(settings);

                string savedAt = DateTime.Now.ToString("yyyy/MM/dd HH:mm");
                TokenInfo = $"✅ {settings.OAuthTokenLogin ?? "(不明)"}  |  更新日: {savedAt}";

                await AutoConnectAsync();
            }
            catch (Exception ex)
            {
                StatusMessage = $"⚠️ 再接続失敗: {ex.Message}　再認可してください";
                TokenInfo = "⚠️ トークンの更新に失敗しました。再認可してください";
                OAuthToken = "";
            }
        }

        private async Task ValidateSavedTokenAsync()
        {
            if (string.IsNullOrEmpty(OAuthToken))
            {
                TokenInfo = null;
                return;
            }

            try
            {
                var (isValid, login, userId) = await _apiService.ValidateTokenAsync(OAuthToken);
                if (isValid)
                {
                    var settings = _settingsService.LoadSettings();
                    string savedAt = settings.OAuthTokenSavedAt.HasValue
                        ? settings.OAuthTokenSavedAt.Value.ToLocalTime().ToString("yyyy/MM/dd HH:mm")
                        : "不明";
                    TokenInfo = $"✅ {login ?? settings.OAuthTokenLogin ?? "(不明)"}  |  取得日: {savedAt}";

                    // UserIdが未保存なら補完
                    if (!string.IsNullOrEmpty(userId) && string.IsNullOrEmpty(settings.UserId))
                    {
                        settings.UserId = userId;
                        settings.BroadcasterUserId = userId;
                        _settingsService.SaveSettings(settings);
                    }

                    // トークン有効 → 自動接続
                    await AutoConnectAsync();
                }
                else
                {
                    // リフレッシュトークンで自動更新を試みる
                    var settings = _settingsService.LoadSettings();
                    if (!string.IsNullOrEmpty(settings.RefreshToken))
                    {
                        try
                        {
                            TokenInfo = "🔄 トークンを更新中...";
                            var oauthServer = new TwitchOAuthServer(ClientId);
                            var newToken = await oauthServer.RefreshTokenAsync(settings.RefreshToken);

                            OAuthToken = newToken.AccessToken;
                            settings.OAuthToken = newToken.AccessToken;
                            settings.RefreshToken = newToken.RefreshToken;
                            settings.OAuthTokenSavedAt = DateTime.UtcNow;
                            _settingsService.SaveSettings(settings);

                            string savedAt = DateTime.Now.ToString("yyyy/MM/dd HH:mm");
                            TokenInfo = $"✅ {settings.OAuthTokenLogin ?? "(不明)"}  |  更新日: {savedAt}";
                            await AutoConnectAsync();
                            return;
                        }
                        catch
                        {
                            // リフレッシュ失敗 → 再認可を促す
                        }
                    }

                    TokenInfo = "⚠️ トークンの有効期限が切れました。再認可してください";
                    OAuthToken = "";
                }
            }
            catch
            {
                // ネットワークエラーなどは無視
            }
        }

        private async void AuthorizeOAuth()
        {
            if (string.IsNullOrWhiteSpace(ClientId))
            {
                StatusMessage = "Client IDを入力してください";
                return;
            }

            try
            {
                IsAuthorizingOAuth = true;
                DeviceUserCode = "";
                StatusMessage = "デバイス認可を開始中...";

                var oauthServer = new TwitchOAuthServer(ClientId);
                var tokenResponse = await oauthServer.AuthorizeAsync((userCode, verUri) =>
                {
                    DeviceUserCode = userCode;
                    StatusMessage = $"ブラウザで {verUri} を開き、コード [{userCode}] を入力してください";
                });

                OAuthToken = tokenResponse.AccessToken;
                DeviceUserCode = "";

                StatusMessage = "ユーザー情報を取得中...";
                var (userId, login) = await _apiService.GetCurrentUserAsync(OAuthToken, ClientId);

                var settings = _settingsService.LoadSettings();
                settings.UserId = userId;
                settings.BroadcasterUserId = userId;
                settings.OAuthToken = OAuthToken;
                settings.RefreshToken = tokenResponse.RefreshToken;
                settings.ClientId = ClientId;
                settings.OAuthTokenSavedAt = DateTime.UtcNow;
                settings.OAuthTokenLogin = login;
                _settingsService.SaveSettings(settings);

                string savedAt = DateTime.Now.ToString("yyyy/MM/dd HH:mm");
                TokenInfo = $"✅ {login}  |  取得日: {savedAt}";
                StatusMessage = $"✅ OAuth認可完了！ログイン: {login}";
            }
            catch (Exception ex)
            {
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
                string.IsNullOrWhiteSpace(OAuthToken) ||
                string.IsNullOrWhiteSpace(ClientId))
            {
                StatusMessage = "チャンネル名、OAuthトークン、Client IDを入力してください";
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

                await _eventSubService.ConnectAsync(OAuthToken, ClientId, broadcasterUserId, userId);
                StatusMessage = $"✅ 接続完了 ({ChannelName})";
                StartTokenRefreshTimer();
                ((DelegateCommand)ConnectCommand).RaiseCanExecuteChanged();
                ((DelegateCommand)DisconnectCommand).RaiseCanExecuteChanged();
            }
            catch (Exception ex)
            {
                StatusMessage = $"接続エラー: {ex.Message}";
            }
        }

        private void Disconnect()
        {
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
                settings.ClientId = ClientId;
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
                ClientId = settings.ClientId ?? "";

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
                StatusMessage = $"設定の読み込みに失敗: {ex.Message}";
            }
        }
    }
}
