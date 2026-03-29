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

    public class AppSettings
    {
        public string ChannelName { get; set; }
        public string OAuthToken { get; set; }
        public string RefreshToken { get; set; }
        public string ClientId { get; set; } = "3rrjg8z3rs5ski5hwfubvgjayh0zu4";
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
        /// <summary>トースト背景の不透明度 (0.0=完全透明 ? 1.0=完全不透明)</summary>
        public double ToastBackgroundOpacity { get; set; } = 0.8;
        /// <summary>トーストのフォントファミリー名。空白=システムデフォルト</summary>
        public string ToastFontFamily { get; set; } = "";
        public ToastBackgroundMode ToastBackgroundMode { get; set; } = ToastBackgroundMode.Dark;
        /// <summary>背景モードがCustomのときのHEXカラー (#RRGGBB)</summary>
        public string ToastCustomBackgroundColor { get; set; } = "#1A1A2E";
        public ToastFontColorMode ToastFontColorMode { get; set; } = ToastFontColorMode.Auto;
        /// <summary>フォント色モードがCustomのときのHEXカラー (#RRGGBB)</summary>
        public string ToastCustomFontColor { get; set; } = "#FFFFFF";

        // 通知表示設定 (初期値: すべて表示)
        public bool ShowReward { get; set; } = true;
        public bool ShowRaid { get; set; } = true;
        public bool ShowFollow { get; set; } = true;
        public bool ShowSubscribe { get; set; } = true;
        public bool ShowGiftSubscribe { get; set; } = true;
        public bool ShowResub { get; set; } = true;
        public bool ShowHypeTrainBegin { get; set; } = true;
        public bool ShowHypeTrainEnd { get; set; } = true;
        public System.Collections.Generic.List<string> RecentChannels { get; set; } = new();
    }

    public class SettingsService
    {
        private readonly string _settingsPath;
        private static readonly byte[] _encryptionKey = Encoding.UTF8.GetBytes("TwitchChatOverlaySecretKey123456"); // 32バイト

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
                string json = JsonSerializer.Serialize(settings);
                byte[] plaintext = Encoding.UTF8.GetBytes(json);

                using (var aes = Aes.Create())
                {
                    aes.Key = _encryptionKey;
                    aes.Mode = CipherMode.CBC;
                    aes.Padding = PaddingMode.PKCS7;

                    using (var encryptor = aes.CreateEncryptor(aes.Key, aes.IV))
                    {
                        using (var ms = new MemoryStream())
                        {
                            // IVを先頭に保存
                            ms.Write(aes.IV, 0, aes.IV.Length);

                            using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
                            {
                                cs.Write(plaintext, 0, plaintext.Length);
                                cs.FlushFinalBlock();
                            }

                            File.WriteAllBytes(_settingsPath, ms.ToArray());
                        }
                    }
                }
            }
            catch (Exception ex)
            {
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
                if (!File.Exists(_settingsPath))
                {
                    return new AppSettings();
                }

                byte[] encryptedData = File.ReadAllBytes(_settingsPath);

                using (var aes = Aes.Create())
                {
                    aes.Key = _encryptionKey;
                    aes.Mode = CipherMode.CBC;
                    aes.Padding = PaddingMode.PKCS7;

                    // IVを抽出
                    byte[] iv = new byte[aes.IV.Length];
                    Array.Copy(encryptedData, 0, iv, 0, iv.Length);
                    aes.IV = iv;

                    using (var decryptor = aes.CreateDecryptor(aes.Key, aes.IV))
                    {
                        using (var ms = new MemoryStream(encryptedData, iv.Length, encryptedData.Length - iv.Length))
                        {
                            using (var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read))
                            {
                                using (var sr = new StreamReader(cs, Encoding.UTF8))
                                {
                                    string json = sr.ReadToEnd();
                                    var settings = JsonSerializer.Deserialize<AppSettings>(json);
                                    // ClientIdが空の場合はデフォルト値を使用
                                    if (string.IsNullOrEmpty(settings.ClientId))
                                        settings.ClientId = "3rrjg8z3rs5ski5hwfubvgjayh0zu4";
                                    return settings;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"設定の読み込みに失敗しました: {ex.Message}", ex);
            }
        }
    }
}
