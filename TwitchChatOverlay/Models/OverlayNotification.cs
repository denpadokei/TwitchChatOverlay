using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace TwitchChatOverlay.Models
{
    public enum NotificationType
    {
        Chat,
        Reward,
        Raid,
        Follow,
        Subscribe,
        GiftSubscribe,
        Resub,
        HypeTrainBegin,
        HypeTrainEnd
    }

    public class OverlayNotification
    {
        public NotificationType Type { get; set; }
        public string Username { get; set; }
        public string DisplayText { get; set; }
        public string SubText { get; set; }
        public string UserColor { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public List<object> Fragments { get; set; } = new();

        public string TypeIcon => Type switch
        {
            NotificationType.Chat => "💬",
            NotificationType.Reward => "🎁",
            NotificationType.Raid => "⚔️",
            NotificationType.Follow => "❤️",
            NotificationType.Subscribe => "⭐",
            NotificationType.GiftSubscribe => "🎁",
            NotificationType.Resub => "🔄",
            NotificationType.HypeTrainBegin => "🚂",
            NotificationType.HypeTrainEnd => "🏁",
            _ => "💬"
        };

        public Color ThemeColor => Type switch
        {
            NotificationType.Chat => Color.FromRgb(255, 255, 255),
            NotificationType.Reward => Color.FromRgb(255, 215, 0),
            NotificationType.Raid => Color.FromRgb(145, 70, 255),
            NotificationType.Follow => Color.FromRgb(76, 175, 80),
            NotificationType.Subscribe => Color.FromRgb(30, 144, 255),
            NotificationType.GiftSubscribe => Color.FromRgb(0, 206, 209),
            NotificationType.Resub => Color.FromRgb(32, 178, 170),
            NotificationType.HypeTrainBegin => Color.FromRgb(255, 140, 0),
            NotificationType.HypeTrainEnd => Color.FromRgb(255, 140, 0),
            _ => Color.FromRgb(255, 255, 255)
        };
    }
}
