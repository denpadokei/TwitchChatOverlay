using Grpc.Core;
using Grpc.Net.Client;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using TwitchChatOverlay.Models;
using TwitchChatOverlay.YouTube.LiveChat.V3;

namespace TwitchChatOverlay.Services
{
    public class YouTubeApiException : Exception
    {
        public int StatusCode { get; }
        public TimeSpan? RetryAfter { get; }

        public YouTubeApiException(int statusCode, string message, TimeSpan? retryAfter = null) : base(message)
        {
            this.StatusCode = statusCode;
            this.RetryAfter = retryAfter;
        }
    }

    public class YouTubeConnectionLostEventArgs : EventArgs
    {
        public bool IsUnauthorized { get; init; }
        public string Message { get; init; }
    }

    public class YouTubeWaitingForBroadcastEventArgs : EventArgs
    {
        public string Message { get; init; }
    }

    public class YouTubeBroadcastEndedEventArgs : EventArgs
    {
        public string Message { get; init; }
    }

    public class YouTubeLiveChatService
    {
        private static readonly HttpClient Http = new();
        private static readonly TimeSpan Http2KeepAlivePingDelay = TimeSpan.FromMinutes(1);
        private static readonly TimeSpan Http2KeepAlivePingTimeout = TimeSpan.FromSeconds(20);
        private static readonly SocketsHttpHandler GrpcHttpHandler = new()
        {
            EnableMultipleHttp2Connections = true,
            KeepAlivePingDelay = Http2KeepAlivePingDelay,
            KeepAlivePingTimeout = Http2KeepAlivePingTimeout,
            KeepAlivePingPolicy = HttpKeepAlivePingPolicy.WithActiveRequests
        };
        private static readonly GrpcChannel GrpcChannel = GrpcChannel.ForAddress(
            "https://youtube.googleapis.com",
            new GrpcChannelOptions
            {
                HttpHandler = GrpcHttpHandler
            });
        private static readonly V3DataLiveChatMessageService.V3DataLiveChatMessageServiceClient GrpcClient =
            new(GrpcChannel);
        private const int BroadcastPollIntervalMs = 30_000;
        private const int GrpcProfileImageSize = 88;
        private const int StreamMaxResults = 500;
        private const int MessageCacheSize = 5000;
        private static readonly TimeSpan GrpcStreamDeadline = TimeSpan.FromHours(1);

        private readonly HashSet<string> _seenMessageIds = [];
        private readonly Queue<string> _seenMessageOrder = new();
        private CancellationTokenSource _cts;
        private string _accessToken;
        private string _liveChatId;
        private string _resumePageToken;
        private bool _broadcastPollingPending;

        public event EventHandler<OverlayNotification> NotificationReceived;
        public event EventHandler<YouTubeConnectionLostEventArgs> ConnectionLost;
        public event EventHandler BroadcastDetected;
        public event EventHandler<YouTubeWaitingForBroadcastEventArgs> WaitingForBroadcastStarted;
        public event EventHandler<YouTubeBroadcastEndedEventArgs> BroadcastEnded;
        public bool IsConnected { get; private set; }
        public bool IsWaitingForBroadcast { get; private set; }

        public Task ConnectAsync(string accessToken)
        {
            return this.ConnectAsync(accessToken, checkImmediately: true, waitForObsSignalBeforePolling: false);
        }

        public async Task ConnectAsync(string accessToken, bool checkImmediately, bool waitForObsSignalBeforePolling)
        {
            await this.ConnectCoreAsync(accessToken, checkImmediately, waitForObsSignalBeforePolling, preserveState: false);
        }

        public async Task ReconnectAsync(string accessToken, bool checkImmediately, bool waitForObsSignalBeforePolling)
        {
            var preserveState = !this.IsWaitingForBroadcast && !string.IsNullOrWhiteSpace(this._liveChatId);
            await this.ConnectCoreAsync(accessToken, checkImmediately, waitForObsSignalBeforePolling, preserveState);
        }

        private async Task ConnectCoreAsync(string accessToken, bool checkImmediately, bool waitForObsSignalBeforePolling, bool preserveState)
        {
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                throw new InvalidOperationException("YouTube OAuth token がありません。");
            }

            this.DisconnectCurrentSession(clearState: !preserveState);
            this._accessToken = accessToken;
            this._cts = new CancellationTokenSource();
            this._broadcastPollingPending = false;

