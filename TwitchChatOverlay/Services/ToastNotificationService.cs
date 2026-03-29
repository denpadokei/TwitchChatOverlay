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
        private readonly List<ToastNotificationWindow> _activeToasts = new();

        private const double ToastHeight = 90;  // ActualHeight が取得できない場合の推定値
        private const double ToastMargin = 8;
        private const double ScreenMargin = 20;

        private (double left, double top, double right, double bottom) GetScreenBounds(int monitorIndex)
        {
            var screens = WinForms.Screen.AllScreens;
            var screen = (monitorIndex >= 0 && monitorIndex < screens.Length)
                ? screens[monitorIndex]
                : WinForms.Screen.PrimaryScreen;

            var source = System.Windows.PresentationSource.FromVisual(
                System.Windows.Application.Current.MainWindow);
            double dpiX = source?.CompositionTarget?.TransformFromDevice.M11 ?? 1.0;
            double dpiY = source?.CompositionTarget?.TransformFromDevice.M22 ?? 1.0;

            return (
                screen.WorkingArea.Left   * dpiX,
                screen.WorkingArea.Top    * dpiY,
                screen.WorkingArea.Right  * dpiX,
                screen.WorkingArea.Bottom * dpiY
            );
        }

        public ToastNotificationService(SettingsService settingsService)
        {
            _settingsService = settingsService;
        }

        public void Initialize(TwitchEventSubService eventSubService)
        {
            eventSubService.NotificationReceived += OnNotificationReceived;
        }

        private void OnNotificationReceived(object sender, OverlayNotification notification)
        {
            if (!ShouldShow(notification.Type))
                return;

            Application.Current.Dispatcher.Invoke(() => ShowToast(notification));
        }

        private bool ShouldShow(NotificationType type)
        {
            var settings = _settingsService.LoadSettings();
            return type switch
            {
                NotificationType.Chat => true,
                NotificationType.Reward => settings.ShowReward,
                NotificationType.Raid => settings.ShowRaid,
                NotificationType.Follow => settings.ShowFollow,
                NotificationType.Subscribe => settings.ShowSubscribe,
                NotificationType.GiftSubscribe => settings.ShowGiftSubscribe,
                NotificationType.Resub => settings.ShowResub,
                NotificationType.HypeTrainBegin => settings.ShowHypeTrainBegin,
                NotificationType.HypeTrainEnd => settings.ShowHypeTrainEnd,
                _ => true
            };
        }

        private void ShowToast(OverlayNotification notification)
        {
            var settings = _settingsService.LoadSettings();
            int maxCount = settings.ToastMaxCount > 0 ? settings.ToastMaxCount : 5;
            int durationMs = settings.ToastDurationSeconds > 0 ? settings.ToastDurationSeconds * 1000 : 5000;

            if (_activeToasts.Count >= maxCount)
                return;

            double fontSize = settings.ToastFontSize > 0 ? settings.ToastFontSize : 12;
            double toastWidth = settings.ToastWidth > 0 ? settings.ToastWidth : 380;
            double bgOpacity = Math.Clamp(settings.ToastBackgroundOpacity, 0.0, 1.0);
            string fontFamily = settings.ToastFontFamily ?? "";
            var bgMode = settings.ToastBackgroundMode;
            string customBgColor = settings.ToastCustomBackgroundColor ?? "#1A1A2E";
            var fontColorMode = settings.ToastFontColorMode;
            string customFontColor = settings.ToastCustomFontColor ?? "#FFFFFF";
            // 初期位置は (0,0) — ReorderToasts で正しい位置を設定する
            var toast = new ToastNotificationWindow(
                notification, 0, 0, fontSize, bgOpacity, toastWidth, fontFamily, bgMode, customBgColor, fontColorMode, customFontColor);

            _activeToasts.Add(toast);
            ReorderToasts(); // ActualHeight が確定する前の暫定配置

            // レンダリング完了後に実際の高さで再整列（特に下揃えの場合に重要）
            toast.ContentRendered += (s, e) => ReorderToasts();

            toast.Closed += (s, e) =>
            {
                _activeToasts.Remove(toast);
                ReorderToasts();
            };

            toast.ShowAndAutoClose(durationMs);
        }

        private void ReorderToasts()
        {
            if (_activeToasts.Count == 0) return;

            var settings = _settingsService.LoadSettings();
            var pos = settings.ToastPosition;
            var (sl, st, sr, sb) = GetScreenBounds(settings.ToastMonitorIndex);
            double toastWidth = settings.ToastWidth > 0 ? settings.ToastWidth : 380;

            bool isLeft = pos == ToastPosition.TopLeft || pos == ToastPosition.BottomLeft;
            bool isTop  = pos == ToastPosition.TopLeft || pos == ToastPosition.TopRight;

            double left = isLeft ? sl + ScreenMargin : sr - toastWidth - ScreenMargin;

            if (isTop)
            {
                double y = st + ScreenMargin;
                foreach (var t in _activeToasts)
                {
                    t.Left = left;
                    t.Top  = y;
                    double h = t.ActualHeight > 0 ? t.ActualHeight : ToastHeight;
                    y += h + ToastMargin;
                }
            }
            else
            {
                double y = sb - ScreenMargin;
                foreach (var t in _activeToasts)
                {
                    double h = t.ActualHeight > 0 ? t.ActualHeight : ToastHeight;
                    y -= h;
                    t.Left = left;
                    t.Top  = y;
                    y -= ToastMargin;
                }
            }
        }
    }
}
