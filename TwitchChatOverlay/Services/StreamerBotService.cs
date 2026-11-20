using Newtonsoft.Json.Linq;
using System;
using System.Globalization;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TwitchChatOverlay.Models;

namespace TwitchChatOverlay.Services
{
    /// <summary>Streamer.bot WebSocket 接続の失敗理由。</summary>
    public enum StreamerBotConnectionFailureReason
    {
        None,
        Unavailable,
        AuthenticationFailed,
        ConfigurationError,
        ProtocolError,
        Unknown,
    }

    /// <summary>Streamer.bot WebSocket 接続結果。</summary>
    public sealed class StreamerBotConnectionResult
    {
        public bool IsConnected { get; init; }

        public StreamerBotConnectionFailureReason FailureReason { get; init; }

        public string Message { get; init; }

        /// <summary>接続先が起動していない可能性があり、時間をおいた再接続が有効かどうか。</summary>
        public bool ShouldRetry => this.FailureReason == StreamerBotConnectionFailureReason.Unavailable;

        public static StreamerBotConnectionResult Success()
        {
            return new StreamerBotConnectionResult
            {
                IsConnected = true,
                FailureReason = StreamerBotConnectionFailureReason.None,
            };
        }
    }

    /// <summary>
    /// Streamer.bot の WebSocket Server に接続し、Twitch / YouTube / Kick のイベントを
    /// <see cref="OverlayNotification"/> に変換して通知する。
    /// Streamer.bot のプロトコルは OBS WebSocket v5（op/d 形式）ではなく、
    /// <c>request</c> / <c>id</c> / <c>status</c> 形式の独自プロトコル。
    /// 認証アルゴリズムのみ OBS WebSocket v5 と共通。
    /// </summary>
    public sealed class StreamerBotService
    {
        private const int MaxMessageBytes = 512 * 1024;

        private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);

        private static readonly string[] TwitchEvents =
        [
            "ChatMessage",
            "Follow",
            "Sub",
            "ReSub",
            "GiftSub",
            "GiftBomb",
            "Raid",
            "RewardRedemption",
            "HypeTrainStart",
            "HypeTrainEnd",
        ];

        private static readonly string[] YouTubeEvents =
        [
            "Message",
            "SuperChat",
            "SuperSticker",
            "NewSponsor",
            "NewSubscriber",
            "MemberMileStone",
            "MembershipGift",
            "GiftMembershipReceived",
        ];

        private static readonly string[] KickEvents =
        [
            "ChatMessage",
            "Follow",
            "Subscription",
            "Resubscription",
            "GiftSubscription",
            "MassGiftSubscription",
        ];

        private readonly SemaphoreSlim _connectLock = new(1, 1);
        private ClientWebSocket _webSocket;
        private CancellationTokenSource _cts;
        private int _requestId;

        public bool IsConnected { get; private set; }

        /// <summary>接続先 Streamer.bot のバージョン（Info から取得。未取得時は null）。</summary>
        public string ServerVersion { get; private set; }

        public event EventHandler<OverlayNotification> NotificationReceived;
        public event EventHandler ConnectionStateChanged;
        public event EventHandler ConnectionLost;