            LogService.Debug(
                $"YouTube接続を初期化します: mode={(preserveState ? "state preserved" : "full reset")}, " +
                $"liveChatId={(string.IsNullOrWhiteSpace(this._liveChatId) ? "none" : "present")}, " +
                $"pageToken={(string.IsNullOrWhiteSpace(this._resumePageToken) ? "none" : "present")}, " +
                $"seenIds={this._seenMessageIds.Count}");

            if (!preserveState || string.IsNullOrWhiteSpace(this._liveChatId))
            {
                this._liveChatId = checkImmediately ? await this.ResolveLiveChatIdAsync(this._cts.Token) : null;
            }
            else
            {
                LogService.Debug("YouTube再接続では liveChatId と pageToken を維持します");
            }

            if (string.IsNullOrWhiteSpace(this._liveChatId))
            {
                if (waitForObsSignalBeforePolling)
                {
                    this.EnterWaitingForBroadcast(startPollingImmediately: false, "YouTube 配信待機中です。OBS の配信開始検出後に30秒間隔の配信確認を開始します。");
                }
                else
                {
                    this.EnterWaitingForBroadcast(startPollingImmediately: true, "YouTube 配信が見つかりません。30秒間隔で配信確認を続行します。");
                }
                return;
            }

            this.IsConnected = true;
            _ = Task.Run(() => this.StreamLoopAsync(this._cts.Token));
            LogService.Info($"YouTube Live Chat 接続開始: liveChatId={this._liveChatId}, mode={(preserveState ? "state preserved" : "full reset")}");
        }

        public void StartBroadcastPolling()
        {
            if (!this.IsWaitingForBroadcast || !this._broadcastPollingPending || this._cts == null)
            {
                return;
            }

            this._broadcastPollingPending = false;
            _ = Task.Run(() => this.WaitForBroadcastLoopAsync(this._cts.Token));
            LogService.Info("YouTube 配信待機ポーリングを開始しました（30秒間隔）");
        }

        private void EnterWaitingForBroadcast(bool startPollingImmediately, string message)
        {
            this.IsConnected = false;
            this.IsWaitingForBroadcast = true;
            this._liveChatId = null;
            this._resumePageToken = null;
            this._broadcastPollingPending = true;

            LogService.Info(message);
            WaitingForBroadcastStarted?.Invoke(this, new YouTubeWaitingForBroadcastEventArgs
            {
                Message = message
            });

            if (startPollingImmediately)
            {
                this.StartBroadcastPolling();
            }
        }

        private void CompleteBroadcast(string message)
        {
            this.IsConnected = false;
            this.IsWaitingForBroadcast = false;
            this._liveChatId = null;
            this._resumePageToken = null;
            this._broadcastPollingPending = false;

            LogService.Info(message);
            BroadcastEnded?.Invoke(this, new YouTubeBroadcastEndedEventArgs
            {
                Message = message
            });
        }

        public void Disconnect()
        {
            this.DisconnectCurrentSession(clearState: true);
        }

        private void DisconnectCurrentSession(bool clearState)
        {
            this._cts?.Cancel();
            this._cts?.Dispose();
            this._cts = null;
            this._accessToken = null;
            this.IsConnected = false;
            this.IsWaitingForBroadcast = false;
            this._broadcastPollingPending = false;

            if (!clearState)
            {
                return;
            }

            this._liveChatId = null;
            this._resumePageToken = null;
            this._seenMessageIds.Clear();
            this._seenMessageOrder.Clear();
        }

