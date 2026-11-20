using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using TwitchChatOverlay.Models;
using TwitchChatOverlay.Services;

namespace TwitchChatOverlay.Tests;

public sealed class StreamerBotServiceTests
{
    [Fact]
    public async Task ConnectAsync_WithPassword_AuthenticatesAndSubscribesToAllPlatforms()
    {
        await using var server = FakeStreamerBotServer.Create("secret");
        await server.StartAsync();
        var service = new StreamerBotService();

        try
        {
            var result = await service.ConnectAsync("127.0.0.1", server.Port, "secret");

            Assert.True(result.IsConnected, result.Message);
            Assert.True(service.IsConnected);
            Assert.True(server.AuthenticateReceived);
            Assert.True(server.Authenticated);
            Assert.Equal("1.0.4", service.ServerVersion);

            var subscriptions = server.ReceivedSubscriptions;
            Assert.NotNull(subscriptions);
            var twitch = subscriptions["Twitch"]!.AsArray().Select(x => x!.GetValue<string>()).ToArray();
            var youtube = subscriptions["YouTube"]!.AsArray().Select(x => x!.GetValue<string>()).ToArray();
            var kick = subscriptions["Kick"]!.AsArray().Select(x => x!.GetValue<string>()).ToArray();

            Assert.Contains("ChatMessage", twitch);
            Assert.Contains("Follow", twitch);
            Assert.Contains("Sub", twitch);
            Assert.Contains("ReSub", twitch);
            Assert.Contains("GiftSub", twitch);
            Assert.Contains("GiftBomb", twitch);
            Assert.Contains("Raid", twitch);
            Assert.Contains("RewardRedemption", twitch);
            Assert.Contains("HypeTrainStart", twitch);
            Assert.Contains("HypeTrainEnd", twitch);

            Assert.Contains("Message", youtube);
            Assert.Contains("SuperChat", youtube);
            Assert.Contains("NewSponsor", youtube);

            Assert.Contains("ChatMessage", kick);
            Assert.Contains("Follow", kick);
            Assert.Contains("Subscription", kick);
        }
        finally
        {
            service.Disconnect();
        }
    }

    [Fact]
    public async Task ConnectAsync_WithoutPassword_SkipsAuthentication()
    {
        await using var server = FakeStreamerBotServer.Create();
        await server.StartAsync();
        var service = new StreamerBotService();

        try
        {
            var result = await service.ConnectAsync("127.0.0.1", server.Port, string.Empty);

            Assert.True(result.IsConnected, result.Message);
            Assert.False(server.AuthenticateReceived);
            Assert.NotNull(server.ReceivedSubscriptions);
        }
        finally
        {
            service.Disconnect();
        }
    }

    [Fact]
    public async Task ConnectAsync_WrongPassword_ReturnsAuthenticationFailure()
    {
        await using var server = FakeStreamerBotServer.Create("secret");
        await server.StartAsync();
        var service = new StreamerBotService();

        var result = await service.ConnectAsync("127.0.0.1", server.Port, "wrong-password");

        Assert.False(result.IsConnected);
        Assert.Equal(StreamerBotConnectionFailureReason.AuthenticationFailed, result.FailureReason);
        Assert.False(service.IsConnected);
    }

    [Fact]
    public async Task ConnectAsync_MissingPassword_WithAuthenticationEnforced_ReturnsAuthenticationFailure()
    {
        await using var server = FakeStreamerBotServer.Create("secret");
        server.EnforceAuthentication = true;
        await server.StartAsync();
        var service = new StreamerBotService();

        var result = await service.ConnectAsync("127.0.0.1", server.Port, string.Empty);

        Assert.False(result.IsConnected);
        Assert.Equal(StreamerBotConnectionFailureReason.AuthenticationFailed, result.FailureReason);
    }

    [Fact]
    public async Task ConnectAsync_MissingPassword_WhenAuthenticationNotEnforced_ConnectsWithoutAuthenticating()
    {
        // Streamer.bot の既定は「SendMessage のみ認証必須」。未認証でも購読できる。
        await using var server = FakeStreamerBotServer.Create("secret");
        await server.StartAsync();
        var service = new StreamerBotService();

        try
        {
            var result = await service.ConnectAsync("127.0.0.1", server.Port, string.Empty);

            Assert.True(result.IsConnected, result.Message);
            Assert.False(server.AuthenticateReceived);
            Assert.NotNull(server.ReceivedSubscriptions);
        }
        finally
        {
            service.Disconnect();
        }
    }

