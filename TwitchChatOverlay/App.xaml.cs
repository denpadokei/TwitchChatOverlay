using Prism.Ioc;
using System;
using System.Threading.Tasks;
using System.Windows;
using TwitchChatOverlay.Services;
using TwitchChatOverlay.ViewModels;
using TwitchChatOverlay.Views;

namespace TwitchChatOverlay
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // ログシステムを最初に初期化
            LogService.Initialize();

            // UIスレッドの未処理例外をログ記録
            DispatcherUnhandledException += (sender, args) =>
            {
                LogService.Error("UIスレッド未処理例外 (DispatcherUnhandledException)", args.Exception);
                LogService.Flush();
            };

            // バックグラウンドスレッドの致命的な未処理例外をログ記録
            AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
            {
                if (args.ExceptionObject is Exception ex)
                {
                    LogService.Error("致命的な未処理例外 (AppDomain.UnhandledException)", ex);
                }
                else
                {
                    LogService.Error($"致命的な未処理例外 (AppDomain.UnhandledException): {args.ExceptionObject}");
                }
                // アプリ終了前にログを書き切る
                LogService.Flush();
            };

            // 非同期タスクの未処理例外をログ記録
            TaskScheduler.UnobservedTaskException += (sender, args) =>
            {
                LogService.Error("非同期タスクの未処理例外 (TaskScheduler.UnobservedTaskException)", args.Exception);
                args.SetObserved();
            };

            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            LogService.Shutdown();
            base.OnExit(e);
        }

        protected override Window CreateShell()
        {
            return this.Container.Resolve<MainWindow>();
        }

        protected override void RegisterTypes(IContainerRegistry containerRegistry)
        {
            _ = containerRegistry.RegisterSingleton<SettingsService>();
            _ = containerRegistry.RegisterSingleton<NotificationSoundService>();
            _ = containerRegistry.RegisterSingleton<TwitchApiService>();
            _ = containerRegistry.RegisterSingleton<TwitchEventSubService>();
            _ = containerRegistry.RegisterSingleton<ToastNotificationService>();
            _ = containerRegistry.RegisterSingleton<UpdateService>();
            _ = containerRegistry.RegisterSingleton<ObsWebSocketService>();
            _ = containerRegistry.RegisterInstance<YouTubeOAuthService>(new YouTubeOAuthService(BuildSecrets.YouTubeClientSecret));
            _ = containerRegistry.RegisterSingleton<YouTubeLiveChatService>();
            _ = containerRegistry.RegisterSingleton<StreamerBotService>();
            _ = containerRegistry.RegisterSingleton<MainWindowViewModel>();
            _ = containerRegistry.Register<CommonSettingsTabViewModel>();
            _ = containerRegistry.Register<TwitchSettingsTabViewModel>();
            _ = containerRegistry.Register<YouTubeSettingsTabViewModel>();
            _ = containerRegistry.Register<StreamerBotSettingsTabViewModel>();
        }
    }
}

