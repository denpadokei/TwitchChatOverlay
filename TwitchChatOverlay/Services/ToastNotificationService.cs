using System;
using System.Collections.Generic;
using System.Windows;
using TwitchChatOverlay.Models;
using TwitchChatOverlay.Views;
using WinForms = System.Windows.Forms;

namespace TwitchChatOverlay.Services
{
    public class ToastNotificationService
    {
        private readonly SettingsService _settingsService;
        private readonly NotificationSoundService _notificationSoundService;
        private readonly List<ToastNotificationWindow> _activeToasts = [];

        private const double ToastHeight = 90;  // ActualHeight が取得できない場合の推定値
        private const double ToastMargin = 8;
        private const double ScreenMargin = 20;

        private (double left, double top, double right, double bottom) GetScreenBounds(int monitorIndex)
        {
            var screens = WinForms.Screen.AllScreens;
            var screen = (monitorIndex >= 0 && monitorIndex < screens.Length)
                ? screens[monitorIndex]
                : WinForms.Screen.PrimaryScreen;

            // Default DPI to 1.0 when Application.Current or MainWindow is not available.
            var dpiX = 1.0;
            var dpiY = 1.0;

            var app = System.Windows.Application.Current;
            var mainWindow = app?.MainWindow;
            if (mainWindow != null)
            {
                var source = System.Windows.PresentationSource.FromVisual(mainWindow);
                var compositionTarget = source?.CompositionTarget;
                if (compositionTarget != null)
                {
                    dpiX = compositionTarget.TransformFromDevice.M11;
                    dpiY = compositionTarget.TransformFromDevice.M22;
                }
            }
            return (
                screen.WorkingArea.Left * dpiX,
                screen.WorkingArea.Top * dpiY,
                screen.WorkingArea.Right * dpiX,
                screen.WorkingArea.Bottom * dpiY
            );
        }

        public ToastNotificationService(SettingsService settingsService, NotificationSoundService notificationSoundService)
        {
            this._settingsService = settingsService;
            this._notificationSoundService = notificationSoundService;
        }

        public void Initialize(TwitchEventSubService twitchEventSubService, YouTubeLiveChatService youTubeLiveChatService, StreamerBotService streamerBotService)
        {
            twitchEventSubService.NotificationReceived += this.OnNotificationReceived;
            youTubeLiveChatService.NotificationReceived += this.OnNotificationReceived;
            streamerBotService.NotificationReceived += this.OnNotificationReceived;
        }

        public void ShowPreviewNotification(OverlayNotification notification)
        {
            if (notification == null)
            {
                return;
            }

            Application.Current.Dispatcher.Invoke(() =>
            {
                try
                {
                    this.ShowToast(notification);
                }
                catch (Exception ex)
                {
                    LogService.Error("プレビュー通知の表示中にエラーが発生しました", ex);
                }
            });
        }

        private void OnNotificationReceived(object sender, OverlayNotification notification)
        {
            if (!this.ShouldShow(notification))
            {
                return;
            }

            try
            {
                this._notificationSoundService.PlayNotificationSound(notification);
            }
            catch (Exception ex)
            {
                LogService.Error("通知音の再生中にエラーが発生しました", ex);
            }

            Application.Current.Dispatcher.Invoke(() =>
            {
                try
                {
                    this.ShowToast(notification);
                }
                catch (Exception ex)
                {
                    LogService.Error("トースト通知の表示中にエラーが発生しました", ex);
                }
            });
        }

