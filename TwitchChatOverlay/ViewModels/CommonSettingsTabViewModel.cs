using System.Collections.ObjectModel;
using System.Collections.Generic;
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
            get => MainWindowViewModel.ToastDurationSeconds;
            set => MainWindowViewModel.ToastDurationSeconds = value;
        }

        public int ToastMaxCount
        {
            get => MainWindowViewModel.ToastMaxCount;
            set => MainWindowViewModel.ToastMaxCount = value;
        }

        public int ToastPositionIndex
        {
            get => MainWindowViewModel.ToastPositionIndex;
            set => MainWindowViewModel.ToastPositionIndex = value;
        }

        public ObservableCollection<string> MonitorList => MainWindowViewModel.MonitorList;

        public int ToastMonitorIndex
        {
            get => MainWindowViewModel.ToastMonitorIndex;
            set => MainWindowViewModel.ToastMonitorIndex = value;
        }

        public double ToastFontSize
        {
            get => MainWindowViewModel.ToastFontSize;
            set => MainWindowViewModel.ToastFontSize = value;
        }

        public double ToastWidth
        {
            get => MainWindowViewModel.ToastWidth;
            set => MainWindowViewModel.ToastWidth = value;
        }

        public int ToastBackgroundOpacityPercent
        {
            get => MainWindowViewModel.ToastBackgroundOpacityPercent;
            set => MainWindowViewModel.ToastBackgroundOpacityPercent = value;
        }

        public string ToastFontFamily
        {
            get => MainWindowViewModel.ToastFontFamily;
            set => MainWindowViewModel.ToastFontFamily = value;
        }

        public List<string> FontFamilyPresets => MainWindowViewModel.FontFamilyPresets;

        public int ToastBackgroundModeIndex
        {
            get => MainWindowViewModel.ToastBackgroundModeIndex;
            set => MainWindowViewModel.ToastBackgroundModeIndex = value;
        }

        public bool IsCustomBackgroundColor => MainWindowViewModel.IsCustomBackgroundColor;

        public string ToastCustomBackgroundColor
        {
            get => MainWindowViewModel.ToastCustomBackgroundColor;
            set => MainWindowViewModel.ToastCustomBackgroundColor = value;
        }

        public int ToastFontColorModeIndex
        {
            get => MainWindowViewModel.ToastFontColorModeIndex;
            set => MainWindowViewModel.ToastFontColorModeIndex = value;
        }

        public bool IsCustomFontColor => MainWindowViewModel.IsCustomFontColor;

        public string ToastCustomFontColor
        {
            get => MainWindowViewModel.ToastCustomFontColor;
            set => MainWindowViewModel.ToastCustomFontColor = value;
        }

        public bool NotificationSoundEnabled
        {
            get => MainWindowViewModel.NotificationSoundEnabled;
            set => MainWindowViewModel.NotificationSoundEnabled = value;
        }

        public int NotificationSoundSourceModeIndex
        {
            get => MainWindowViewModel.NotificationSoundSourceModeIndex;
            set => MainWindowViewModel.NotificationSoundSourceModeIndex = value;
        }

        public bool IsCustomNotificationSoundFile => MainWindowViewModel.IsCustomNotificationSoundFile;

        public string NotificationSoundFilePath
        {
            get => MainWindowViewModel.NotificationSoundFilePath;
            set => MainWindowViewModel.NotificationSoundFilePath = value;
        }

        public ICommand BrowseNotificationSoundFileCommand => MainWindowViewModel.BrowseNotificationSoundFileCommand;

        public int NotificationSoundVolumePercent
        {
            get => MainWindowViewModel.NotificationSoundVolumePercent;
            set => MainWindowViewModel.NotificationSoundVolumePercent = value;
        }

        public ObservableCollection<AudioOutputDeviceOption> AudioOutputDevices => MainWindowViewModel.AudioOutputDevices;

        public string NotificationSoundOutputDeviceId
        {
            get => MainWindowViewModel.NotificationSoundOutputDeviceId;
            set => MainWindowViewModel.NotificationSoundOutputDeviceId = value;
        }

        public ICommand PreviewNotificationSoundCommand => MainWindowViewModel.PreviewNotificationSoundCommand;

        public ICommand PreviewCommonCommentCommand => MainWindowViewModel.PreviewCommonCommentCommand;
    }
}