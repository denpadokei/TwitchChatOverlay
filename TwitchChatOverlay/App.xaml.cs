using System.Windows;
using Prism.Ioc;
using TwitchChatOverlay.Services;
using TwitchChatOverlay.Views;

namespace TwitchChatOverlay
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App
    {
        protected override Window CreateShell()
        {
            return Container.Resolve<MainWindow>();
        }

        protected override void RegisterTypes(IContainerRegistry containerRegistry)
        {
            containerRegistry.RegisterSingleton<SettingsService>();
            containerRegistry.RegisterSingleton<TwitchApiService>();
            containerRegistry.RegisterSingleton<TwitchEventSubService>();
            containerRegistry.RegisterSingleton<ToastNotificationService>();
            containerRegistry.RegisterSingleton<UpdateService>();
        }
    }
}

