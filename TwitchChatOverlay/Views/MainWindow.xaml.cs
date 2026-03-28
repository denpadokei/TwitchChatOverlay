using System.Windows;

namespace TwitchChatOverlay.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            Closed += (s, e) =>
            {
                var vm = DataContext as ViewModels.MainWindowViewModel;
                vm?.DisconnectCommand?.Execute(null);
            };
        }
    }
}


