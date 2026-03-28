using System;
using System.Collections.Generic;
using System.Windows;
using TwitchChatOverlay.Models;
using TwitchChatOverlay.Views;

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

        private (double left, double top) GetToastPosition(ToastPosition position, int index)
        {
            double left = (position == ToastPosition.TopLeft || position == ToastPosition.BottomLeft)
                ? ScreenMargin
                : SystemParameters.PrimaryScreenWidth - ToastWidth - ScreenMargin;

            double top = (position == ToastPosition.TopLeft || position == ToastPosition.TopRight)
                ? ScreenMargin + index * (ToastHeight + ToastMargin)
                : SystemParameters.PrimaryScreenHeight - ScreenMargin - (index + 1) * (ToastHeight + ToastMargin);

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

            var (left, top) = GetToastPosition(settings.ToastPosition, _activeToasts.Count);
            var toast = new ToastNotificationWindow(notification, left, top);

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
                var (left, top) = GetToastPosition(settings.ToastPosition, i);
                _activeToasts[i].Left = left;
                _activeToasts[i].Top = top;
            }
        }
    }
}
