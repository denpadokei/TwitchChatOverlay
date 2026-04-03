using System.Windows.Controls;
using TwitchChatOverlay.ViewModels;

namespace TwitchChatOverlay.Views.Tabs
{
    public partial class YouTubeSettingsTabView : UserControl
    {
        public YouTubeSettingsTabView()
        {
            InitializeComponent();
        }

        private void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm)
                ObsPasswordBox.Password = vm.ObsWebSocketPassword;
        }

        private void OnObsPasswordChanged(object sender, System.Windows.RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm)
                vm.ObsWebSocketPassword = ObsPasswordBox.Password;
        }
    }
}
