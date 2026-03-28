using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using TwitchChatOverlay.Models;

namespace TwitchChatOverlay.Services
{
    public class TwitchEventSubService
    {
        private readonly TwitchApiService _apiService;
        private ClientWebSocket _webSocket;
        private CancellationTokenSource _cts;
        private string _accessToken;
        private string _clientId;
        private string _broadcasterUserId;
        private string _userId;

        private const string EventSubWssUrl = "wss://eventsub.wss.twitch.tv/ws";

        public event EventHandler<OverlayNotification> NotificationReceived;
        public bool IsConnected { get; private set; }

        public TwitchEventSubService(TwitchApiService apiService)
        {
            _apiService = apiService;
        }

        public async Task ConnectAsync(string accessToken, string clientId, string broadcasterUserId, string userId)
        {
            _accessToken = accessToken;
            _clientId = clientId;
            _broadcasterUserId = broadcasterUserId;
            _userId = userId;

            _cts = new CancellationTokenSource();
            await ConnectWebSocketAsync(EventSubWssUrl);
        }

        public void Disconnect()
        {
            _cts?.Cancel();
            IsConnected = false;
        }

        private async Task ConnectWebSocketAsync(string url)
        {
            _webSocket = new ClientWebSocket();
            await _webSocket.ConnectAsync(new Uri(url), _cts.Token);
            IsConnected = true;
            _ = ReceiveLoopAsync();
        }

        private async Task ReceiveLoopAsync()
        {
            var buffer = new byte[65536];
            var messageBuilder = new StringBuilder();

            try
            {
                while (_webSocket.State == WebSocketState.Open && !_cts.IsCancellationRequested)
                {
                    messageBuilder.Clear();
                    WebSocketReceiveResult result;

                    do
                    {
                        result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);
                        messageBuilder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                    } while (!result.EndOfMessage);

                    if (result.MessageType == WebSocketMessageType.Close)
                        break;

                    await HandleMessageAsync(messageBuilder.ToString());
                }
            }
            catch (OperationCanceledException)
            {
                // 正常切断
            }
            catch (Exception)
            {
                // 接続が切れた場合は再接続を試みない (将来拡張)
            }
            finally
            {
                IsConnected = false;
            }
        }

        private async Task HandleMessageAsync(string raw)
        {
            try
            {
                var json = JObject.Parse(raw);
                var messageType = json["metadata"]?["message_type"]?.ToString();

                switch (messageType)
                {
                    case "session_welcome":
                        var sessionId = json["payload"]?["session"]?["id"]?.ToString();
                        if (sessionId != null)
                            await _apiService.CreateEventSubSubscriptionsAsync(
                                _accessToken, _clientId, sessionId, _broadcasterUserId, _userId);
                        break;

                    case "notification":
                        HandleNotification(json);
                        break;

                    case "session_reconnect":
                        var reconnectUrl = json["payload"]?["session"]?["reconnect_url"]?.ToString();
                        if (reconnectUrl != null)
                            await ConnectWebSocketAsync(reconnectUrl);
                        break;

                    case "session_keepalive":
                        break;
                }
            }
            catch
            {
                // 個々のメッセージのパースエラーは無視
            }
        }

        private void HandleNotification(JObject json)
        {
            var subscriptionType = json["payload"]?["subscription"]?["type"]?.ToString();
            var evt = json["payload"]?["event"];
            if (evt == null) return;

            OverlayNotification notification = subscriptionType switch
            {
                "channel.chat.message" => BuildChatNotification(evt),
                "channel.channel_points_custom_reward_redemption.add" => BuildRewardNotification(evt),
                "channel.raid" => BuildRaidNotification(evt),
                "channel.follow" => BuildFollowNotification(evt),
                "channel.subscribe" => BuildSubscribeNotification(evt),
                "channel.subscription.gift" => BuildGiftSubscribeNotification(evt),
                "channel.subscription.message" => BuildResubNotification(evt),
                "channel.hype_train.begin" => BuildHypeTrainBeginNotification(evt),
                "channel.hype_train.end" => BuildHypeTrainEndNotification(evt),
                _ => null
            };

            if (notification != null)
                NotificationReceived?.Invoke(this, notification);
        }