        private async Task WaitForBroadcastLoopAsync(CancellationToken cancellationToken)
        {
            long failureCount = 0;
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        this._liveChatId = await this.ResolveLiveChatIdAsync(cancellationToken);
                        failureCount = 0;
                    }
                    catch (YouTubeApiException apiEx) when (apiEx.StatusCode != 401)
                    {
                        // 403（権限不足/API未有効化/クォータ超過など）は恒久的エラーのため待機を終了
                        if (apiEx.StatusCode == 403)
                        {
                            LogService.Warning($"YouTube 配信確認で権限/クォータエラー（{apiEx.StatusCode}）。待機を終了します。");
                            if (!cancellationToken.IsCancellationRequested)
                            {
                                this.IsWaitingForBroadcast = false;
                                ConnectionLost?.Invoke(this, new YouTubeConnectionLostEventArgs
                                {
                                    IsUnauthorized = false,
                                    Message = $"YouTube API エラー ({apiEx.StatusCode}): {apiEx.Message}"
                                });
                            }
                            return;
                        }

                        if (IsBroadcastEndedException(apiEx))
                        {
                            this.CompleteBroadcast("YouTube 配信終了を検出したため、配信待機を停止しました。");
                            return;
                        }

                        var delay = CalculateBackoffDelay(++failureCount, apiEx.RetryAfter);
                        LogService.Warning($"YouTube 配信確認失敗: {apiEx.StatusCode}。{delay.TotalSeconds:F1}秒後に再試行します");
                        await Task.Delay(delay, cancellationToken);
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(this._liveChatId))
                    {
                        this.IsWaitingForBroadcast = false;
                        this.IsConnected = true;
                        this._resumePageToken = null;
                        LogService.Info($"YouTube 配信を検出しました: liveChatId={this._liveChatId}");
                        BroadcastDetected?.Invoke(this, EventArgs.Empty);
                        await this.StreamLoopAsync(cancellationToken);
                        return;
                    }
                    LogService.Info("YouTube 配信中のブロードキャストが見つかりません。再試行します...");
                    await Task.Delay(BroadcastPollIntervalMs, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                LogService.Info("YouTube 配信待機終了（キャンセル）");
            }
            catch (Exception ex)
            {
                LogService.Error("YouTube 配信待機エラー", ex);
                if (!cancellationToken.IsCancellationRequested)
                {
                    this.IsConnected = false;
                    ConnectionLost?.Invoke(this, new YouTubeConnectionLostEventArgs
                    {
                        IsUnauthorized = ex is YouTubeApiException apiEx && apiEx.StatusCode == 401,
                        Message = ex.Message
                    });
                }
            }
            finally
            {
                if (this.IsCurrentSession(cancellationToken) || this._cts == null)
                {
                    this.IsWaitingForBroadcast = false;
                }
            }
        }

        private async Task<string> ResolveLiveChatIdAsync(CancellationToken cancellationToken)
        {
            // mine と broadcastStatus は同時指定不可。mine=true で自分の配信を全取得し、
            // lifeCycleStatus が live / liveStarting のものを選択する。
            var url = "https://www.googleapis.com/youtube/v3/liveBroadcasts" +
                         "?part=snippet,status&mine=true&maxResults=10";

            var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", this._accessToken);
            var res = await Http.SendAsync(req, cancellationToken);
            var json = await res.Content.ReadAsStringAsync(cancellationToken);
            if (!res.IsSuccessStatusCode)
            {
                throw new YouTubeApiException((int)res.StatusCode, $"YouTube liveBroadcasts 取得失敗: {(int)res.StatusCode} {json}", GetRetryAfter(res));
            }

            var obj = JObject.Parse(json);
            if (obj["items"] is not JArray items)
            {
                return null;
            }

            foreach (var item in items)
            {
                var status = item["status"]?["lifeCycleStatus"]?.ToString();
                if (status is "live" or "liveStarting")
                {
                    var liveChatId = item["snippet"]?["liveChatId"]?.ToString();
                    if (!string.IsNullOrEmpty(liveChatId))
                    {
                        return liveChatId;
                    }
                }
            }

            return null;
        }

        private async Task StreamLoopAsync(CancellationToken cancellationToken)
        {
            long failureCount = 0;

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        using var call = GrpcClient.StreamList(
                            this.CreateStreamRequest(),
                            headers: this.CreateGrpcHeaders(),
                            deadline: DateTime.UtcNow.Add(GrpcStreamDeadline),
                            cancellationToken: cancellationToken);

                        LogService.Info($"YouTube Live Chat gRPC ストリーム開始: liveChatId={this._liveChatId}");

                        var receivedResponse = false;
                        while (await call.ResponseStream.MoveNext(cancellationToken))
                        {
                            receivedResponse = true;
                            failureCount = 0;

                            var response = call.ResponseStream.Current;
                            if (!string.IsNullOrWhiteSpace(response.NextPageToken))
                            {
                                this._resumePageToken = response.NextPageToken;
                            }

                            foreach (var item in response.Items)
                            {
                                var messageId = item.Id;
                                if (string.IsNullOrEmpty(messageId) || this._seenMessageIds.Contains(messageId))
                                {
                                    continue;
                                }

                                this.AddSeenMessageId(messageId);

                                var notification = BuildNotification(item);
                                if (notification != null)
                                {
                                    NotificationReceived?.Invoke(this, notification);
                                }
                            }

                            if (!string.IsNullOrWhiteSpace(response.OfflineAt))
                            {
                                throw new YouTubeApiException(410, $"YouTube 配信が終了しました: offlineAt={response.OfflineAt}");
                            }
                        }

                        if (!receivedResponse)
                        {
                            throw new YouTubeApiException(502, "YouTube Live Chat gRPC ストリームが応答なしで終了しました。");
                        }

                        var closedDelay = CalculateBackoffDelay(++failureCount, null);
                        LogService.Debug($"YouTube Live Chat gRPC ストリームが終了しました。{closedDelay.TotalSeconds:F1}秒後に再接続します");
                        await Task.Delay(closedDelay, cancellationToken);
                    }
                    catch (RpcException rpcEx) when (rpcEx.StatusCode != StatusCode.Cancelled)
                    {
                        var apiEx = ConvertToYouTubeApiException(rpcEx);
                        if (IsBroadcastEndedException(apiEx))
                        {
                            this.CompleteBroadcast("YouTube 配信終了を検出したため、配信待機を停止しました。");
                            return;
                        }

                        if (ShouldResumeBroadcastWait(apiEx))
                        {
                            this.EnterWaitingForBroadcast(startPollingImmediately: true, "YouTube 配信が未検出になったため、30秒間隔の配信待機に戻ります。");
                            return;
                        }

                        if (ShouldBubbleStreamException(apiEx))
                        {
                            throw apiEx;
                        }

                        var delay = CalculateBackoffDelay(++failureCount, apiEx.RetryAfter);
                        LogService.Warning($"YouTube Live Chat gRPC 受信失敗: {apiEx.StatusCode}。{delay.TotalSeconds:F1}秒後に再接続します");
                        await Task.Delay(delay, cancellationToken);
                    }
                    catch (RpcException rpcEx) when (rpcEx.StatusCode == StatusCode.Cancelled)
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            LogService.Info("YouTube Live Chat gRPC ストリーム終了（キャンセル）");
                            return;
                        }

                        var delay = CalculateBackoffDelay(++failureCount, null);
                        LogService.Warning($"YouTube Live Chat gRPC ストリームがキャンセルされました。{delay.TotalSeconds:F1}秒後に再接続します");
                        await Task.Delay(delay, cancellationToken);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                LogService.Info("YouTube Live Chat gRPC ストリーム終了（キャンセル）");
            }
            catch (Exception ex)
            {
                LogService.Error("YouTube Live Chat gRPC ストリームエラー", ex);

                if (!cancellationToken.IsCancellationRequested)
                {
                    this.IsConnected = false;
                    var args = new YouTubeConnectionLostEventArgs
                    {
                        IsUnauthorized = ex is YouTubeApiException apiEx && apiEx.StatusCode == 401,
                        Message = ex.Message
                    };
                    ConnectionLost?.Invoke(this, args);
                }
            }
            finally
            {
                if (this.IsCurrentSession(cancellationToken) || this._cts == null)
                {
                    this.IsConnected = false;
                }
            }
        }

        private bool IsCurrentSession(CancellationToken cancellationToken)
        {
            return this._cts != null && this._cts.Token == cancellationToken;
        }

        private LiveChatMessageListRequest CreateStreamRequest()
        {
            var request = new LiveChatMessageListRequest
            {
                LiveChatId = this._liveChatId,
                ProfileImageSize = GrpcProfileImageSize,
                MaxResults = StreamMaxResults
            };

            if (!string.IsNullOrWhiteSpace(this._resumePageToken))
            {
                request.PageToken = this._resumePageToken;
            }

            request.Part.Add("snippet");
            request.Part.Add("authorDetails");
            return request;
        }

        private Metadata CreateGrpcHeaders()
        {
            var headers = new Metadata();
            if (!string.IsNullOrWhiteSpace(this._accessToken))
            {
                headers.Add("authorization", $"Bearer {this._accessToken}");
            }

            LogService.Debug($"YouTube Live Chat gRPC headers: authorization={(headers.Count > 0 ? "present" : "absent")}");
            return headers;
        }

        private void AddSeenMessageId(string messageId)
        {
            if (!this._seenMessageIds.Add(messageId))
            {
                return;
            }

            this._seenMessageOrder.Enqueue(messageId);
            while (this._seenMessageOrder.Count > MessageCacheSize)
            {
                var oldId = this._seenMessageOrder.Dequeue();
                _ = this._seenMessageIds.Remove(oldId);
            }
        }

        private static TimeSpan CalculateBackoffDelay(long failureCount, TimeSpan? retryAfter)
        {
            if (retryAfter.HasValue && retryAfter.Value > TimeSpan.Zero)
            {
                return retryAfter.Value;
            }

            double exponent = Math.Min(failureCount, 62);
            var baseSeconds = Math.Pow(2, exponent);
            var jitterMs = Random.Shared.Next(0, 1000);
            var totalMs = (baseSeconds * 1000.0) + jitterMs;
            if (totalMs > int.MaxValue)
            {
                totalMs = int.MaxValue;
            }

            return TimeSpan.FromMilliseconds(totalMs);
        }

        private static TimeSpan? GetRetryAfter(HttpResponseMessage response)
        {
            if (response.Headers.RetryAfter?.Delta is TimeSpan delta && delta > TimeSpan.Zero)
            {
                return delta;
            }

            if (response.Headers.RetryAfter?.Date is DateTimeOffset retryAt)
            {
                var diff = retryAt - DateTimeOffset.UtcNow;
                if (diff > TimeSpan.Zero)
                {
                    return diff;
                }
            }

            return null;
        }

        private static bool ShouldBubbleStreamException(YouTubeApiException exception)
        {
            return exception.StatusCode is 401 or 403;
        }

        private static bool IsBroadcastEndedException(YouTubeApiException exception)
        {
            return exception.StatusCode == 410;
        }

        private static bool ShouldResumeBroadcastWait(YouTubeApiException exception)
        {
            return exception.StatusCode is 404 or 412;
        }

        private static YouTubeApiException ConvertToYouTubeApiException(RpcException exception)
        {
            var statusCode = exception.StatusCode switch
            {
                StatusCode.Unauthenticated => 401,
                StatusCode.PermissionDenied => 403,
                StatusCode.InvalidArgument => 400,
                StatusCode.NotFound => 404,
                StatusCode.FailedPrecondition => 412,
                StatusCode.ResourceExhausted => 429,
                StatusCode.Unavailable => 503,
                StatusCode.DeadlineExceeded => 504,
                _ => 500
            };

            return new YouTubeApiException(statusCode, $"YouTube liveChat stream失敗: {exception.StatusCode} {exception.Status.Detail}");
        }

        private static OverlayNotification BuildNotification(LiveChatMessage item)
        {
            var snippet = item.Snippet;
            if (snippet == null)
            {
                return null;
            }

            var username = item.AuthorDetails?.DisplayName ?? "YouTubeUser";
            var message = snippet.DisplayMessage ?? string.Empty;

            return snippet.Type switch
            {
                LiveChatMessageSnippet.Types.Type.TextMessageEvent => new OverlayNotification
                {
                    SourcePlatform = "YouTube",
                    Type = NotificationType.Chat,
                    Username = username,
                    DisplayText = message,
                    Fragments = [new TextFragment { Text = message }],
                    UserColor = "#FFFFFF"
                },
                LiveChatMessageSnippet.Types.Type.SuperChatEvent => new OverlayNotification
                {
                    SourcePlatform = "YouTube",
                    Type = NotificationType.Reward,
                    Username = username,
                    DisplayText = string.IsNullOrWhiteSpace(snippet.SuperChatDetails?.AmountDisplayString)
                        ? "Super Chat"
                        : snippet.SuperChatDetails.AmountDisplayString,
                    SubText = string.IsNullOrWhiteSpace(snippet.SuperChatDetails?.UserComment)
                        ? message
                        : snippet.SuperChatDetails.UserComment
                },
                LiveChatMessageSnippet.Types.Type.NewSponsorEvent or LiveChatMessageSnippet.Types.Type.MemberMilestoneChatEvent => new OverlayNotification
                {
                    SourcePlatform = "YouTube",
                    Type = NotificationType.Subscribe,
                    Username = username,
                    DisplayText = "メンバーシップ",
                    SubText = string.IsNullOrWhiteSpace(message)
                        ? snippet.MemberMilestoneChatDetails?.UserComment ?? snippet.NewSponsorDetails?.MemberLevelName ?? string.Empty
                        : message
                },
                _ => null
            };
        }
    }
}
