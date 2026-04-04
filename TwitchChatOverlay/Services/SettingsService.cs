using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;

namespace TwitchChatOverlay.Services
{
    public enum ToastPosition { TopRight = 0, TopLeft = 1, BottomRight = 2, BottomLeft = 3 }
    public enum ToastBackgroundMode { Dark = 0, Light = 1, System = 2, Custom = 3 }
    public enum ToastFontColorMode { Auto = 0, Custom = 1 }
    public enum NotificationSoundSourceMode { Embedded = 0, CustomFile = 1 }

    public class AppSettings
    {
        public int SelectedTabIndex { get; set; } = 0;
        public string ChannelName { get; set; }
        public string OAuthToken { get; set; }
        public string RefreshToken { get; set; }
        public string YouTubeOAuthToken { get; set; }
        public string YouTubeRefreshToken { get; set; }
        public string YouTubeTokenInfo { get; set; }
        public bool YouTubeAutoConnectEnabled { get; set; } = true;
        public string BroadcasterUserId { get; set; }
        public string UserId { get; set; }
        /// <summary>トークンを取得した日時（UTC）</summary>
        public DateTime? OAuthTokenSavedAt { get; set; }
        /// <summary>トークン取得時のTwitchログイン名</summary>
        public string OAuthTokenLogin { get; set; }
        public int ToastDurationSeconds { get; set; } = 5;
        public int ToastMaxCount { get; set; } = 5;
        public ToastPosition ToastPosition { get; set; } = ToastPosition.TopRight;
        public int ToastMonitorIndex { get; set; } = 0;
        public double ToastFontSize { get; set; } = 12;
        public double ToastWidth { get; set; } = 380;
        /// <summary>トースト背景の不透明度（0.0 = 完全透明 ～ 1.0 = 完全不透明）</summary>
        public double ToastBackgroundOpacity { get; set; } = 0.8;
        /// <summary>トーストのフォントファミリー名。空文字列の場合はシステムの既定フォントを使用</summary>
        public string ToastFontFamily { get; set; } = "";
        public ToastBackgroundMode ToastBackgroundMode { get; set; } = ToastBackgroundMode.Dark;
        /// <summary>背景モードが Custom のときに使用する背景色（HEX カラー形式: #RRGGBB）</summary>
        public string ToastCustomBackgroundColor { get; set; } = "#1A1A2E";
        public ToastFontColorMode ToastFontColorMode { get; set; } = ToastFontColorMode.Auto;
        /// <summary>フォント色モードが Custom のときに使用する文字色（HEX カラー形式: #RRGGBB）</summary>
        public string ToastCustomFontColor { get; set; } = "#FFFFFF";
        public NotificationSoundSourceMode NotificationSoundSourceMode { get; set; } = NotificationSoundSourceMode.Embedded;
        public string NotificationSoundFilePath { get; set; } = "";
        public int NotificationSoundVolumePercent { get; set; } = 100;
        public string NotificationSoundOutputDeviceId { get; set; } = "";
        public bool NotificationSoundEnabled { get; set; } = true;

        // 通知表示設定 (初期値: すべて表示)
        public bool ShowReward { get; set; } = true;
        public bool ShowRaid { get; set; } = true;
        public bool ShowFollow { get; set; } = true;
        public bool ShowSubscribe { get; set; } = true;
        public bool ShowGiftSubscribe { get; set; } = true;
        public bool ShowResub { get; set; } = true;
        public bool ShowHypeTrainBegin { get; set; } = true;
        public bool ShowHypeTrainEnd { get; set; } = true;
        public bool ShowYouTubeChat { get; set; } = true;
        public bool ShowYouTubeSuperChat { get; set; } = true;
        public bool ShowYouTubeMembership { get; set; } = true;
        public bool ObsWebSocketEnabled { get; set; } = false;
        public string ObsWebSocketHost { get; set; } = "127.0.0.1";
        public int ObsWebSocketPort { get; set; } = 4455;
        public string ObsWebSocketPassword { get; set; } = "";
        public System.Collections.Generic.List<string> RecentChannels { get; set; } = new();
    }

    public class SettingsService
    {
        private readonly string _settingsPath;
        private static readonly byte[] _legacyEncryptionKey = Encoding.UTF8.GetBytes("TwitchChatOverlaySecretKey123456"); // 32バイト
        private static readonly byte[] _settingsFormatHeader = Encoding.ASCII.GetBytes("TCOSET1\0");
        private readonly object _sync = new();
        private AppSettings _cachedSettings;