        public async Task<StreamerBotConnectionResult> ConnectAsync(string host, int port, string password, CancellationToken cancellationToken = default)
        {
            await this._connectLock.WaitAsync(cancellationToken);
            try
            {
                if (this.IsConnected)
                {
                    return StreamerBotConnectionResult.Success();
                }

                this.DisconnectCore();

                if (!TryBuildEndpoint(host, port, out var endpoint, out var configurationError))
                {
                    LogService.Warning($"Streamer.bot 接続設定が不正です: {configurationError}");
                    return new StreamerBotConnectionResult
                    {
                        FailureReason = StreamerBotConnectionFailureReason.ConfigurationError,
                        Message = configurationError,
                    };
                }

                var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(ConnectTimeout);
                var socket = new ClientWebSocket();
                socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
                this._cts = cts;
                this._webSocket = socket;

                try
                {
                    await socket.ConnectAsync(endpoint, cts.Token);
                    await this.PerformHandshakeAsync(socket, password, cts.Token);

                    // 接続後のタイムアウトを解除する（受信ループは接続が続く限り待機する）
                    cts.CancelAfter(Timeout.InfiniteTimeSpan);
                }
                catch (Exception ex)
                {
                    LogService.Warning("Streamer.bot WebSocket 接続失敗", ex);
                    var failure = CreateFailureResult(ex);
                    this.DisconnectCore();
                    return failure;
                }

                this.SetConnectionState(true);
                var token = cts.Token;
                _ = Task.Run(() => this.ReceiveLoopAsync(socket, token), CancellationToken.None);
                LogService.Info($"Streamer.bot WebSocket 接続完了 (version: {this.ServerVersion ?? "不明"})");
                return StreamerBotConnectionResult.Success();
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
            this.ServerVersion = null;
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

        private async Task PerformHandshakeAsync(ClientWebSocket socket, string password, CancellationToken cancellationToken)
        {
            // 接続直後にサーバから Hello が届く（request: "Hello"）。
            var hello = await this.ReceiveUntilAsync(socket, message => IsRequest(message, "Hello"), cancellationToken);
            this.ServerVersion = hello["info"]?["version"]?.ToString();

            var authentication = hello["authentication"];
            if (authentication is JObject)
            {
                var challenge = authentication["challenge"]?.ToString();
                var salt = authentication["salt"]?.ToString();
                if (string.IsNullOrEmpty(challenge) || string.IsNullOrEmpty(salt))
                {
                    throw new StreamerBotProtocolException("Streamer.bot の Hello に認証情報（salt / challenge）が含まれていません。");
                }

                if (string.IsNullOrEmpty(password))
                {
                    // パスワード未設定。Streamer.bot は既定で SendMessage のみ認証必須のため、
                    // 未認証でもイベント購読できる構成がある。公式クライアントと同様に
                    // GetInfo が通るかどうかで「認証が必須か」を判定する。
                    var infoId = this.NextRequestId("getinfo");
                    await SendJsonAsync(socket, new JObject
                    {
                        ["request"] = "GetInfo",
                        ["id"] = infoId,
                    }, cancellationToken);

                    var info = await this.ReceiveUntilAsync(socket, message => HasId(message, infoId), cancellationToken);
                    if (!IsStatusOk(info))
                    {
                        throw new StreamerBotConnectionException(
                            StreamerBotConnectionFailureReason.AuthenticationFailed,
                            "Streamer.bot の WebSocket Server はパスワード認証が必要です。設定タブでパスワードを入力してください。");
                    }
                }
                else
                {
                    var authenticateId = this.NextRequestId("authenticate");
                    await SendJsonAsync(socket, new JObject
                    {
                        ["request"] = "Authenticate",
                        ["id"] = authenticateId,
                        ["authentication"] = BuildAuthentication(password, challenge, salt),
                    }, cancellationToken);

                    JObject authResponse;
                    try
                    {
                        authResponse = await this.ReceiveUntilAsync(socket, message => HasId(message, authenticateId), cancellationToken);
                    }
                    catch (StreamerBotProtocolException ex)
                    {
                        // 認証失敗時にサーバが接続を閉じる実装へのフォールバック
                        throw new StreamerBotConnectionException(
                            StreamerBotConnectionFailureReason.AuthenticationFailed,
                            $"Streamer.bot の認証に失敗しました。パスワードを確認してください。({ex.Message})");
                    }

                    if (!IsStatusOk(authResponse))
                    {
                        throw new StreamerBotConnectionException(
                            StreamerBotConnectionFailureReason.AuthenticationFailed,
                            $"Streamer.bot の認証に失敗しました。{DescribeError(authResponse)}");
                    }
                }
            }

            await this.SubscribeAsync(socket, cancellationToken);
        }

        private async Task SubscribeAsync(ClientWebSocket socket, CancellationToken cancellationToken)
        {
            var subscribeId = this.NextRequestId("subscribe");
            await SendJsonAsync(socket, BuildSubscribeRequest(subscribeId, includeKick: true), cancellationToken);
            var response = await this.ReceiveUntilAsync(socket, message => HasId(message, subscribeId), cancellationToken);
            if (IsStatusOk(response))
            {
                LogService.Info("Streamer.bot イベント購読を開始しました。");
                return;
            }

            // Kick 連携が無効な Streamer.bot では Kick 購読がエラーになることがある。
            // Twitch / YouTube だけで再試行し、それでも失敗する場合のみ接続失敗とする。
            LogService.Warning($"Streamer.bot のイベント購読に失敗したため Kick を除外して再試行します: {DescribeError(response)}");

            var retryId = this.NextRequestId("subscribe");
            await SendJsonAsync(socket, BuildSubscribeRequest(retryId, includeKick: false), cancellationToken);
            var retryResponse = await this.ReceiveUntilAsync(socket, message => HasId(message, retryId), cancellationToken);
            if (IsStatusOk(retryResponse))
            {
                LogService.Info("Streamer.bot イベント購読を開始しました（Kick を除く）。");
                return;
            }

            throw new StreamerBotProtocolException($"Streamer.bot のイベント購読に失敗しました。{DescribeError(retryResponse)}");
        }

        private async Task ReceiveLoopAsync(ClientWebSocket socket, CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var message = await ReceiveMessageAsync(socket, cancellationToken);
                    if (message == null)
                    {
                        break;
                    }

                    if (await this.HandleControlMessageAsync(socket, message, cancellationToken))
                    {
                        continue;
                    }

                    if (message["event"] is JObject eventObject)
                    {
                        this.HandleEvent(eventObject, message["data"]);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                LogService.Info("Streamer.bot WebSocket 受信ループ終了（キャンセル）");
            }
            catch (Exception ex)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    LogService.Info("Streamer.bot WebSocket 受信ループ終了（切断）");
                }
                else
                {
                    LogService.Warning("Streamer.bot WebSocket 受信ループエラー", ex);
                }
            }
            finally
            {
                if (this._webSocket == socket)
                {
                    this.SetConnectionState(false);
                }
            }

            if (!cancellationToken.IsCancellationRequested)
            {
                ConnectionLost?.Invoke(this, EventArgs.Empty);
            }
        }

