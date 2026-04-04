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

        public bool HasToken => MainWindowViewModel.HasToken;

        public string TokenInfo => MainWindowViewModel.TokenInfo;

        public ICommand AuthorizeOAuthCommand => MainWindowViewModel.AuthorizeOAuthCommand;

        public bool IsAuthorizingOAuth => MainWindowViewModel.IsAuthorizingOAuth;

        public string DeviceUserCode => MainWindowViewModel.DeviceUserCode;

        public string ChannelName
        {
            get => MainWindowViewModel.ChannelName;
            set => MainWindowViewModel.ChannelName = value;
        }

        public ObservableCollection<string> RecentChannels => MainWindowViewModel.RecentChannels;

        public bool HasRecentChannels => MainWindowViewModel.HasRecentChannels;

        public ICommand SelectRecentChannelCommand => MainWindowViewModel.SelectRecentChannelCommand;

        public ICommand ConnectCommand => MainWindowViewModel.ConnectCommand;

        public ICommand DisconnectCommand => MainWindowViewModel.DisconnectCommand;

        public bool ShowReward
        {
            get => MainWindowViewModel.ShowReward;
            set => MainWindowViewModel.ShowReward = value;
        }

        public bool ShowRaid
        {
            get => MainWindowViewModel.ShowRaid;
            set => MainWindowViewModel.ShowRaid = value;
        }

        public bool ShowFollow
        {
            get => MainWindowViewModel.ShowFollow;
            set => MainWindowViewModel.ShowFollow = value;
        }

        public bool ShowSubscribe
        {
            get => MainWindowViewModel.ShowSubscribe;
            set => MainWindowViewModel.ShowSubscribe = value;
        }

        public bool ShowGiftSubscribe
        {
            get => MainWindowViewModel.ShowGiftSubscribe;
            set => MainWindowViewModel.ShowGiftSubscribe = value;
        }

        public bool ShowResub
        {
            get => MainWindowViewModel.ShowResub;
            set => MainWindowViewModel.ShowResub = value;
        }

        public bool ShowHypeTrainBegin
        {
            get => MainWindowViewModel.ShowHypeTrainBegin;
            set => MainWindowViewModel.ShowHypeTrainBegin = value;
        }

        public bool ShowHypeTrainEnd
        {
            get => MainWindowViewModel.ShowHypeTrainEnd;
            set => MainWindowViewModel.ShowHypeTrainEnd = value;
        }

        public ICommand PreviewTwitchCommentCommand => MainWindowViewModel.PreviewTwitchCommentCommand;
    }
}