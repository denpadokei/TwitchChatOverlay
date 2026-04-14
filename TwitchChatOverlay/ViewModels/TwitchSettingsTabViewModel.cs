using System.Collections.ObjectModel;
using System.Windows.Input;

namespace TwitchChatOverlay.ViewModels
{
    public class TwitchSettingsTabViewModel : TabViewModelBase
    {
        public TwitchSettingsTabViewModel(MainWindowViewModel mainWindowViewModel)
            : base(
                mainWindowViewModel,
                nameof(HasToken),
                nameof(TokenInfo),
                nameof(AuthorizeOAuthCommand),
                nameof(IsAuthorizingOAuth),
                nameof(DeviceUserCode),
                nameof(ChannelName),
                nameof(RecentChannels),
                nameof(HasRecentChannels),
                nameof(ConnectCommand),
                nameof(DisconnectCommand),
                nameof(ShowReward),
                nameof(ShowRaid),
                nameof(ShowFollow),
                nameof(ShowSubscribe),
                nameof(ShowGiftSubscribe),
                nameof(ShowResub),
                nameof(ShowHypeTrainBegin),
                nameof(ShowHypeTrainEnd))
        {
        }

        public bool HasToken => this.MainWindowViewModel.HasToken;

        public string TokenInfo => this.MainWindowViewModel.TokenInfo;

        public ICommand AuthorizeOAuthCommand => this.MainWindowViewModel.AuthorizeOAuthCommand;

        public bool IsAuthorizingOAuth => this.MainWindowViewModel.IsAuthorizingOAuth;

        public string DeviceUserCode => this.MainWindowViewModel.DeviceUserCode;

        public string ChannelName
        {
            get => this.MainWindowViewModel.ChannelName;
            set => this.MainWindowViewModel.ChannelName = value;
        }

        public ObservableCollection<string> RecentChannels => this.MainWindowViewModel.RecentChannels;

        public bool HasRecentChannels => this.MainWindowViewModel.HasRecentChannels;

        public ICommand SelectRecentChannelCommand => this.MainWindowViewModel.SelectRecentChannelCommand;

        public ICommand ConnectCommand => this.MainWindowViewModel.ConnectCommand;

        public ICommand DisconnectCommand => this.MainWindowViewModel.DisconnectCommand;

        public bool ShowReward
        {
            get => this.MainWindowViewModel.ShowReward;
            set => this.MainWindowViewModel.ShowReward = value;
        }

        public bool ShowRaid
        {
            get => this.MainWindowViewModel.ShowRaid;
            set => this.MainWindowViewModel.ShowRaid = value;
        }

        public bool ShowFollow
        {
            get => this.MainWindowViewModel.ShowFollow;
            set => this.MainWindowViewModel.ShowFollow = value;
        }

        public bool ShowSubscribe
        {
            get => this.MainWindowViewModel.ShowSubscribe;
            set => this.MainWindowViewModel.ShowSubscribe = value;
        }

        public bool ShowGiftSubscribe
        {
            get => this.MainWindowViewModel.ShowGiftSubscribe;
            set => this.MainWindowViewModel.ShowGiftSubscribe = value;
        }

        public bool ShowResub
        {
            get => this.MainWindowViewModel.ShowResub;
            set => this.MainWindowViewModel.ShowResub = value;
        }

        public bool ShowHypeTrainBegin
        {
            get => this.MainWindowViewModel.ShowHypeTrainBegin;
            set => this.MainWindowViewModel.ShowHypeTrainBegin = value;
        }

        public bool ShowHypeTrainEnd
        {
            get => this.MainWindowViewModel.ShowHypeTrainEnd;
            set => this.MainWindowViewModel.ShowHypeTrainEnd = value;
        }

        public ICommand PreviewTwitchCommentCommand => this.MainWindowViewModel.PreviewTwitchCommentCommand;
    }
}