using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Net.Client;
using Newtonsoft.Json.Linq;
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
            StatusCode = statusCode;
            RetryAfter = retryAfter;
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

    public class YouTubeLiveChatService
    {
        private static readonly HttpClient Http = new();
        private static readonly SocketsHttpHandler GrpcHttpHandler = new()
        {
            EnableMultipleHttp2Connections = true
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

        private readonly HashSet<string> _seenMessageIds = new();
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
        public bool IsConnected { get; private set; }
        public bool IsWaitingForBroadcast { get; private set; }

        public Task ConnectAsync(string accessToken)
        {
            return ConnectAsync(accessToken, checkImmediately: true, waitForObsSignalBeforePolling: false);
        }

        public async Task ConnectAsync(string accessToken, bool checkImmediately, bool waitForObsSignalBeforePolling)
        {
            if (string.IsNullOrWhiteSpace(accessToken))
                throw new InvalidOperationException("YouTube OAuth token がありません。");

            Disconnect();
            _accessToken = accessToken;
            _liveChatId = null;
            _resumePageToken = null;
            _seenMessageIds.Clear();
            _seenMessageOrder.Clear();
            _cts = new CancellationTokenSource();
            _broadcastPollingPending = false;

            _liveChatId = checkImmediately ? await ResolveLiveChatIdAsync(_cts.Token) : null;
            if (string.IsNullOrWhiteSpace(_liveChatId))
            {
                if (waitForObsSignalBeforePolling)
                {
                    EnterWaitingForBroadcast(startPollingImmediately: false, "YouTube 配信待機中です。OBS の配信開始検出後に30秒間隔の配信確認を開始します。");
                }
                else
                {
                    EnterWaitingForBroadcast(startPollingImmediately: true, "YouTube 配信が見つかりません。30秒間隔で配信確認を続行します。");
                }
                return;
            }

            IsConnected = true;
            _ = Task.Run(() => StreamLoopAsync(_cts.Token));
            LogService.Info($"YouTube Live Chat 接続開始: liveChatId={_liveChatId}");
        }

        public void StartBroadcastPolling()
        {
            if (!IsWaitingForBroadcast || !_broadcastPollingPending || _cts == null)
                return;

            _broadcastPollingPending = false;
            _ = Task.Run(() => WaitForBroadcastLoopAsync(_cts.Token));
            LogService.Info("YouTube 配信待機ポーリングを開始しました（30秒間隔）");
        }

        private void EnterWaitingForBroadcast(bool startPollingImmediately, string message)
        {
            IsConnected = false;
            IsWaitingForBroadcast = true;
            _liveChatId = null;
            _resumePageToken = null;
            _broadcastPollingPending = true;

            LogService.Info(message);
            WaitingForBroadcastStarted?.Invoke(this, new YouTubeWaitingForBroadcastEventArgs
            {
                Message = message
            });

            if (startPollingImmediately)
                StartBroadcastPolling();
        }

        public void Disconnect()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            _accessToken = null;
            _liveChatId = null;
            _resumePageToken = null;
            IsConnected = false;
            IsWaitingForBroadcast = false;
            _broadcastPollingPending = false;
        }

        private async Task WaitForBroadcastLoopAsync(CancellationToken cancellationToken)
        {
            long failureCount = 0;
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(BroadcastPollIntervalMs, cancellationToken);
                    try
                    {
                        _liveChatId = await ResolveLiveChatIdAsync(cancellationToken);
                        failureCount = 0;
                    }
                    catch (YouTubeApiException apiEx) when (apiEx.StatusCode != 401)
                    {
                        var delay = CalculateBackoffDelay(++failureCount, apiEx.RetryAfter);
                        LogService.Warning($"YouTube 配信確認失敗: {apiEx.StatusCode}。{delay.TotalSeconds:F1}秒後に再試行します");
                        await Task.Delay(delay, cancellationToken);
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(_liveChatId))
                    {
                        IsWaitingForBroadcast = false;
                        IsConnected = true;
                        _resumePageToken = null;
                        LogService.Info($"YouTube 配信を検出しました: liveChatId={_liveChatId}");
                        BroadcastDetected?.Invoke(this, EventArgs.Empty);
                        await StreamLoopAsync(cancellationToken);
                        return;
                    }
                    LogService.Info("YouTube 配信中のブロードキャストが見つかりません。再試行します...");
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
                    IsConnected = false;
                    ConnectionLost?.Invoke(this, new YouTubeConnectionLostEventArgs
                    {
                        IsUnauthorized = ex is YouTubeApiException apiEx && apiEx.StatusCode == 401,
                        Message = ex.Message
                    });
                }
            }
            finally
            {
                IsWaitingForBroadcast = false;
            }
        }

        private async Task<string> ResolveLiveChatIdAsync(CancellationToken cancellationToken)
        {
            // mine と broadcastStatus は同時指定不可。mine=true で自分の配信を全取得し、
            // lifeCycleStatus が live / liveStarting のものを選択する。
            string url = "https://www.googleapis.com/youtube/v3/liveBroadcasts" +
                         "?part=snippet,status&mine=true&maxResults=10";

            var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
            var res = await Http.SendAsync(req, cancellationToken);
            string json = await res.Content.ReadAsStringAsync(cancellationToken);
            if (!res.IsSuccessStatusCode)
                throw new YouTubeApiException((int)res.StatusCode, $"YouTube liveBroadcasts 取得失敗: {(int)res.StatusCode} {json}", GetRetryAfter(res));

            var obj = JObject.Parse(json);
            if (obj["items"] is not JArray items)
                return null;

            foreach (var item in items)
            {
                string status = item["status"]?["lifeCycleStatus"]?.ToString();
                if (status is "live" or "liveStarting")
                {
                    string liveChatId = item["snippet"]?["liveChatId"]?.ToString();
                    if (!string.IsNullOrEmpty(liveChatId))
                        return liveChatId;
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
                            CreateStreamRequest(),
                            headers: CreateGrpcHeaders(),
                            cancellationToken: cancellationToken);

                        LogService.Info($"YouTube Live Chat gRPC ストリーム開始: liveChatId={_liveChatId}");

                        bool receivedResponse = false;
                        while (await call.ResponseStream.MoveNext(cancellationToken))
                        {
                            receivedResponse = true;
                            failureCount = 0;

                            var response = call.ResponseStream.Current;
                            if (!string.IsNullOrWhiteSpace(response.NextPageToken))
                                _resumePageToken = response.NextPageToken;

                            foreach (var item in response.Items)
                            {
                                string messageId = item.Id;
                                if (string.IsNullOrEmpty(messageId) || _seenMessageIds.Contains(messageId))
                                    continue;

                                AddSeenMessageId(messageId);

                                var notification = BuildNotification(item);
                                if (notification != null)
                                    NotificationReceived?.Invoke(this, notification);
                            }

                            if (!string.IsNullOrWhiteSpace(response.OfflineAt))
                                throw new YouTubeApiException(410, $"YouTube 配信が終了しました: offlineAt={response.OfflineAt}");
                        }

                        if (!receivedResponse)
                            throw new YouTubeApiException(502, "YouTube Live Chat gRPC ストリームが応答なしで終了しました。");

                        var closedDelay = CalculateBackoffDelay(++failureCount, null);
                        LogService.Warning($"YouTube Live Chat gRPC ストリームが終了しました。{closedDelay.TotalSeconds:F1}秒後に再接続します");
                        await Task.Delay(closedDelay, cancellationToken);
                    }
                    catch (RpcException rpcEx) when (rpcEx.StatusCode != StatusCode.Cancelled)
                    {
                        var apiEx = ConvertToYouTubeApiException(rpcEx);
                        if (ShouldResumeBroadcastWait(apiEx))
                        {
                            EnterWaitingForBroadcast(startPollingImmediately: true, "YouTube 配信が終了または未検出になったため、30秒間隔の配信待機に戻ります。");
                            return;
                        }

                        if (ShouldBubbleStreamException(apiEx))
                            throw apiEx;

                        var delay = CalculateBackoffDelay(++failureCount, apiEx.RetryAfter);
                        LogService.Warning($"YouTube Live Chat gRPC 受信失敗: {apiEx.StatusCode}。{delay.TotalSeconds:F1}秒後に再接続します");
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
                    IsConnected = false;
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
                IsConnected = false;
            }
        }

        private LiveChatMessageListRequest CreateStreamRequest()
        {
            var request = new LiveChatMessageListRequest
            {
                LiveChatId = _liveChatId,
                ProfileImageSize = GrpcProfileImageSize,
                MaxResults = StreamMaxResults
            };

            if (!string.IsNullOrWhiteSpace(_resumePageToken))
                request.PageToken = _resumePageToken;

            request.Part.Add("snippet");
            request.Part.Add("authorDetails");
            return request;
        }

        private Metadata CreateGrpcHeaders()
        {
            return new Metadata
            {
                { "authorization", $"Bearer {_accessToken}" }
            };
        }

        private void AddSeenMessageId(string messageId)
        {
            if (!_seenMessageIds.Add(messageId))
                return;

            _seenMessageOrder.Enqueue(messageId);
            while (_seenMessageOrder.Count > MessageCacheSize)
            {
                string oldId = _seenMessageOrder.Dequeue();
                _seenMessageIds.Remove(oldId);
            }
        }

        private static TimeSpan CalculateBackoffDelay(long failureCount, TimeSpan? retryAfter)
        {
            if (retryAfter.HasValue && retryAfter.Value > TimeSpan.Zero)
                return retryAfter.Value;

            double exponent = Math.Min(failureCount, 62);
            double baseSeconds = Math.Pow(2, exponent);
            int jitterMs = Random.Shared.Next(0, 1000);
            double totalMs = (baseSeconds * 1000.0) + jitterMs;
            if (totalMs > int.MaxValue)
                totalMs = int.MaxValue;

            return TimeSpan.FromMilliseconds(totalMs);
        }

        private static TimeSpan? GetRetryAfter(HttpResponseMessage response)
        {
            if (response.Headers.RetryAfter?.Delta is TimeSpan delta && delta > TimeSpan.Zero)
                return delta;

            if (response.Headers.RetryAfter?.Date is DateTimeOffset retryAt)
            {
                var diff = retryAt - DateTimeOffset.UtcNow;
                if (diff > TimeSpan.Zero)
                    return diff;
            }

            return null;
        }

        private static bool ShouldBubbleStreamException(YouTubeApiException exception)
        {
            return exception.StatusCode is 401 or 403;
        }

        private static bool ShouldResumeBroadcastWait(YouTubeApiException exception)
        {
            return exception.StatusCode is 404 or 410 or 412;
        }

        private static YouTubeApiException ConvertToYouTubeApiException(RpcException exception)
        {
            int statusCode = exception.StatusCode switch
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
                return null;

            string username = item.AuthorDetails?.DisplayName ?? "YouTubeUser";
            string message = snippet.DisplayMessage ?? string.Empty;

            return snippet.Type switch
            {
                LiveChatMessageSnippet.Types.Type.TextMessageEvent => new OverlayNotification
                {
                    SourcePlatform = "YouTube",
                    Type = NotificationType.Chat,
                    Username = username,
                    DisplayText = message,
                    Fragments = new List<object> { new TextFragment { Text = message } },
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