    [Fact]
    public async Task ConnectAsync_SubscribeFailure_RetriesWithoutKick()
    {
        await using var server = FakeStreamerBotServer.Create();
        server.SubscribeResponseFactory = message =>
        {
            var requestedKick = message["events"]?["Kick"] != null;
            return requestedKick
                ? new JsonObject
                {
                    ["id"] = message["id"]?.GetValue<string>(),
                    ["status"] = "error",
                    ["error"] = "Kick is not connected.",
                }
                : new JsonObject
                {
                    ["id"] = message["id"]?.GetValue<string>(),
                    ["status"] = "ok",
                };
        };
        await server.StartAsync();
        var service = new StreamerBotService();

        try
        {
            var result = await service.ConnectAsync("127.0.0.1", server.Port, string.Empty);

            Assert.True(result.IsConnected, result.Message);
            Assert.Equal(2, server.SubscribeCount);
            var subscriptions = server.ReceivedSubscriptions;
            Assert.NotNull(subscriptions);
            Assert.Null(subscriptions["Kick"]);
            Assert.NotNull(subscriptions["Twitch"]);
            Assert.NotNull(subscriptions["YouTube"]);
        }
        finally
        {
            service.Disconnect();
        }
    }

    [Fact]
    public async Task ConnectAsync_UnreachableServer_ReturnsRetryableFailure()
    {
        var port = GetUnusedPort();
        var service = new StreamerBotService();

        var result = await service.ConnectAsync("127.0.0.1", port, string.Empty);

        Assert.False(result.IsConnected);
        Assert.Equal(StreamerBotConnectionFailureReason.Unavailable, result.FailureReason);
        Assert.True(result.ShouldRetry);
    }

    [Fact]
    public async Task ConnectAsync_InvalidHost_ReturnsConfigurationError()
    {
        var service = new StreamerBotService();

        var result = await service.ConnectAsync("http://example.com/path", 8080, string.Empty);

        Assert.False(result.IsConnected);
        Assert.Equal(StreamerBotConnectionFailureReason.ConfigurationError, result.FailureReason);
    }