        private async Task<bool> HandleControlMessageAsync(ClientWebSocket socket, JObject message, CancellationToken cancellationToken)
        {
            // 公式仕様には Ping/Pong の記載はないが、実装によっては送られてくるため防御的に応答する。
            if (IsRequest(message, "Ping"))
            {
                await SendJsonAsync(socket, new JObject { ["request"] = "Pong" }, cancellationToken);
                return true;
            }

            if (IsRequest(message, "Pong"))
            {
                return true;
            }

            // 要求に対する応答（status 付き）で、購読処理などで消費しなかったものはここで捨てる。
            return message["status"] != null;
        }

        private void HandleEvent(JObject eventObject, JToken data)
        {
            try
            {
                var source = eventObject["source"]?.ToString() ?? string.Empty;
                var type = eventObject["type"]?.ToString() ?? string.Empty;

                var notification = MapToNotification(source, type, data);
                if (notification == null)
                {
                    LogService.Debug($"Streamer.bot 未対応イベントを無視しました: {source}.{type}");
                    return;
                }

                NotificationReceived?.Invoke(this, notification);
            }
            catch (Exception ex)
            {
                // イベント本文（チャット内容など）はログに残さない
                LogService.Warning($"Streamer.bot イベント処理エラー: {eventObject["source"]}.{eventObject["type"]}", ex);
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
                    ViaStreamerBot = true,
                    Type = NotificationType.Chat,
                    Username = GetUserName(data),
                    DisplayText = GetChatMessage(data),
                    UserColor = GetUserColor(data),
                },
                "Follow" => new OverlayNotification
                {
                    SourcePlatform = "Twitch",
                    ViaStreamerBot = true,
                    Type = NotificationType.Follow,
                    Username = GetUserName(data),
                    DisplayText = "フォローありがとうございます！",
                },
                "Sub" => new OverlayNotification
                {
                    SourcePlatform = "Twitch",
                    ViaStreamerBot = true,
                    Type = NotificationType.Subscribe,
                    Username = GetUserName(data),
                    DisplayText = string.IsNullOrWhiteSpace(GetChatMessage(data)) ? "サブスクリプションありがとうございます！" : GetChatMessage(data),
                    SubText = GetSubPlanName(data),
                },
                "ReSub" => new OverlayNotification
                {
                    SourcePlatform = "Twitch",
                    ViaStreamerBot = true,
                    Type = NotificationType.Resub,
                    Username = GetUserName(data),
                    DisplayText = string.IsNullOrWhiteSpace(GetChatMessage(data))
                        ? "リサブスクリプションありがとうございます！"
                        : GetChatMessage(data),
                    SubText = GetCumulativeMonths(data),
                },
                "GiftSub" => new OverlayNotification
                {
                    SourcePlatform = "Twitch",
                    ViaStreamerBot = true,
                    Type = NotificationType.GiftSubscribe,
                    Username = GetUserName(data),
                    DisplayText = $"{GetString(data, "recipientDisplayName") ?? GetString(data, "recipient_display_name") ?? GetString(data, "recipientUsername") ?? GetUserName(data)} にギフトサブ！",
                },
                "GiftBomb" => new OverlayNotification
                {
                    SourcePlatform = "Twitch",
                    ViaStreamerBot = true,
                    Type = NotificationType.GiftSubscribe,
                    Username = GetUserName(data),
                    DisplayText = $"{GetString(data, "totalGifts") ?? GetString(data, "gifts") ?? GetString(data, "gift_count") ?? "?"} 件のギフトサブ！",
                },
                "Raid" => new OverlayNotification
                {
                    SourcePlatform = "Twitch",
                    ViaStreamerBot = true,
                    Type = NotificationType.Raid,
                    Username = GetString(data, "from_broadcaster_user_name")
                        ?? GetString(data, "fromBroadcasterUserName")
                        ?? GetString(Child(data, "user"), "name")
                        ?? GetUserName(data),
                    DisplayText = $"レイドが来ました！ {GetString(data, "viewers") ?? GetString(data, "viewerCount") ?? "?"} 人",
                },
                "RewardRedemption" => new OverlayNotification
                {
                    SourcePlatform = "Twitch",
                    ViaStreamerBot = true,
                    Type = NotificationType.Reward,
                    Username = GetUserName(data),
                    DisplayText = GetString(Child(data, "reward"), "title") ?? GetString(Child(data, "reward"), "name") ?? "チャンネルポイント交換",
                    SubText = GetString(data, "user_input") ?? GetString(data, "userInput") ?? GetString(Child(data, "message"), "message"),
                },
                "HypeTrainStart" => new OverlayNotification
                {
                    SourcePlatform = "Twitch",
                    ViaStreamerBot = true,
                    Type = NotificationType.HypeTrainBegin,
                    Username = "Twitch",
                    DisplayText = "ハイプトレイン開始！",
                    SubText = $"レベル {GetString(data, "level") ?? "1"}",
                },
                "HypeTrainEnd" => new OverlayNotification
                {
                    SourcePlatform = "Twitch",
                    ViaStreamerBot = true,
                    Type = NotificationType.HypeTrainEnd,
                    Username = "Twitch",
                    DisplayText = "ハイプトレイン終了！",
                    SubText = $"最終レベル {GetString(data, "level") ?? "?"}",
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
                    ViaStreamerBot = true,
                    Type = NotificationType.Chat,
                    Username = GetUserName(data),
                    DisplayText = GetChatMessage(data),
                    UserColor = GetUserColor(data),
                },
                "SuperChat" => new OverlayNotification
                {
                    SourcePlatform = "YouTube",
                    ViaStreamerBot = true,
                    Type = NotificationType.Reward,
                    Username = GetUserName(data),
                    DisplayText = string.IsNullOrWhiteSpace(GetChatMessage(data)) ? "Super Chat ありがとうございます！" : GetChatMessage(data),
                    SubText = GetAmountString(data),
                },
                "SuperSticker" => new OverlayNotification
                {
                    SourcePlatform = "YouTube",
                    ViaStreamerBot = true,
                    Type = NotificationType.Reward,
                    Username = GetUserName(data),
                    DisplayText = "Super Sticker ありがとうございます！",
                    SubText = GetAmountString(data),
                },
                "NewSponsor" or "NewSubscriber" => new OverlayNotification
                {
                    SourcePlatform = "YouTube",
                    ViaStreamerBot = true,
                    Type = NotificationType.Subscribe,
                    Username = GetUserName(data),
                    DisplayText = "メンバーシップ登録ありがとうございます！",
                },
                "MemberMileStone" or "MemberMilestoneChat" => new OverlayNotification
                {
                    SourcePlatform = "YouTube",
                    ViaStreamerBot = true,
                    Type = NotificationType.Resub,
                    Username = GetUserName(data),
                    DisplayText = string.IsNullOrWhiteSpace(GetChatMessage(data)) ? "メンバーシップ継続ありがとうございます！" : GetChatMessage(data),
                    SubText = GetMembershipMonths(data),
                },
                "MembershipGift" or "GiftMembershipReceived" => new OverlayNotification
                {
                    SourcePlatform = "YouTube",
                    ViaStreamerBot = true,
                    Type = NotificationType.GiftSubscribe,
                    Username = GetUserName(data),
                    DisplayText = $"{GetString(data, "count") ?? GetString(Child(data, "gift"), "count") ?? GetString(data, "giftCount") ?? "?"} 件のメンバーシップギフト！",
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
                    ViaStreamerBot = true,
                    Type = NotificationType.Chat,
                    Username = GetUserName(data),
                    DisplayText = GetChatMessage(data),
                    UserColor = GetUserColor(data),
                },
                "Follow" => new OverlayNotification
                {
                    SourcePlatform = "Kick",
                    ViaStreamerBot = true,
                    Type = NotificationType.Follow,
                    Username = GetUserName(data),
                    DisplayText = "フォローありがとうございます！",
                },
                "Subscription" => new OverlayNotification
                {
                    SourcePlatform = "Kick",
                    ViaStreamerBot = true,
                    Type = NotificationType.Subscribe,
                    Username = GetUserName(data),
                    DisplayText = "サブスクリプションありがとうございます！",
                },
                "GiftSubscription" => new OverlayNotification
                {
                    SourcePlatform = "Kick",
                    ViaStreamerBot = true,
                    Type = NotificationType.GiftSubscribe,
                    Username = GetString(Child(data, "gifter"), "username") ?? GetString(Child(data, "gifter"), "name") ?? GetUserName(data),
                    DisplayText = $"{GetString(Child(data, "receiver"), "username") ?? GetString(Child(data, "receiver"), "name") ?? GetString(Child(data, "recipient"), "username") ?? GetString(Child(data, "recipient"), "name") ?? "?"} にギフトサブ！",
                },
                "MassGiftSubscription" => new OverlayNotification
                {
                    SourcePlatform = "Kick",
                    ViaStreamerBot = true,
                    Type = NotificationType.GiftSubscribe,
                    Username = GetString(data, "gifter_username") ?? GetString(Child(data, "gifter"), "username") ?? GetUserName(data),
                    DisplayText = $"{GetString(data, "gifter_total") ?? GetString(data, "count") ?? GetArrayLength(Child(data, "gifted_usernames")) ?? "?"} 件のギフトサブ！",
                },
                "Resubscription" => new OverlayNotification
                {
                    SourcePlatform = "Kick",
                    ViaStreamerBot = true,
                    Type = NotificationType.Resub,
                    Username = GetUserName(data),
                    DisplayText = "リサブスクリプションありがとうございます！",
                },
                _ => null,
            };
        }

        private static string GetUserName(JToken data)
        {
            if (data == null)
            {
                return "(unknown)";
            }

            return FirstNonEmpty(
                GetString(Child(data, "user"), "displayName"),
                GetString(Child(data, "user"), "display_name"),
                GetString(Child(data, "user"), "name"),
                GetString(Child(data, "user"), "username"),
                GetString(Child(data, "user"), "login"),
                GetString(Child(data, "sender"), "username"),
                GetString(Child(data, "sender"), "name"),
                GetString(Child(data, "message"), "displayName"),
                GetString(Child(data, "message"), "username"),
                GetString(Child(data, "message"), "user", "name"),
                GetString(Child(data, "message"), "sender", "username"),
                GetString(data, "displayName"),
                GetString(data, "display_name"),
                GetString(data, "userName"),
                GetString(data, "user_name"),
                GetString(data, "username"),
                "(unknown)");
        }

        private static string GetChatMessage(JToken data)
        {
            if (data == null)
            {
                return string.Empty;
            }

            var messageNode = Child(data, "message");
            return FirstNonEmpty(
                GetString(messageNode, "message"),
                GetString(messageNode, "text"),
                GetString(messageNode, "content"),
                GetString(messageNode, "msg"),
                messageNode != null && messageNode.Type == JTokenType.String ? messageNode.ToString() : null,
                GetString(data, "text"),
                GetString(data, "message"),
                string.Empty);
        }

        private static string GetUserColor(JToken data)
        {
            if (data == null)
            {
                return null;
            }

            return FirstNonEmpty(
                GetString(Child(data, "message"), "color"),
                GetString(Child(data, "message"), "identity", "color"),
                GetString(Child(data, "user"), "color"),
                GetString(data, "color"));
        }

        private static string GetSubPlanName(JToken data)
        {
            var tier = FirstNonEmpty(
                GetString(Child(data, "sub"), "planName"),
                GetString(Child(data, "sub"), "plan"),
                GetString(Child(data, "sub"), "subTier"),
                GetString(Child(data, "sub"), "tier"),
                GetString(data, "subTier"),
                GetString(data, "planName"));

            // Streamer.bot / Twitch はサブスク種別を数値コードで返す
            return tier switch
            {
                "1000" => "Tier 1",
                "2000" => "Tier 2",
                "3000" => "Tier 3",
                "4000" => "Prime",
                _ => tier,
            };
        }

        private static string GetCumulativeMonths(JToken data)
        {
            var months = FirstNonEmpty(
                GetString(data, "cumulative_months"),
                GetString(data, "cumulativeMonths"),
                GetString(Child(data, "sub"), "cumulativeMonths"),
                GetString(Child(data, "sub"), "months"));
            return months == null ? null : $"累計 {months} ヶ月";
        }

        private static string GetAmountString(JToken data)
        {
            return FirstNonEmpty(
                GetString(Child(data, "message"), "amountString"),
                GetString(Child(data, "superChat"), "amountString"),
                GetString(Child(data, "superSticker"), "amountString"),
                GetString(Child(data, "amount"), "string"),
                GetString(Child(data, "amount"), "amountString"),
                GetString(data, "amount"),
                GetString(data, "amountString"));
        }

        private static string GetArrayLength(JToken node)
        {
            return node is JArray array ? array.Count.ToString(CultureInfo.InvariantCulture) : null;
        }

        private static string GetMembershipMonths(JToken data)
        {
            var months = FirstNonEmpty(
                GetString(data, "memberMonth"),
                GetString(data, "months"),
                GetString(Child(data, "message"), "memberMonth"));
            return months == null ? null : $"{months} ヶ月";
        }

        /// <summary>先行する値から最初の非空文字列を返す。</summary>
        private static string FirstNonEmpty(params string[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return null;
        }

        /// <summary>
        /// 子要素を取得する。オブジェクト以外（文字列・数値など）に対しては null を返す。
        /// Streamer.bot の payload は <c>message</c> が文字列のイベントとオブジェクトのイベントが混在するため、
        /// <see cref="JToken"/> のインデクサーを直接使うと例外になる。
        /// </summary>
        private static JToken Child(JToken node, string name)
        {
            return node is JObject obj ? obj[name] : null;
        }

        private static string GetString(JToken node, params string[] path)
        {
            var current = node;
            foreach (var name in path)
            {
                current = Child(current, name);
                if (current == null)
                {
                    return null;
                }
            }

            return current.Type switch
            {
                JTokenType.String => current.ToString(),
                JTokenType.Integer or JTokenType.Float => Convert.ToString(((JValue)current).Value, CultureInfo.InvariantCulture),
                JTokenType.Boolean => current.Value<bool>() ? "true" : "false",
                _ => null,
            };
        }

        private static JObject BuildSubscribeRequest(string requestId, bool includeKick)
        {
            var events = new JObject
            {
                ["Twitch"] = new JArray(TwitchEvents),
                ["YouTube"] = new JArray(YouTubeEvents),
            };

            if (includeKick)
            {
                events["Kick"] = new JArray(KickEvents);
            }

            return new JObject
            {
                ["request"] = "Subscribe",
                ["id"] = requestId,
                ["events"] = events,
            };
        }

        private static string BuildAuthentication(string password, string challenge, string salt)
        {
            var secretHash = SHA256.HashData(Encoding.UTF8.GetBytes((password ?? string.Empty) + salt));
            var secret = Convert.ToBase64String(secretHash);
            var authHash = SHA256.HashData(Encoding.UTF8.GetBytes(secret + challenge));
            return Convert.ToBase64String(authHash);
        }

        private static bool TryBuildEndpoint(string host, int port, out Uri endpoint, out string error)
        {
            endpoint = null;
            error = null;

            var targetHost = string.IsNullOrWhiteSpace(host) ? "127.0.0.1" : host.Trim();
            var targetPort = port > 0 ? port : 8080;

            if (targetPort is < 1 or > 65535)
            {
                error = $"ポート番号が不正です: {targetPort}";
                return false;
            }

            // ホスト名として解釈できない値（URL やパスを含む文字列）は拒否する
            if (Uri.CheckHostName(targetHost) == UriHostNameType.Unknown)
            {
                error = $"ホスト名が不正です: {targetHost}";
                return false;
            }

            try
            {
                var builder = new UriBuilder("ws", targetHost, targetPort, "/");
                endpoint = builder.Uri;
                return true;
            }
            catch (UriFormatException ex)
            {
                error = $"接続先 URL を組み立てられませんでした: {ex.Message}";
                return false;
            }
        }

        private string NextRequestId(string prefix)
        {
            return $"{prefix}-{Interlocked.Increment(ref this._requestId)}";
        }

        private async Task<JObject> ReceiveUntilAsync(ClientWebSocket socket, Func<JObject, bool> predicate, CancellationToken cancellationToken)
        {
            while (true)
            {
                var message = await ReceiveMessageAsync(socket, cancellationToken);
                if (message == null)
                {
                    throw new StreamerBotProtocolException("Streamer.bot が WebSocket を閉じました。");
                }

                if (IsRequest(message, "Ping"))
                {
                    await SendJsonAsync(socket, new JObject { ["request"] = "Pong" }, cancellationToken);
                    continue;
                }

                if (predicate(message))
                {
                    return message;
                }
            }
        }

        private static async Task<JObject> ReceiveMessageAsync(ClientWebSocket socket, CancellationToken cancellationToken)
        {
            var buffer = new byte[8192];
            var builder = new StringBuilder();

            while (true)
            {
                var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return null;
                }

                if (result.MessageType != WebSocketMessageType.Text)
                {
                    continue;
                }

                _ = builder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                if (builder.Length > MaxMessageBytes)
                {
                    throw new StreamerBotProtocolException($"Streamer.bot から受信したメッセージが大きすぎます（{builder.Length} 文字）。");
                }

                if (result.EndOfMessage)
                {
                    return JObject.Parse(builder.ToString());
                }
            }
        }

        private static bool IsRequest(JObject message, string request)
        {
            return string.Equals(message["request"]?.ToString(), request, StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasId(JObject message, string id)
        {
            return string.Equals(message["id"]?.ToString(), id, StringComparison.Ordinal);
        }

        private static bool IsStatusOk(JObject message)
        {
            return string.Equals(message["status"]?.ToString(), "ok", StringComparison.OrdinalIgnoreCase);
        }

        private static string DescribeError(JObject message)
        {
            var error = message["error"]?.ToString();
            var status = message["status"]?.ToString();
            return string.IsNullOrWhiteSpace(error) ? $"status={status ?? "不明"}" : error;
        }

        private static async Task SendJsonAsync(ClientWebSocket socket, JObject payload, CancellationToken cancellationToken)
        {
            var bytes = Encoding.UTF8.GetBytes(payload.ToString(Newtonsoft.Json.Formatting.None));
            await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
        }

        private static StreamerBotConnectionResult CreateFailureResult(Exception ex)
        {
            if (ex is StreamerBotConnectionException connectionException)
            {
                return new StreamerBotConnectionResult
                {
                    FailureReason = connectionException.FailureReason,
                    Message = connectionException.Message,
                };
            }

            var socketException = FindSocketException(ex);
            if (socketException != null && IsRetryableSocketError(socketException.SocketErrorCode))
            {
                return new StreamerBotConnectionResult
                {
                    FailureReason = StreamerBotConnectionFailureReason.Unavailable,
                    Message = "Streamer.bot が見つかりません。10 秒後に再試行します。",
                };
            }

            if (ex is OperationCanceledException)
            {
                return new StreamerBotConnectionResult
                {
                    FailureReason = StreamerBotConnectionFailureReason.Unknown,
                    Message = "Streamer.bot 接続がタイムアウトしました。",
                };
            }

            return new StreamerBotConnectionResult
            {
                FailureReason = StreamerBotConnectionFailureReason.Unknown,
                Message = $"Streamer.bot 接続エラー: {ex.Message}",
            };
        }

        private static SocketException FindSocketException(Exception ex)
        {
            for (var current = ex; current != null; current = current.InnerException)
            {
                if (current is SocketException socketException)
                {
                    return socketException;
                }
            }

            return null;
        }

        private static bool IsRetryableSocketError(SocketError socketError)
        {
            return socketError is SocketError.ConnectionRefused
                or SocketError.TimedOut
                or SocketError.HostDown
                or SocketError.HostUnreachable
                or SocketError.NetworkDown
                or SocketError.NetworkUnreachable;
        }

        private sealed class StreamerBotProtocolException : Exception
        {
            public StreamerBotProtocolException(string message)
                : base(message)
            {
            }
        }

        private sealed class StreamerBotConnectionException : Exception
        {
            public StreamerBotConnectionException(StreamerBotConnectionFailureReason failureReason, string message)
                : base(message)
            {
                this.FailureReason = failureReason;
            }

            public StreamerBotConnectionFailureReason FailureReason { get; }
        }
    }
}