        private static OverlayNotification BuildChatNotification(JToken evt)
        {
            var fragments = new List<object>();
            if (evt["message"]?["fragments"] is JArray fragArray)
            {
                foreach (var frag in fragArray)
                {
                    var fragType = frag["type"]?.ToString();
                    var text = frag["text"]?.ToString() ?? "";
                    if (fragType == "emote")
                    {
                        var emoteId = frag["emote"]?["id"]?.ToString() ?? "";
                        var formats = frag["emote"]?["format"] as JArray;
                        bool isAnimated = formats?.Any(f => f.ToString() == "animated") ?? false;
                        EmoteFragment emote = isAnimated
                            ? new AnimatedEmoteFragment { Text = text, EmoteId = emoteId }
                            : new StaticEmoteFragment { Text = text, EmoteId = emoteId };
                        fragments.Add(emote);
                    }
                    else if (!string.IsNullOrEmpty(text))
                    {
                        fragments.Add(new TextFragment { Text = text });
                    }
                }
            }

            var msgText = evt["message"]?["text"]?.ToString() ?? "";
            if (fragments.Count == 0 && !string.IsNullOrEmpty(msgText))
                fragments.Add(new TextFragment { Text = msgText });

            return new OverlayNotification
            {
                Type = NotificationType.Chat,
                Username = evt["chatter_user_name"]?.ToString() ?? evt["chatter_user_login"]?.ToString() ?? "",
                DisplayText = msgText,
                Fragments = fragments,
                UserColor = evt["color"]?.ToString() ?? "#FFFFFF"
            };
        }

        private static OverlayNotification BuildRewardNotification(JToken evt)
        {
            return new OverlayNotification
            {
                Type = NotificationType.Reward,
                Username = evt["user_name"]?.ToString() ?? evt["user_login"]?.ToString() ?? "",
                DisplayText = evt["reward"]?["title"]?.ToString() ?? "リワード交換",
                SubText = evt["user_input"]?.ToString()
            };
        }

        private static OverlayNotification BuildRaidNotification(JToken evt)
        {
            var viewers = evt["viewers"]?.ToString() ?? "0";
            return new OverlayNotification
            {
                Type = NotificationType.Raid,
                Username = evt["from_broadcaster_user_name"]?.ToString() ?? "",
                DisplayText = "レイドが来ました！",
                SubText = $"{viewers}人の視聴者"
            };
        }

        private static OverlayNotification BuildFollowNotification(JToken evt)
        {
            return new OverlayNotification
            {
                Type = NotificationType.Follow,
                Username = evt["user_name"]?.ToString() ?? evt["user_login"]?.ToString() ?? "",
                DisplayText = "フォローしました！"
            };
        }

        private static OverlayNotification BuildSubscribeNotification(JToken evt)
        {
            var tier = evt["tier"]?.ToString() switch
            {
                "1000" => "Tier 1",
                "2000" => "Tier 2",
                "3000" => "Tier 3",
                _ => ""
            };
            return new OverlayNotification
            {
                Type = NotificationType.Subscribe,
                Username = evt["user_name"]?.ToString() ?? evt["user_login"]?.ToString() ?? "",
                DisplayText = "チャンネルをサブスクしました！",
                SubText = tier
            };
        }

        private static OverlayNotification BuildGiftSubscribeNotification(JToken evt)
        {
            var total = evt["total"]?.ToString() ?? "1";
            var isAnon = evt["is_anonymous"]?.Value<bool>() == true;
            return new OverlayNotification
            {
                Type = NotificationType.GiftSubscribe,
                Username = isAnon ? "匿名" : (evt["user_name"]?.ToString() ?? evt["user_login"]?.ToString() ?? ""),
                DisplayText = $"{total}件のギフトサブスクを贈りました！"
            };
        }

        private static OverlayNotification BuildResubNotification(JToken evt)
        {
            var months = evt["cumulative_months"]?.ToString() ?? "?";
            return new OverlayNotification
            {
                Type = NotificationType.Resub,
                Username = evt["user_name"]?.ToString() ?? evt["user_login"]?.ToString() ?? "",
                DisplayText = "リサブしました！",
                SubText = $"継続{months}ヶ月"
            };
        }

        private static OverlayNotification BuildHypeTrainBeginNotification(JToken evt)
        {
            var level = evt["level"]?.ToString() ?? "1";
            return new OverlayNotification
            {
                Type = NotificationType.HypeTrainBegin,
                Username = "",
                DisplayText = "ハイプトレイン開始！",
                SubText = $"Lv.{level}"
            };
        }

        private static OverlayNotification BuildHypeTrainEndNotification(JToken evt)
        {
            var level = evt["level"]?.ToString() ?? "1";
            return new OverlayNotification
            {
                Type = NotificationType.HypeTrainEnd,
                Username = "",
                DisplayText = "ハイプトレイン終了！",
                SubText = $"Lv.{level} 達成"
            };
        }
    }
}
