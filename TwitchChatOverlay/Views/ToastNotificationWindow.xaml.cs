using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using TwitchChatOverlay.Models;

namespace TwitchChatOverlay.Views
{
    public partial class ToastNotificationWindow : Window
    {
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_LAYERED = 0x00080000;
        private const int WS_EX_NOACTIVATE = 0x08000000;

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hwnd, int index);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

        public ToastNotificationWindow(
            OverlayNotification notification,
            double left,
            double top,
            double fontSize,
            double backgroundOpacity = 0.8,
            double windowWidth = 380,
            string fontFamily = "",
            Services.ToastBackgroundMode bgMode = Services.ToastBackgroundMode.Dark,
            string customBgColor = "#1A1A2E",
            Services.ToastFontColorMode fontColorMode = Services.ToastFontColorMode.Auto,
            string customFontColor = "#FFFFFF")
        {
            this.InitializeComponent();
            this.DataContext = new ViewModels.ToastNotificationViewModel(
                notification, fontSize, backgroundOpacity, fontFamily,
                bgMode, customBgColor, fontColorMode, customFontColor);

            this.Width = windowWidth;
            this.Left = left;
            this.Top = top;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var hwnd = new WindowInteropHelper(this).Handle;
            var exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            _ = SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_NOACTIVATE);
        }

        public async void ShowAndAutoClose(int durationMs)
        {
            this.Show();
            await Task.Delay(durationMs);
            this.FadeOutAndClose();
        }

        private void FadeOutAndClose()
        {
            var animation = new DoubleAnimation
            {
                From = 1.0,
                To = 0.0,
                Duration = new Duration(TimeSpan.FromMilliseconds(500))
            };
            animation.Completed += (s, e) => this.Close();

            var storyboard = new Storyboard();
            Storyboard.SetTarget(animation, this);
            Storyboard.SetTargetProperty(animation, new PropertyPath(OpacityProperty));
            storyboard.Children.Add(animation);
            storyboard.Begin();
        }
    }
}