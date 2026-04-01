using System.Windows;
using Prism.Regions;

namespace TwitchChatOverlay.Views
{
    public partial class MainWindow : Window
    {
        private readonly IRegionManager _regionManager;

        public MainWindow(IRegionManager regionManager)
        {
            _regionManager = regionManager;
            InitializeComponent();
            Loaded += (s, e) =>
            {
                var vm = DataContext as ViewModels.MainWindowViewModel;
                vm?.InitializeRegions(_regionManager);
            };

            Closed += (s, e) =>
            {
                var vm = DataContext as ViewModels.MainWindowViewModel;
                vm?.DisconnectCommand?.Execute(null);
                vm?.DisconnectYouTubeCommand?.Execute(null);
            };
        }
    }
}


