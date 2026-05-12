using Newtonsoft.Json.Linq;
using System;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TwitchChatOverlay.Models;

namespace TwitchChatOverlay.Services
{
    public sealed class StreamerBotService
    {
        private ClientWebSocket _webSocket;
        private CancellationTokenSource _cts;
        private readonly SemaphoreSlim _connectLock = new(1, 1);

        public bool IsConnected { get; private set; }

        public event EventHandler<OverlayNotification> NotificationReceived;
        public event EventHandler ConnectionStateChanged;
        public event EventHandler ConnectionLost;

        public async Task<bool> ConnectAsync(string host, int port, string password, CancellationToken cancellationToken = default)
        {
            await this._connectLock.WaitAsync(cancellationToken);
            try
            {
                if (this.IsConnected)
                {
                    return true;
                }

                this.DisconnectCore();
                this._cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                this._webSocket = new ClientWebSocket();

                var targetHost = string.IsNullOrWhiteSpace(host) ? "127.0.0.1" : host;
                var targetPort = port > 0 ? port : 8080;
                await this._webSocket.ConnectAsync(new Uri($"ws://{targetHost}:{targetPort}/"), this._cts.Token);

                // Hello メッセージ受信
                var hello = await ReceiveJsonAsync(this._webSocket, this._cts.Token);
                var requestType = hello["request"]?.ToString();
                if (!string.Equals(requestType, "Hello", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Streamer.bot Hello メッセージを受信できませんでした。request={requestType}");
                }

                // 認証が必要な場合は認証リクエストを送信
                var authToken = hello["authentication"];
                if (authToken != null && authToken.Type != JTokenType.Null)
                {
                    var authentication = BuildAuthentication(password, authToken);
                    if (string.IsNullOrEmpty(authentication))
                    {
                        throw new InvalidOperationException("Streamer.bot 認証文字列の生成に失敗しました。");
                    }

                    await SendJsonAsync(this._webSocket, new JObject
                    {
                        ["request"] = "Authenticate",
                        ["id"] = "tco-auth-1",
                        ["authentication"] = authentication,
                    }, this._cts.Token);

                    var authResponse = await ReceiveJsonAsync(this._webSocket, this._cts.Token);
                    var status = authResponse["status"]?.ToString();
                    if (!string.Equals(status, "ok", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException($"Streamer.bot 認証に失敗しました。status={status}");
                    }
                }

                // イベントのサブスクライブ
                await SendJsonAsync(this._webSocket, BuildSubscribeRequest(), this._cts.Token);
                var subscribeResponse = await ReceiveJsonAsync(this._webSocket, this._cts.Token);
                LogService.Info($"Streamer.bot Subscribe レスポンス: {subscribeResponse}");

                this.SetConnectionState(true);
                _ = Task.Run(() => this.ReceiveLoopAsync(this._cts.Token));
                LogService.Info("Streamer.bot WebSocket 接続完了");
                return true;
            }
            catch (Exception ex)
            {
                LogService.Warning("Streamer.bot WebSocket 接続失敗", ex);
                this.DisconnectCore();
                throw;
            }
            finally
            {
                _ = this._connectLock.Release();
            }
        }

        public void Disconnect()
        {
            this.DisconnectCore();
        }

        private void DisconnectCore()
        {
            try
            {
                this._cts?.Cancel();
            }
            catch
            {
            }

            this._webSocket?.Dispose();
            this._webSocket = null;
            this._cts?.Dispose();
            this._cts = null;
            this.SetConnectionState(false);
        }

        private void SetConnectionState(bool isConnected)
        {
            if (this.IsConnected == isConnected)
            {
                return;
            }

            this.IsConnected = isConnected;
            ConnectionStateChanged?.Invoke(this, EventArgs.Empty);
        }

        private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
        {
            var buffer = new byte[65536];
            var messageBuilder = new StringBuilder();

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var socket = this._webSocket;
                    if (socket == null || socket.State != WebSocketState.Open)
                    {
                        break;
                    }

                    _ = messageBuilder.Clear();
                    WebSocketReceiveResult result;

                    do
                    {
                        result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                        _ = messageBuilder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                    } while (!result.EndOfMessage);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        break;
                    }

                    this.HandleMessage(messageBuilder.ToString());
                }
            }
            catch (OperationCanceledException)
            {
                LogService.Info("Streamer.bot WebSocket 受信ループ終了（キャンセル）");
            }
            catch (Exception ex)
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    LogService.Warning("Streamer.bot WebSocket 受信ループエラー", ex);
                    this.SetConnectionState(false);
                    ConnectionLost?.Invoke(this, EventArgs.Empty);
                }
                return;
            }
            finally
            {
                this.SetConnectionState(false);
            }

            if (!cancellationToken.IsCancellationRequested)
            {
                ConnectionLost?.Invoke(this, EventArgs.Empty);
            }
        }

        private void HandleMessage(string raw)
        {
            try
            {
                var json = JObject.Parse(raw);
                var eventObj = json["event"];
                if (eventObj == null)
                {
                    return;
                }

                var source = eventObj["source"]?.ToString() ?? "";
                var type = eventObj["type"]?.ToString() ?? "";
                var data = json["data"];

                var notification = MapToNotification(source, type, data);
                if (notification != null)
                {
                    NotificationReceived?.Invoke(this, notification);
                }
            }
            catch (Exception ex)
            {
                LogService.Warning($"Streamer.bot メッセージ処理エラー: {raw?[..Math.Min(200, raw.Length)]}", ex);
            }
        }

        private static OverlayNotification MapToNotification(string source, string type, JToken data)
        {
            return source.ToUpperInvariant() switch
            {
                "TWITCH" => MapTwitchEvent(type, data),
                "YOUTUBE" => MapYouTubeEvent(type, data),
                "KICK" => MapKickEvent(type, data),
                _ => null,
            };
        }

        private static OverlayNotification MapTwitchEvent(string type, JToken data)
        {
            return type switch
            {
                "ChatMessage" => new OverlayNotification
                {
                    SourcePlatform = "Twitch",
                    Type = NotificationType.Chat,
                    Username = data?["message"]?["displayName"]?.ToString() ?? data?["displayName"]?.ToString() ?? "(unknown)",
                    DisplayText = data?["message"]?["message"]?.ToString() ?? data?["message"]?.ToString() ?? "",
                    UserColor = data?["message"]?["color"]?.ToString() ?? "#FFFFFF",
                },
                "Follow" => new OverlayNotification
                {
                    SourcePlatform = "Twitch",
                    Type = NotificationType.Follow,
                    Username = data?["user"]?["display_name"]?.ToString() ?? data?["displayName"]?.ToString() ?? "(unknown)",
                    DisplayText = "フォローありがとうございます！",
                },
                "Sub" => new OverlayNotification
                {
                    SourcePlatform = "Twitch",
                    Type = NotificationType.Subscribe,
                    Username = data?["user"]?["display_name"]?.ToString() ?? data?["displayName"]?.ToString() ?? "(unknown)",
                    DisplayText = BuildSubText(data),
                    SubText = data?["sub"]?["planName"]?.ToString() ?? "",
                },
                "ReSub" => new OverlayNotification
                {
                    SourcePlatform = "Twitch",
                    Type = NotificationType.Resub,
                    Username = data?["user"]?["display_name"]?.ToString() ?? data?["displayName"]?.ToString() ?? "(unknown)",
                    DisplayText = data?["message"]?["message"]?.ToString() ?? data?["message"]?.ToString() ?? "",
                    SubText = $"累計 {data?["cumulative_months"]?.ToString() ?? "?"} ヶ月",
                },
                "GiftSub" => new OverlayNotification
                {
                    SourcePlatform = "Twitch",
                    Type = NotificationType.GiftSubscribe,
                    Username = data?["user"]?["display_name"]?.ToString() ?? data?["displayName"]?.ToString() ?? "(unknown)",
                    DisplayText = $"{data?["recipient_display_name"]?.ToString() ?? "?"} にギフトサブ！",
                },
                "GiftBomb" => new OverlayNotification
                {
                    SourcePlatform = "Twitch",
                    Type = NotificationType.GiftSubscribe,
                    Username = data?["user"]?["display_name"]?.ToString() ?? data?["displayName"]?.ToString() ?? "(unknown)",
                    DisplayText = $"{data?["gifts"]?.ToString() ?? "?"} 件のギフトサブ！",
                },
                "Raid" => new OverlayNotification
                {
                    SourcePlatform = "Twitch",
                    Type = NotificationType.Raid,
                    Username = data?["from_broadcaster_user_name"]?.ToString() ?? data?["displayName"]?.ToString() ?? "(unknown)",
                    DisplayText = $"レイドが来ました！ {data?["viewers"]?.ToString() ?? "?"} 人",
                },
                "RewardRedemption" => new OverlayNotification
                {
                    SourcePlatform = "Twitch",
                    Type = NotificationType.Reward,
                    Username = data?["user"]?["display_name"]?.ToString() ?? data?["displayName"]?.ToString() ?? "(unknown)",
                    DisplayText = data?["reward"]?["title"]?.ToString() ?? "チャンネルポイント交換",
                    SubText = data?["user_input"]?.ToString() ?? "",
                },
                "HypeTrainStart" => new OverlayNotification
                {
                    SourcePlatform = "Twitch",
                    Type = NotificationType.HypeTrainBegin,
                    Username = "Twitch",
                    DisplayText = "ハイプトレイン開始！",
                    SubText = $"レベル {data?["level"]?.ToString() ?? "1"}",
                },
                "HypeTrainEnd" => new OverlayNotification
                {
                    SourcePlatform = "Twitch",
                    Type = NotificationType.HypeTrainEnd,
                    Username = "Twitch",
                    DisplayText = "ハイプトレイン終了！",
                    SubText = $"最終レベル {data?["level"]?.ToString() ?? "?"}",
                },
                _ => null,
            };
        }

        private static OverlayNotification MapYouTubeEvent(string type, JToken data)
        {
            return type switch
            {
                "Message" => new OverlayNotification
                {
                    SourcePlatform = "YouTube",
                    Type = NotificationType.Chat,
                    Username = data?["message"]?["displayName"]?.ToString() ?? data?["displayName"]?.ToString() ?? "(unknown)",
                    DisplayText = data?["message"]?["message"]?.ToString() ?? data?["message"]?.ToString() ?? "",
                },
                "SuperChat" => new OverlayNotification
                {
                    SourcePlatform = "YouTube",
                    Type = NotificationType.Reward,
                    Username = data?["message"]?["displayName"]?.ToString() ?? data?["displayName"]?.ToString() ?? "(unknown)",
                    DisplayText = data?["message"]?["message"]?.ToString() ?? "",
                    SubText = data?["amount"]?.ToString() ?? "",
                },
                "NewSponsor" => new OverlayNotification
                {
                    SourcePlatform = "YouTube",
                    Type = NotificationType.Subscribe,
                    Username = data?["message"]?["displayName"]?.ToString() ?? data?["displayName"]?.ToString() ?? "(unknown)",
                    DisplayText = "メンバーシップ登録ありがとうございます！",
                },
                "MembershipGift" => new OverlayNotification
                {
                    SourcePlatform = "YouTube",
                    Type = NotificationType.GiftSubscribe,
                    Username = data?["message"]?["displayName"]?.ToString() ?? data?["displayName"]?.ToString() ?? "(unknown)",
                    DisplayText = $"{data?["count"]?.ToString() ?? "?"} 件のメンバーシップギフト！",
                },
                _ => null,
            };
        }

        private static OverlayNotification MapKickEvent(string type, JToken data)
        {
            return type switch
            {
                "ChatMessage" => new OverlayNotification
                {
                    SourcePlatform = "Kick",
                    Type = NotificationType.Chat,
                    Username = data?["message"]?["displayName"]?.ToString() ?? data?["displayName"]?.ToString() ?? "(unknown)",
                    DisplayText = data?["message"]?["content"]?.ToString() ?? data?["message"]?.ToString() ?? "",
                    UserColor = data?["message"]?["identity"]?["color"]?.ToString() ?? "#FFFFFF",
                },
                "Follow" => new OverlayNotification
                {
                    SourcePlatform = "Kick",
                    Type = NotificationType.Follow,
                    Username = data?["user"]?["username"]?.ToString() ?? data?["displayName"]?.ToString() ?? "(unknown)",
                    DisplayText = "フォローありがとうございます！",
                },
                "Subscription" => new OverlayNotification
                {
                    SourcePlatform = "Kick",
                    Type = NotificationType.Subscribe,
                    Username = data?["user"]?["username"]?.ToString() ?? data?["displayName"]?.ToString() ?? "(unknown)",
                    DisplayText = "サブスクリプションありがとうございます！",
                },
                "GiftSubscription" => new OverlayNotification
                {
                    SourcePlatform = "Kick",
                    Type = NotificationType.GiftSubscribe,
                    Username = data?["gifter"]?["username"]?.ToString() ?? data?["displayName"]?.ToString() ?? "(unknown)",
                    DisplayText = $"{data?["receiver"]?["username"]?.ToString() ?? "?"} にギフトサブ！",
                },
                "Resubscription" => new OverlayNotification
                {
                    SourcePlatform = "Kick",
                    Type = NotificationType.Resub,
                    Username = data?["user"]?["username"]?.ToString() ?? data?["displayName"]?.ToString() ?? "(unknown)",
                    DisplayText = "リサブスクリプションありがとうございます！",
                },
                _ => null,
            };
        }

        private static string BuildSubText(JToken data)
        {
            var months = data?["cumulative_months"]?.ToString();
            return !string.IsNullOrEmpty(months) && months != "0" && months != "1" ? $"累計 {months} ヶ月" : "サブスクリプションありがとうございます！";
        }

        private static JObject BuildSubscribeRequest()
        {
            return new JObject
            {
                ["request"] = "Subscribe",
                ["id"] = "tco-subscribe-1",
                ["events"] = new JObject
                {
                    ["Twitch"] = new JArray(
                        "ChatMessage",
                        "Follow",
                        "Sub",
                        "ReSub",
                        "GiftSub",
                        "GiftBomb",
                        "Raid",
                        "RewardRedemption",
                        "HypeTrainStart",
                        "HypeTrainEnd"
                    ),
                    ["YouTube"] = new JArray(
                        "Message",
                        "SuperChat",
                        "NewSponsor",
                        "MembershipGift"
                    ),
                    ["Kick"] = new JArray(
                        "ChatMessage",
                        "Follow",
                        "Subscription",
                        "GiftSubscription",
                        "Resubscription"
                    ),
                },
            };
        }

        private static string BuildAuthentication(string password, JToken authToken)
        {
            if (authToken == null)
            {
                return null;
            }

            var challenge = authToken["challenge"]?.ToString();
            var salt = authToken["salt"]?.ToString();
            if (string.IsNullOrEmpty(challenge) || string.IsNullOrEmpty(salt))
            {
                return null;
            }

            var basePassword = password ?? string.Empty;
            var secretHash = SHA256.HashData(Encoding.UTF8.GetBytes(basePassword + salt));
            var secret = Convert.ToBase64String(secretHash);
            var authHash = SHA256.HashData(Encoding.UTF8.GetBytes(secret + challenge));
            return Convert.ToBase64String(authHash);
        }

        private static async Task SendJsonAsync(ClientWebSocket socket, JObject payload, CancellationToken cancellationToken)
        {
            var bytes = Encoding.UTF8.GetBytes(payload.ToString(Newtonsoft.Json.Formatting.None));
            await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
        }

        private static async Task<JObject> ReceiveJsonAsync(ClientWebSocket socket, CancellationToken cancellationToken)
        {
            var buffer = new byte[8192];
            var builder = new StringBuilder();

            while (true)
            {
                var result = await socket.ReceiveAsync(buffer, cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    throw new InvalidOperationException($"WebSocket が閉じられました: {result.CloseStatus} {result.CloseStatusDescription}");
                }

                _ = builder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                if (result.EndOfMessage)
                {
                    return JObject.Parse(builder.ToString());
                }
            }
        }
    }
}