        private bool ShouldShow(OverlayNotification notification)
        {
            var settings = this._settingsService.LoadSettings();
            var platform = notification.SourcePlatform ?? "";
            var isYouTube = string.Equals(platform, "YouTube", StringComparison.OrdinalIgnoreCase);
            var isKick = string.Equals(platform, "Kick", StringComparison.OrdinalIgnoreCase);

            // Kick イベントのフィルタリング
            if (isKick)
            {
                return notification.Type switch
                {
                    NotificationType.Chat => settings.ShowStreamerBotKick,
                    _ => settings.ShowStreamerBotKick && settings.ShowStreamerBotTwitchNotifications,
                };
            }

            return notification.Type switch
            {
                NotificationType.Chat => isYouTube ? settings.ShowYouTubeChat || settings.ShowStreamerBotYouTube : settings.ShowStreamerBotTwitchChat,
                NotificationType.Reward => isYouTube ? settings.ShowYouTubeSuperChat || settings.ShowStreamerBotYouTube : settings.ShowReward && settings.ShowStreamerBotTwitchNotifications,
                NotificationType.Raid => settings.ShowRaid && settings.ShowStreamerBotTwitchNotifications,
                NotificationType.Follow => settings.ShowFollow && settings.ShowStreamerBotTwitchNotifications,
                NotificationType.Subscribe => isYouTube ? settings.ShowYouTubeMembership || settings.ShowStreamerBotYouTube : settings.ShowSubscribe && settings.ShowStreamerBotTwitchNotifications,
                NotificationType.GiftSubscribe => settings.ShowGiftSubscribe && settings.ShowStreamerBotTwitchNotifications,
                NotificationType.Resub => settings.ShowResub && settings.ShowStreamerBotTwitchNotifications,
                NotificationType.HypeTrainBegin => settings.ShowHypeTrainBegin && settings.ShowStreamerBotTwitchNotifications,
                NotificationType.HypeTrainEnd => settings.ShowHypeTrainEnd && settings.ShowStreamerBotTwitchNotifications,
                _ => true
            };
        }

        private void ShowToast(OverlayNotification notification)
        {
            var settings = this._settingsService.LoadSettings();
            var maxCount = settings.ToastMaxCount > 0 ? settings.ToastMaxCount : 5;
            var durationMs = settings.ToastDurationSeconds > 0 ? settings.ToastDurationSeconds * 1000 : 5000;

            if (this._activeToasts.Count >= maxCount)
            {
                return;
            }

            var fontSize = settings.ToastFontSize > 0 ? settings.ToastFontSize : 12;
            var toastWidth = settings.ToastWidth > 0 ? settings.ToastWidth : 380;
            var bgOpacity = Math.Clamp(settings.ToastBackgroundOpacity, 0.0, 1.0);
            var fontFamily = settings.ToastFontFamily ?? "";
            var bgMode = settings.ToastBackgroundMode;
            var customBgColor = settings.ToastCustomBackgroundColor ?? "#1A1A2E";
            var fontColorMode = settings.ToastFontColorMode;
            var customFontColor = settings.ToastCustomFontColor ?? "#FFFFFF";
            // 初期位置は (0,0) — ReorderToasts で正しい位置を設定する
            var toast = new ToastNotificationWindow(
                notification, 0, 0, fontSize, bgOpacity, toastWidth, fontFamily, bgMode, customBgColor, fontColorMode, customFontColor);

            this._activeToasts.Add(toast);
            this.ReorderToasts(); // ActualHeight が確定する前の暫定配置

            // レンダリング完了後に実際の高さで再整列（特に下揃えの場合に重要）
            toast.ContentRendered += (s, e) => this.ReorderToasts();

            toast.Closed += (s, e) =>
            {
                _ = this._activeToasts.Remove(toast);
                this.ReorderToasts();
            };

            toast.ShowAndAutoClose(durationMs);
        }

        private void ReorderToasts()
        {
            if (this._activeToasts.Count == 0)
            {
                return;
            }

            var settings = this._settingsService.LoadSettings();
            var pos = settings.ToastPosition;
            var (sl, st, sr, sb) = this.GetScreenBounds(settings.ToastMonitorIndex);
            var toastWidth = settings.ToastWidth > 0 ? settings.ToastWidth : 380;

            var isLeft = pos == ToastPosition.TopLeft || pos == ToastPosition.BottomLeft;
            var isTop = pos == ToastPosition.TopLeft || pos == ToastPosition.TopRight;

            var left = isLeft ? sl + ScreenMargin : sr - toastWidth - ScreenMargin;

            if (isTop)
            {
                var y = st + ScreenMargin;
                foreach (var t in this._activeToasts)
                {
                    t.Left = left;
                    t.Top = y;
                    var h = t.ActualHeight > 0 ? t.ActualHeight : ToastHeight;
                    y += h + ToastMargin;
                }
            }
            else
            {
                var y = sb - ScreenMargin;
                foreach (var t in this._activeToasts)
                {
                    var h = t.ActualHeight > 0 ? t.ActualHeight : ToastHeight;
                    y -= h;
                    t.Left = left;
                    t.Top = y;
                    y -= ToastMargin;
                }
            }
        }
    }
}
