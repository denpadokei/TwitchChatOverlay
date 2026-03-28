using System;
using System.Collections.Generic;
using System.Windows.Media;
using Prism.Mvvm;
using TwitchChatOverlay.Models;

namespace TwitchChatOverlay.ViewModels
{
    public class ToastNotificationViewModel : BindableBase
    {
        public string TypeIcon { get; }
        public string Username { get; }
        public string DisplayText { get; }
        public string SubText { get; }
        public bool HasSubText { get; }
        public IReadOnlyList<object> Fragments { get; }
        public bool HasFragments { get; }
        public bool NoFragments { get; }
        public double FontSize { get; }
        public double UsernameFontSize { get; }
        public double SubTextFontSize { get; }
        public SolidColorBrush UserColorBrush { get; }
        public SolidColorBrush ThemeColorBrush { get; }

        public ToastNotificationViewModel(OverlayNotification notification, double fontSize = 12)
        {
            TypeIcon = notification.TypeIcon;
            Username = notification.Username;
            DisplayText = notification.DisplayText;
            SubText = notification.SubText;
            HasSubText = !string.IsNullOrEmpty(notification.SubText);
            Fragments = notification.Fragments?.Count > 0 ? notification.Fragments : null;
            HasFragments = Fragments?.Count > 0 == true;
            NoFragments = !HasFragments;
            FontSize = fontSize;
            UsernameFontSize = fontSize + 1;
            SubTextFontSize = Math.Max(8, fontSize - 1);
            ThemeColorBrush = new SolidColorBrush(notification.ThemeColor);

            if (!string.IsNullOrEmpty(notification.UserColor) &&
                notification.UserColor.StartsWith("#") &&
                notification.UserColor.Length == 7)
            {
                try
                {
                    var color = (Color)ColorConverter.ConvertFromString(notification.UserColor);
                    UserColorBrush = new SolidColorBrush(color);
                }
                catch
                {
                    UserColorBrush = new SolidColorBrush(Colors.White);
                }
            }
            else
            {
                UserColorBrush = new SolidColorBrush(Colors.White);
            }
        }
    }
}