    [Fact]
    public async Task Events_AreMappedToNotifications()
    {
        await using var server = FakeStreamerBotServer.Create();
        await server.StartAsync();
        var service = new StreamerBotService();
        var notifications = new ConcurrentQueue<OverlayNotification>();
        service.NotificationReceived += (_, notification) => notifications.Enqueue(notification);

        try
        {
            var result = await service.ConnectAsync("127.0.0.1", server.Port, string.Empty);
            Assert.True(result.IsConnected, result.Message);

            // Twitch チャット
            await server.SendEventAsync("Twitch", "ChatMessage", new JsonObject
            {
                ["message"] = new JsonObject
                {
                    ["msgId"] = "m1",
                    ["userId"] = "u1",
                    ["username"] = "alice",
                    ["displayName"] = "Alice",
                    ["color"] = "#FF00FF",
                    ["message"] = "こんにちは",
                },
            });

            var chat = await WaitForNotificationAsync(notifications);
            Assert.Equal("Twitch", chat.SourcePlatform);
            Assert.Equal(NotificationType.Chat, chat.Type);
            Assert.Equal("Alice", chat.Username);
            Assert.Equal("こんにちは", chat.DisplayText);
            Assert.Equal("#FF00FF", chat.UserColor);
            Assert.True(chat.ViaStreamerBot);

            // Twitch フォロー（公式型はトップレベル snake_case）
            await server.SendEventAsync("Twitch", "Follow", new JsonObject
            {
                ["user_id"] = "u2",
                ["user_login"] = "bob",
                ["user_name"] = "Bob",
                ["followed_at"] = "2026-01-01T00:00:00Z",
            });

            var follow = await WaitForNotificationAsync(notifications);
            Assert.Equal(NotificationType.Follow, follow.Type);
            Assert.Equal("Bob", follow.Username);

            // Twitch サブスク（message は文字列、subTier はトップレベル）
            await server.SendEventAsync("Twitch", "Sub", new JsonObject
            {
                ["displayName"] = "Carol",
                ["subTier"] = "1000",
                ["message"] = "初サブスクです",
            });

            var sub = await WaitForNotificationAsync(notifications);
            Assert.Equal(NotificationType.Subscribe, sub.Type);
            Assert.Equal("Carol", sub.Username);
            Assert.Equal("初サブスクです", sub.DisplayText);
            Assert.Equal("Tier 1", sub.SubText);

            // Twitch レイド
            await server.SendEventAsync("Twitch", "Raid", new JsonObject
            {
                ["from_broadcaster_user_name"] = "dave",
                ["viewers"] = 42,
            });

            var raid = await WaitForNotificationAsync(notifications);
            Assert.Equal(NotificationType.Raid, raid.Type);
            Assert.Equal("dave", raid.Username);
            Assert.Contains("42", raid.DisplayText, StringComparison.Ordinal);

            // YouTube メッセージ（message は文字列、ユーザーは user.name）
            await server.SendEventAsync("YouTube", "Message", new JsonObject
            {
                ["message"] = "great stream",
                ["eventId"] = "yt-1",
                ["user"] = new JsonObject
                {
                    ["id"] = "UC123",
                    ["name"] = "Erin",
                },
            });

            var youTubeMessage = await WaitForNotificationAsync(notifications);
            Assert.Equal("YouTube", youTubeMessage.SourcePlatform);
            Assert.Equal(NotificationType.Chat, youTubeMessage.Type);
            Assert.Equal("Erin", youTubeMessage.Username);
            Assert.Equal("great stream", youTubeMessage.DisplayText);

            // YouTube SuperChat（公式 payload は未定義。amount オブジェクト形式を許容する）
            await server.SendEventAsync("YouTube", "SuperChat", new JsonObject
            {
                ["message"] = "super chat text",
                ["user"] = new JsonObject { ["name"] = "Frank" },
                ["amount"] = new JsonObject { ["string"] = "¥500" },
            });

            var superChat = await WaitForNotificationAsync(notifications);
            Assert.Equal("YouTube", superChat.SourcePlatform);
            Assert.Equal(NotificationType.Reward, superChat.Type);
            Assert.Equal("Frank", superChat.Username);
            Assert.Equal("super chat text", superChat.DisplayText);
            Assert.Equal("¥500", superChat.SubText);

            // Kick チャット（message は文字列、ユーザーは user.name）
            await server.SendEventAsync("Kick", "ChatMessage", new JsonObject
            {
                ["message"] = "kick hello",
                ["user"] = new JsonObject { ["username"] = "gina" },
                ["color"] = "#00FF00",
            });

            var kick = await WaitForNotificationAsync(notifications);
            Assert.Equal("Kick", kick.SourcePlatform);
            Assert.Equal(NotificationType.Chat, kick.Type);
            Assert.Equal("gina", kick.Username);
            Assert.Equal("kick hello", kick.DisplayText);
            Assert.Equal("#00FF00", kick.UserColor);

            // 未対応イベントは通知しない
            await server.SendEventAsync("Twitch", "UnknownEvent", new JsonObject());
            await Task.Delay(200);
            Assert.False(notifications.TryDequeue(out _));
        }
        finally
        {
            service.Disconnect();
        }
    }

    [Fact]
    public async Task ServerPing_IsAnsweredWithPong()
    {
        await using var server = FakeStreamerBotServer.Create();
        await server.StartAsync();
        var service = new StreamerBotService();

        try
        {
            var result = await service.ConnectAsync("127.0.0.1", server.Port, string.Empty);
            Assert.True(result.IsConnected, result.Message);

            await server.SendPingAsync();
            await WaitUntilAsync(() => server.PongReceived);

            Assert.True(service.IsConnected);
        }
        finally
        {
            service.Disconnect();
        }
    }

    [Fact]
    public async Task Disconnect_UpdatesConnectionStateAndRaisesEvent()
    {
        await using var server = FakeStreamerBotServer.Create();
        await server.StartAsync();
        var service = new StreamerBotService();
        var stateChanges = 0;
        service.ConnectionStateChanged += (_, _) => Interlocked.Increment(ref stateChanges);

        var result = await service.ConnectAsync("127.0.0.1", server.Port, string.Empty);
        Assert.True(result.IsConnected, result.Message);

        service.Disconnect();

        Assert.False(service.IsConnected);
        Assert.True(stateChanges >= 2);
    }

    private static async Task<OverlayNotification> WaitForNotificationAsync(ConcurrentQueue<OverlayNotification> notifications)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (notifications.TryDequeue(out var notification))
            {
                return notification;
            }

            await Task.Delay(20);
        }

        throw new TimeoutException("Streamer.bot からの通知が届きませんでした。");
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(20);
        }

        throw new TimeoutException("条件が満たされませんでした。");
    }

    private static int GetUnusedPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
