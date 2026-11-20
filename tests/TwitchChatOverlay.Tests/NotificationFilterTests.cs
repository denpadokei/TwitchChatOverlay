using TwitchChatOverlay.Models;
using TwitchChatOverlay.Services;

namespace TwitchChatOverlay.Tests;

public sealed class NotificationFilterTests
{
    [Fact]
    public void DirectNotifications_AreNotAffectedByStreamerBotFilters()
    {
        var settings = new AppSettings
        {
            // Streamer.bot 側のフィルターをすべて無効にしても、直接接続の通知は表示される
            ShowStreamerBotTwitchChat = false,
            ShowStreamerBotTwitchNotifications = false,
            ShowStreamerBotYouTube = false,
            ShowStreamerBotKick = false,
        };

        Assert.True(ToastNotificationService.ShouldShow(
            new OverlayNotification { SourcePlatform = "Twitch", Type = NotificationType.Chat },
            settings));

        Assert.True(ToastNotificationService.ShouldShow(
            new OverlayNotification { SourcePlatform = "Twitch", Type = NotificationType.Follow },
            settings));

        Assert.True(ToastNotificationService.ShouldShow(
            new OverlayNotification { SourcePlatform = "YouTube", Type = NotificationType.Chat },
            settings));
    }

    [Fact]
    public void StreamerBotNotifications_UseOnlyStreamerBotFilters()
    {
        var settings = new AppSettings
        {
            // 直接接続側のフィルターをすべて無効にしても、Streamer.bot 側の設定で表示される
            ShowFollow = false,
            ShowYouTubeChat = false,
            ShowYouTubeSuperChat = false,
            ShowStreamerBotTwitchChat = true,
            ShowStreamerBotTwitchNotifications = true,
            ShowStreamerBotYouTube = true,
            ShowStreamerBotKick = true,
        };

        Assert.True(ToastNotificationService.ShouldShow(
            new OverlayNotification { SourcePlatform = "Twitch", Type = NotificationType.Chat, ViaStreamerBot = true },
            settings));

        Assert.True(ToastNotificationService.ShouldShow(
            new OverlayNotification { SourcePlatform = "YouTube", Type = NotificationType.Chat, ViaStreamerBot = true },
            settings));

        Assert.True(ToastNotificationService.ShouldShow(
            new OverlayNotification { SourcePlatform = "Kick", Type = NotificationType.Chat, ViaStreamerBot = true },
            settings));
    }

    [Fact]
    public void StreamerBotChatAndNotifications_AreFilteredIndependently()
    {
        var settings = new AppSettings
        {
            ShowStreamerBotTwitchChat = false,
            ShowStreamerBotTwitchNotifications = true,
        };

        Assert.False(ToastNotificationService.ShouldShow(
            new OverlayNotification { SourcePlatform = "Twitch", Type = NotificationType.Chat, ViaStreamerBot = true },
            settings));

        Assert.True(ToastNotificationService.ShouldShow(
            new OverlayNotification { SourcePlatform = "Twitch", Type = NotificationType.Follow, ViaStreamerBot = true },
            settings));

        settings.ShowStreamerBotTwitchNotifications = false;

        Assert.False(ToastNotificationService.ShouldShow(
            new OverlayNotification { SourcePlatform = "Twitch", Type = NotificationType.Follow, ViaStreamerBot = true },
            settings));
    }

    [Fact]
    public void StreamerBotKick_UsesKickFilter()
    {
        var settings = new AppSettings { ShowStreamerBotKick = false };

        Assert.False(ToastNotificationService.ShouldShow(
            new OverlayNotification { SourcePlatform = "Kick", Type = NotificationType.Subscribe, ViaStreamerBot = true },
            settings));

        settings.ShowStreamerBotKick = true;

        Assert.True(ToastNotificationService.ShouldShow(
            new OverlayNotification { SourcePlatform = "Kick", Type = NotificationType.Subscribe, ViaStreamerBot = true },
            settings));
    }

    [Fact]
    public void StreamerBotTwitchNotifications_StillRespectPerTypeToggles()
    {
        var settings = new AppSettings
        {
            ShowStreamerBotTwitchNotifications = true,
            ShowFollow = false,
        };

        Assert.False(ToastNotificationService.ShouldShow(
            new OverlayNotification { SourcePlatform = "Twitch", Type = NotificationType.Follow, ViaStreamerBot = true },
            settings));
    }
}
