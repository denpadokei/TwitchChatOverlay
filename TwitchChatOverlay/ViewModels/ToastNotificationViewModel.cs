using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using Prism.Mvvm;
using TwitchChatOverlay.Models;
using TwitchChatOverlay.Services;

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
        public FontFamily FontFamily { get; }
        public SolidColorBrush UserColorBrush { get; }
        public SolidColorBrush ThemeColorBrush { get; }
        public SolidColorBrush BackgroundBrush { get; }
        public SolidColorBrush TextForegroundBrush { get; }
        public SolidColorBrush SubTextForegroundBrush { get; }

        public ToastNotificationViewModel(
            OverlayNotification notification,
            double fontSize = 12,
            double backgroundOpacity = 0.8,
            string fontFamilyName = "",
            ToastBackgroundMode bgMode = ToastBackgroundMode.Dark,
            string customBgColor = "#1A1A2E",
            ToastFontColorMode fontColorMode = ToastFontColorMode.Auto,
            string customFontColor = "#FFFFFF")
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

            FontFamily = string.IsNullOrWhiteSpace(fontFamilyName)
                ? new FontFamily()
                : new FontFamily(fontFamilyName);

            (BackgroundBrush, TextForegroundBrush, SubTextForegroundBrush) =
                ResolveColors(backgroundOpacity, bgMode, customBgColor, fontColorMode, customFontColor);

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

        private static (SolidColorBrush bg, SolidColorBrush fg, SolidColorBrush sub)
            ResolveColors(double opacity, ToastBackgroundMode bgMode, string customBgHex,
                          ToastFontColorMode fontColorMode, string customFontHex)
        {
            byte alpha = (byte)Math.Clamp(opacity * 255, 0, 255);
            Color baseColor = bgMode switch
            {
                ToastBackgroundMode.Dark   => Color.FromRgb(0x1A, 0x1A, 0x2E),
                ToastBackgroundMode.Light  => Color.FromRgb(0xF5, 0xF5, 0xF5),
                ToastBackgroundMode.System => SystemColors.WindowColor,
                ToastBackgroundMode.Custom => ParseHex(customBgHex),
                _                          => Color.FromRgb(0x1A, 0x1A, 0x2E)
            };

            var bg = new SolidColorBrush(Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B));

            SolidColorBrush fg;
            SolidColorBrush sub;

            if (fontColorMode == ToastFontColorMode.Custom)
            {
                var fgColor = ParseHex(customFontHex);
                fg  = new SolidColorBrush(fgColor);
                // サブテキストはメイン色を少し薄くする
                sub = new SolidColorBrush(Color.FromArgb(0xCC, fgColor.R, fgColor.G, fgColor.B));
            }
            else
            {
                bool isDark = bgMode == ToastBackgroundMode.Dark ||
                              (bgMode == ToastBackgroundMode.Custom && IsColorDark(baseColor)) ||
                              (bgMode == ToastBackgroundMode.System && IsColorDark(SystemColors.WindowColor));

                fg  = new SolidColorBrush(isDark ? Colors.White : Color.FromRgb(0x1A, 0x1A, 0x2E));
                sub = new SolidColorBrush(isDark ? Color.FromRgb(0xCC, 0xCC, 0xCC) : Color.FromRgb(0x55, 0x55, 0x55));
            }

            return (bg, fg, sub);
        }

        private static bool IsColorDark(Color c)
        {
            double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
            return (0.299 * r + 0.587 * g + 0.114 * b) < 0.5;
        }

        private static Color ParseHex(string hex)
        {
            try { return (Color)ColorConverter.ConvertFromString(hex); }
            catch { return Color.FromRgb(0x1A, 0x1A, 0x2E); }
        }
    }
}
