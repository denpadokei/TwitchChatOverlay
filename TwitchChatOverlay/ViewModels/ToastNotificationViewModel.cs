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
        public SolidColorBrush UserColorBrush { get; }
        public SolidColorBrush ThemeColorBrush { get; }

        public ToastNotificationViewModel(OverlayNotification notification)
        {
            TypeIcon = notification.TypeIcon;
            Username = notification.Username;
            DisplayText = notification.DisplayText;
            SubText = notification.SubText;
            HasSubText = !string.IsNullOrEmpty(notification.SubText);
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
