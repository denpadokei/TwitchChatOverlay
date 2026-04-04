using System;
using System.Threading.Tasks;
using System.Windows;
using Prism.Ioc;
using TwitchChatOverlay.Services;
using TwitchChatOverlay.Views;
using TwitchChatOverlay.Views.Tabs;

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
                    LogService.Error("致命的な未処理例外 (AppDomain.UnhandledException)", ex);
                else
                    LogService.Error($"致命的な未処理例外 (AppDomain.UnhandledException): {args.ExceptionObject}");
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
            return Container.Resolve<MainWindow>();
        }

        protected override void RegisterTypes(IContainerRegistry containerRegistry)
        {
            containerRegistry.RegisterSingleton<SettingsService>();
            containerRegistry.RegisterSingleton<NotificationSoundService>();
            containerRegistry.RegisterSingleton<TwitchApiService>();
            containerRegistry.RegisterSingleton<TwitchEventSubService>();
            containerRegistry.RegisterSingleton<ToastNotificationService>();
            containerRegistry.RegisterSingleton<UpdateService>();
            containerRegistry.RegisterSingleton<ObsWebSocketService>();
            containerRegistry.RegisterInstance<YouTubeOAuthService>(new YouTubeOAuthService(BuildSecrets.YouTubeClientSecret));
            containerRegistry.RegisterSingleton<YouTubeLiveChatService>();

            containerRegistry.RegisterForNavigation<CommonSettingsTabView>();
            containerRegistry.RegisterForNavigation<TwitchSettingsTabView>();
            containerRegistry.RegisterForNavigation<YouTubeSettingsTabView>();
        }
    }
}

