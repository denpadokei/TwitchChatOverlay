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

        private const double ToastHeight = 90;
        private const double ToastMargin = 8;
        private const double ToastWidth = 380;
        private const double ScreenMargin = 20;

        private (double left, double top) GetToastPosition(ToastPosition position, int index, int monitorIndex)
        {
            var screens = WinForms.Screen.AllScreens;
            var screen = (monitorIndex >= 0 && monitorIndex < screens.Length)
                ? screens[monitorIndex]
                : WinForms.Screen.PrimaryScreen;

            // WPF の論理ピクセルへ変換 (DPI スケール考慮)
            var source = System.Windows.PresentationSource.FromVisual(
                System.Windows.Application.Current.MainWindow);
            double dpiX = source?.CompositionTarget?.TransformFromDevice.M11 ?? 1.0;
            double dpiY = source?.CompositionTarget?.TransformFromDevice.M22 ?? 1.0;

            double screenLeft   = screen.WorkingArea.Left   * dpiX;
            double screenTop    = screen.WorkingArea.Top    * dpiY;
            double screenRight  = screen.WorkingArea.Right  * dpiX;
            double screenBottom = screen.WorkingArea.Bottom * dpiY;
            double screenWidth  = screen.WorkingArea.Width  * dpiX;

            double left = (position == ToastPosition.TopLeft || position == ToastPosition.BottomLeft)
                ? screenLeft + ScreenMargin
                : screenRight - ToastWidth - ScreenMargin;

            double top = (position == ToastPosition.TopLeft || position == ToastPosition.TopRight)
                ? screenTop + ScreenMargin + index * (ToastHeight + ToastMargin)
                : screenBottom - ScreenMargin - (index + 1) * (ToastHeight + ToastMargin);

            return (left, top);
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

            var (left, top) = GetToastPosition(settings.ToastPosition, _activeToasts.Count, settings.ToastMonitorIndex);
            double fontSize = settings.ToastFontSize > 0 ? settings.ToastFontSize : 12;
            var toast = new ToastNotificationWindow(notification, left, top, fontSize);

            _activeToasts.Add(toast);
            toast.Closed += (s, e) =>
            {
                _activeToasts.Remove(toast);
                ReorderToasts();
            };

            toast.ShowAndAutoClose(durationMs);
        }

        private void ReorderToasts()
        {
            var settings = _settingsService.LoadSettings();
            for (int i = 0; i < _activeToasts.Count; i++)
            {
                var (left, top) = GetToastPosition(settings.ToastPosition, i, settings.ToastMonitorIndex);
                _activeToasts[i].Left = left;
                _activeToasts[i].Top = top;
            }
        }
    }
}
