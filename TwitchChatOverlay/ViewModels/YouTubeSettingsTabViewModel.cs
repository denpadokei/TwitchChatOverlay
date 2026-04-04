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

        public string YouTubeTokenInfo => MainWindowViewModel.YouTubeTokenInfo;

        public ICommand AuthorizeYouTubeOAuthCommand => MainWindowViewModel.AuthorizeYouTubeOAuthCommand;

        public ICommand ConnectYouTubeCommand => MainWindowViewModel.ConnectYouTubeCommand;

        public ICommand DisconnectYouTubeCommand => MainWindowViewModel.DisconnectYouTubeCommand;

        public bool ShowYouTubeChat
        {
            get => MainWindowViewModel.ShowYouTubeChat;
            set => MainWindowViewModel.ShowYouTubeChat = value;
        }

        public bool ShowYouTubeSuperChat
        {
            get => MainWindowViewModel.ShowYouTubeSuperChat;
            set => MainWindowViewModel.ShowYouTubeSuperChat = value;
        }

        public bool ShowYouTubeMembership
        {
            get => MainWindowViewModel.ShowYouTubeMembership;
            set => MainWindowViewModel.ShowYouTubeMembership = value;
        }

        public ICommand PreviewYouTubeCommentCommand => MainWindowViewModel.PreviewYouTubeCommentCommand;

        public bool ObsWebSocketEnabled
        {
            get => MainWindowViewModel.ObsWebSocketEnabled;
            set => MainWindowViewModel.ObsWebSocketEnabled = value;
        }

        public string ObsWebSocketHost
        {
            get => MainWindowViewModel.ObsWebSocketHost;
            set => MainWindowViewModel.ObsWebSocketHost = value;
        }

        public int ObsWebSocketPort
        {
            get => MainWindowViewModel.ObsWebSocketPort;
            set => MainWindowViewModel.ObsWebSocketPort = value;
        }

        public string ObsWebSocketPassword
        {
            get => MainWindowViewModel.ObsWebSocketPassword;
            set => MainWindowViewModel.ObsWebSocketPassword = value;
        }

        public string YouTubeStatusMessage => MainWindowViewModel.YouTubeStatusMessage;
    }
}