        public SettingsService()
        {
            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string appFolder = Path.Combine(appDataPath, "TwitchChatOverlay");
            Directory.CreateDirectory(appFolder);
            _settingsPath = Path.Combine(appFolder, "settings.json");
        }

        /// <summary>
        /// 設定を暗号化して保存
        /// </summary>
        public void SaveSettings(AppSettings settings)
        {
            try
            {
                var target = settings ?? new AppSettings();
                string json = JsonSerializer.Serialize(target);
                byte[] plaintext = Encoding.UTF8.GetBytes(json);
                byte[] protectedBytes = ProtectedData.Protect(plaintext, null, DataProtectionScope.CurrentUser);
                byte[] payload = new byte[_settingsFormatHeader.Length + protectedBytes.Length];
                Buffer.BlockCopy(_settingsFormatHeader, 0, payload, 0, _settingsFormatHeader.Length);
                Buffer.BlockCopy(protectedBytes, 0, payload, _settingsFormatHeader.Length, protectedBytes.Length);
                File.WriteAllBytes(_settingsPath, payload);

                lock (_sync)
                {
                    _cachedSettings = CloneSettings(target);
                }
            }
            catch (Exception ex)
            {
                LogService.Error("設定の保存に失敗しました", ex);
                throw new Exception($"設定の保存に失敗しました: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 暗号化された設定を読み込む
        /// </summary>
        public AppSettings LoadSettings()
        {
            try
            {
                lock (_sync)
                {
                    if (_cachedSettings != null)
                        return CloneSettings(_cachedSettings);
                }

                if (!File.Exists(_settingsPath))
                {
                    var defaults = new AppSettings();
                    lock (_sync)
                    {
                        _cachedSettings = CloneSettings(defaults);
                    }
                    return CloneSettings(defaults);
                }

                byte[] encryptedData = File.ReadAllBytes(_settingsPath);

                AppSettings loaded;
                if (HasCurrentHeader(encryptedData))
                {
                    loaded = LoadCurrentFormat(encryptedData);
                }
                else
                {
                    loaded = LoadLegacyFormat(encryptedData);
                    SaveSettings(loaded);
                }

                lock (_sync)
                {
                    _cachedSettings = CloneSettings(loaded);
                }

                return CloneSettings(loaded);
            }
            catch (Exception ex)
            {
                LogService.Error("設定の読み込みに失敗しました", ex);
                throw new Exception($"設定の読み込みに失敗しました: {ex.Message}", ex);
            }
        }

        private static bool HasCurrentHeader(byte[] data)
        {
            return data.Length > _settingsFormatHeader.Length &&
                   data.AsSpan(0, _settingsFormatHeader.Length).SequenceEqual(_settingsFormatHeader);
        }

        private static AppSettings LoadCurrentFormat(byte[] encryptedData)
        {
            try
            {
                var protectedData = encryptedData.AsSpan(_settingsFormatHeader.Length).ToArray();
                byte[] plaintext = ProtectedData.Unprotect(protectedData, null, DataProtectionScope.CurrentUser);
                string json = Encoding.UTF8.GetString(plaintext);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
            catch (Exception ex) when (ex is CryptographicException or JsonException or IOException or ArgumentException)
            {
                LogService.Warning($"設定ファイルの読み込みに失敗しました。デフォルト設定で起動します。: {ex.Message}");
                return new AppSettings();
            }
        }

        private static AppSettings LoadLegacyFormat(byte[] encryptedData)
        {
            try
            {
                using var aes = Aes.Create();
                aes.Key = _legacyEncryptionKey;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                if (encryptedData == null || encryptedData.Length < aes.IV.Length)
                    return new AppSettings();

                byte[] iv = new byte[aes.IV.Length];
                Array.Copy(encryptedData, 0, iv, 0, iv.Length);
                aes.IV = iv;

                using var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
                using var ms = new MemoryStream(encryptedData, iv.Length, encryptedData.Length - iv.Length);
                using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
                using var sr = new StreamReader(cs, Encoding.UTF8);
                string json = sr.ReadToEnd();
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
            catch (Exception ex) when (ex is CryptographicException or JsonException or IOException or ArgumentException)
            {
                LogService.Warning($"旧形式設定ファイルの読み込みに失敗しました。デフォルト設定で起動します。: {ex.Message}");
                return new AppSettings();
            }
        }

        private static AppSettings CloneSettings(AppSettings settings)
        {
            var json = JsonSerializer.Serialize(settings ?? new AppSettings());
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
    }
}
