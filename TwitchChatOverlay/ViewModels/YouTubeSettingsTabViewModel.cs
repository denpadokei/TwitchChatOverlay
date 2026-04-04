using System.Windows.Input;

namespace TwitchChatOverlay.ViewModels
{
    public class YouTubeSettingsTabViewModel : TabViewModelBase
    {
        public YouTubeSettingsTabViewModel(MainWindowViewModel mainWindowViewModel)
            : base(
                mainWindowViewModel,
                nameof(YouTubeTokenInfo),
                nameof(AuthorizeYouTubeOAuthCommand),
                nameof(ConnectYouTubeCommand),
                nameof(DisconnectYouTubeCommand),
                nameof(ShowYouTubeChat),
                nameof(ShowYouTubeSuperChat),
                nameof(ShowYouTubeMembership),
                nameof(ObsWebSocketEnabled),
                nameof(ObsWebSocketHost),
                nameof(ObsWebSocketPort),
                nameof(ObsWebSocketPassword),
                nameof(YouTubeStatusMessage))
        {
        }

        public string YouTubeTokenInfo => this.MainWindowViewModel.YouTubeTokenInfo;

        public ICommand AuthorizeYouTubeOAuthCommand => this.MainWindowViewModel.AuthorizeYouTubeOAuthCommand;

        public ICommand ConnectYouTubeCommand => this.MainWindowViewModel.ConnectYouTubeCommand;

        public ICommand DisconnectYouTubeCommand => this.MainWindowViewModel.DisconnectYouTubeCommand;

        public bool ShowYouTubeChat
        {
            get => this.MainWindowViewModel.ShowYouTubeChat;
            set => this.MainWindowViewModel.ShowYouTubeChat = value;
        }

        public bool ShowYouTubeSuperChat
        {
            get => this.MainWindowViewModel.ShowYouTubeSuperChat;
            set => this.MainWindowViewModel.ShowYouTubeSuperChat = value;
        }

        public bool ShowYouTubeMembership
        {
            get => this.MainWindowViewModel.ShowYouTubeMembership;
            set => this.MainWindowViewModel.ShowYouTubeMembership = value;
        }

        public ICommand PreviewYouTubeCommentCommand => this.MainWindowViewModel.PreviewYouTubeCommentCommand;

        public bool ObsWebSocketEnabled
        {
            get => this.MainWindowViewModel.ObsWebSocketEnabled;
            set => this.MainWindowViewModel.ObsWebSocketEnabled = value;
        }

        public string ObsWebSocketHost
        {
            get => this.MainWindowViewModel.ObsWebSocketHost;
            set => this.MainWindowViewModel.ObsWebSocketHost = value;
        }

        public int ObsWebSocketPort
        {
            get => this.MainWindowViewModel.ObsWebSocketPort;
            set => this.MainWindowViewModel.ObsWebSocketPort = value;
        }

        public string ObsWebSocketPassword
        {
            get => this.MainWindowViewModel.ObsWebSocketPassword;
            set => this.MainWindowViewModel.ObsWebSocketPassword = value;
        }

        public string YouTubeStatusMessage => this.MainWindowViewModel.YouTubeStatusMessage;
    }
}