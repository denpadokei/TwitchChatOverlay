using System.Windows;

namespace TwitchChatOverlay.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            this.InitializeComponent();

            Closed += (s, e) =>
            {
                var vm = this.DataContext as ViewModels.MainWindowViewModel;
                vm?.DisconnectCommand?.Execute(null);
                vm?.DisconnectYouTubeCommand?.Execute(null);
            };
        }
    }
}

