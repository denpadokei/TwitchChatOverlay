using System.Windows.Input;

namespace TwitchChatOverlay.ViewModels
{
    public class StreamerBotSettingsTabViewModel : TabViewModelBase
    {
        public StreamerBotSettingsTabViewModel(MainWindowViewModel mainWindowViewModel)
            : base(
                mainWindowViewModel,
                nameof(StreamerBotEnabled),
                nameof(StreamerBotHost),
                nameof(StreamerBotPort),
                nameof(StreamerBotPassword),
                nameof(StreamerBotStatusMessage),
                nameof(ShowStreamerBotTwitchChat),
                nameof(ShowStreamerBotTwitchNotifications),
                nameof(ShowStreamerBotYouTube),
                nameof(ShowStreamerBotKick),
                nameof(ConnectStreamerBotCommand),
                nameof(DisconnectStreamerBotCommand))
        {
        }

        public bool StreamerBotEnabled
        {
            get => this.MainWindowViewModel.StreamerBotEnabled;
            set => this.MainWindowViewModel.StreamerBotEnabled = value;
        }

        public string StreamerBotHost
        {
            get => this.MainWindowViewModel.StreamerBotHost;
            set => this.MainWindowViewModel.StreamerBotHost = value;
        }

        public int StreamerBotPort
        {
            get => this.MainWindowViewModel.StreamerBotPort;
            set => this.MainWindowViewModel.StreamerBotPort = value;
        }

        public string StreamerBotPassword
        {
            get => this.MainWindowViewModel.StreamerBotPassword;
            set => this.MainWindowViewModel.StreamerBotPassword = value;
        }

        public string StreamerBotStatusMessage => this.MainWindowViewModel.StreamerBotStatusMessage;

        public bool ShowStreamerBotTwitchChat
        {
            get => this.MainWindowViewModel.ShowStreamerBotTwitchChat;
            set => this.MainWindowViewModel.ShowStreamerBotTwitchChat = value;
        }

        public bool ShowStreamerBotTwitchNotifications
        {
            get => this.MainWindowViewModel.ShowStreamerBotTwitchNotifications;
            set => this.MainWindowViewModel.ShowStreamerBotTwitchNotifications = value;
        }

        public bool ShowStreamerBotYouTube
        {
            get => this.MainWindowViewModel.ShowStreamerBotYouTube;
            set => this.MainWindowViewModel.ShowStreamerBotYouTube = value;
        }

        public bool ShowStreamerBotKick
        {
            get => this.MainWindowViewModel.ShowStreamerBotKick;
            set => this.MainWindowViewModel.ShowStreamerBotKick = value;
        }

        public ICommand ConnectStreamerBotCommand => this.MainWindowViewModel.ConnectStreamerBotCommand;

        public ICommand DisconnectStreamerBotCommand => this.MainWindowViewModel.DisconnectStreamerBotCommand;
    }
}
