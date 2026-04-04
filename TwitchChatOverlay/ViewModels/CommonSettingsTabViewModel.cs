using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Input;
using TwitchChatOverlay.Services;

namespace TwitchChatOverlay.ViewModels
{
    public class CommonSettingsTabViewModel : TabViewModelBase
    {
        public CommonSettingsTabViewModel(MainWindowViewModel mainWindowViewModel)
            : base(
                mainWindowViewModel,
                nameof(ToastDurationSeconds),
                nameof(ToastMaxCount),
                nameof(ToastPositionIndex),
                nameof(MonitorList),
                nameof(ToastMonitorIndex),
                nameof(ToastFontSize),
                nameof(ToastWidth),
                nameof(ToastBackgroundOpacityPercent),
                nameof(ToastFontFamily),
                nameof(FontFamilyPresets),
                nameof(ToastBackgroundModeIndex),
                nameof(IsCustomBackgroundColor),
                nameof(ToastCustomBackgroundColor),
                nameof(ToastFontColorModeIndex),
                nameof(IsCustomFontColor),
                nameof(ToastCustomFontColor),
                nameof(NotificationSoundEnabled),
                nameof(NotificationSoundSourceModeIndex),
                nameof(IsCustomNotificationSoundFile),
                nameof(NotificationSoundFilePath),
                nameof(NotificationSoundVolumePercent),
                nameof(AudioOutputDevices),
                nameof(NotificationSoundOutputDeviceId))
        {
        }

        public int ToastDurationSeconds
        {
            get => this.MainWindowViewModel.ToastDurationSeconds;
            set => this.MainWindowViewModel.ToastDurationSeconds = value;
        }

        public int ToastMaxCount
        {
            get => this.MainWindowViewModel.ToastMaxCount;
            set => this.MainWindowViewModel.ToastMaxCount = value;
        }

        public int ToastPositionIndex
        {
            get => this.MainWindowViewModel.ToastPositionIndex;
            set => this.MainWindowViewModel.ToastPositionIndex = value;
        }

        public ObservableCollection<string> MonitorList => this.MainWindowViewModel.MonitorList;

        public int ToastMonitorIndex
        {
            get => this.MainWindowViewModel.ToastMonitorIndex;
            set => this.MainWindowViewModel.ToastMonitorIndex = value;
        }

        public double ToastFontSize
        {
            get => this.MainWindowViewModel.ToastFontSize;
            set => this.MainWindowViewModel.ToastFontSize = value;
        }

        public double ToastWidth
        {
            get => this.MainWindowViewModel.ToastWidth;
            set => this.MainWindowViewModel.ToastWidth = value;
        }

        public int ToastBackgroundOpacityPercent
        {
            get => this.MainWindowViewModel.ToastBackgroundOpacityPercent;
            set => this.MainWindowViewModel.ToastBackgroundOpacityPercent = value;
        }

        public string ToastFontFamily
        {
            get => this.MainWindowViewModel.ToastFontFamily;
            set => this.MainWindowViewModel.ToastFontFamily = value;
        }

        public List<string> FontFamilyPresets => this.MainWindowViewModel.FontFamilyPresets;

        public int ToastBackgroundModeIndex
        {
            get => this.MainWindowViewModel.ToastBackgroundModeIndex;
            set => this.MainWindowViewModel.ToastBackgroundModeIndex = value;
        }

        public bool IsCustomBackgroundColor => this.MainWindowViewModel.IsCustomBackgroundColor;

        public string ToastCustomBackgroundColor
        {
            get => this.MainWindowViewModel.ToastCustomBackgroundColor;
            set => this.MainWindowViewModel.ToastCustomBackgroundColor = value;
        }

        public int ToastFontColorModeIndex
        {
            get => this.MainWindowViewModel.ToastFontColorModeIndex;
            set => this.MainWindowViewModel.ToastFontColorModeIndex = value;
        }

        public bool IsCustomFontColor => this.MainWindowViewModel.IsCustomFontColor;

        public string ToastCustomFontColor
        {
            get => this.MainWindowViewModel.ToastCustomFontColor;
            set => this.MainWindowViewModel.ToastCustomFontColor = value;
        }

        public bool NotificationSoundEnabled
        {
            get => this.MainWindowViewModel.NotificationSoundEnabled;
            set => this.MainWindowViewModel.NotificationSoundEnabled = value;
        }

        public int NotificationSoundSourceModeIndex
        {
            get => this.MainWindowViewModel.NotificationSoundSourceModeIndex;
            set => this.MainWindowViewModel.NotificationSoundSourceModeIndex = value;
        }

        public bool IsCustomNotificationSoundFile => this.MainWindowViewModel.IsCustomNotificationSoundFile;

        public string NotificationSoundFilePath
        {
            get => this.MainWindowViewModel.NotificationSoundFilePath;
            set => this.MainWindowViewModel.NotificationSoundFilePath = value;
        }

        public ICommand BrowseNotificationSoundFileCommand => this.MainWindowViewModel.BrowseNotificationSoundFileCommand;

        public int NotificationSoundVolumePercent
        {
            get => this.MainWindowViewModel.NotificationSoundVolumePercent;
            set => this.MainWindowViewModel.NotificationSoundVolumePercent = value;
        }

        public ObservableCollection<AudioOutputDeviceOption> AudioOutputDevices => this.MainWindowViewModel.AudioOutputDevices;

        public string NotificationSoundOutputDeviceId
        {
            get => this.MainWindowViewModel.NotificationSoundOutputDeviceId;
            set => this.MainWindowViewModel.NotificationSoundOutputDeviceId = value;
        }

        public ICommand PreviewNotificationSoundCommand => this.MainWindowViewModel.PreviewNotificationSoundCommand;

        public ICommand PreviewCommonCommentCommand => this.MainWindowViewModel.PreviewCommonCommentCommand;
    }
}