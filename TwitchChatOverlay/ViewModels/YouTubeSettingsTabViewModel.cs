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
                nameof(YouTubeMessageCacheSize),
                nameof(YouTubeLegalConsentAccepted),
                nameof(OpenPrivacyPolicyCommand),
                nameof(OpenTermsOfUseCommand),
                nameof(OpenYouTubeTermsCommand),
                nameof(OpenGooglePrivacyPolicyCommand),
                nameof(OpenGooglePermissionsCommand),
                nameof(OpenSupportCommand),
                nameof(ClearYouTubeAuthorizationCommand),
                nameof(RevokeYouTubeAuthorizationCommand),
                nameof(ObsWebSocketEnabled),
                nameof(ObsWebSocketHost),
                nameof(ObsWebSocketPort),
                nameof(ObsWebSocketPassword),
                nameof(ConnectObsCommand),
                nameof(DisconnectObsCommand),
                nameof(ObsStatusMessage),
                nameof(YouTubeStatusMessage))
        {
        }

        public string YouTubeTokenInfo => this.MainWindowViewModel.YouTubeTokenInfo;

        public ICommand AuthorizeYouTubeOAuthCommand => this.MainWindowViewModel.AuthorizeYouTubeOAuthCommand;

        public ICommand ConnectYouTubeCommand => this.MainWindowViewModel.ConnectYouTubeCommand;

        public ICommand DisconnectYouTubeCommand => this.MainWindowViewModel.DisconnectYouTubeCommand;

        public bool YouTubeLegalConsentAccepted
        {
            get => this.MainWindowViewModel.YouTubeLegalConsentAccepted;
            set => this.MainWindowViewModel.YouTubeLegalConsentAccepted = value;
        }

        public ICommand OpenPrivacyPolicyCommand => this.MainWindowViewModel.OpenPrivacyPolicyCommand;

        public ICommand OpenTermsOfUseCommand => this.MainWindowViewModel.OpenTermsOfUseCommand;

        public ICommand OpenYouTubeTermsCommand => this.MainWindowViewModel.OpenYouTubeTermsCommand;

        public ICommand OpenGooglePrivacyPolicyCommand => this.MainWindowViewModel.OpenGooglePrivacyPolicyCommand;

        public ICommand OpenGooglePermissionsCommand => this.MainWindowViewModel.OpenGooglePermissionsCommand;

        public ICommand OpenSupportCommand => this.MainWindowViewModel.OpenSupportCommand;

        public ICommand ClearYouTubeAuthorizationCommand => this.MainWindowViewModel.ClearYouTubeAuthorizationCommand;

        public ICommand RevokeYouTubeAuthorizationCommand => this.MainWindowViewModel.RevokeYouTubeAuthorizationCommand;

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

        public int YouTubeMessageCacheSize
        {
            get => this.MainWindowViewModel.YouTubeMessageCacheSize;
            set => this.MainWindowViewModel.YouTubeMessageCacheSize = value;
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

        public ICommand ConnectObsCommand => this.MainWindowViewModel.ConnectObsCommand;

        public ICommand DisconnectObsCommand => this.MainWindowViewModel.DisconnectObsCommand;

        public string ObsStatusMessage => this.MainWindowViewModel.ObsStatusMessage;

        public string YouTubeStatusMessage => this.MainWindowViewModel.YouTubeStatusMessage;
    }
}