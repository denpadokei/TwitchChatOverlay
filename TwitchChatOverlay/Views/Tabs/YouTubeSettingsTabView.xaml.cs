using System.Windows.Controls;
using TwitchChatOverlay.Infrastructure;

namespace TwitchChatOverlay.Views.Tabs
{
    public partial class YouTubeSettingsTabView : UserControl
    {
        public YouTubeSettingsTabView()
        {
            this.InitializeComponent();
        }

        private void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
        {
            if (this.DataContext is ViewModels.YouTubeSettingsTabViewModel vm)
            {
                if (vm is IInitialized initialized)
                    initialized.Initialize();
                this.ObsPasswordBox.Password = vm.ObsWebSocketPassword;
            }
        }

        private void OnObsPasswordChanged(object sender, System.Windows.RoutedEventArgs e)
        {
            if (this.DataContext is ViewModels.YouTubeSettingsTabViewModel vm)
            {
                vm.ObsWebSocketPassword = this.ObsPasswordBox.Password;
            }
        }
    }
}
