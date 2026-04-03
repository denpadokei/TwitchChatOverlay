using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using TwitchChatOverlay.Models;

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

    public class YouTubeLiveChatService
    {
        private static readonly HttpClient Http = new();
        private const int BroadcastPollIntervalMs = 30_000;
        private const int MessageCacheSize = 5000;

        private readonly HashSet<string> _seenMessageIds = new();
        private readonly Queue<string> _seenMessageOrder = new();
        private CancellationTokenSource _cts;
        private string _accessToken;
        private string _liveChatId;
        private bool _broadcastPollingPending;

        public event EventHandler<OverlayNotification> NotificationReceived;
        public event EventHandler<YouTubeConnectionLostEventArgs> ConnectionLost;
        public event EventHandler BroadcastDetected;
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
            _seenMessageIds.Clear();
            _seenMessageOrder.Clear();
            _cts = new CancellationTokenSource();
            _broadcastPollingPending = false;

            _liveChatId = checkImmediately ? await ResolveLiveChatIdAsync(_cts.Token) : null;
            if (string.IsNullOrWhiteSpace(_liveChatId))
            {
                IsWaitingForBroadcast = true;
                _broadcastPollingPending = true;

                if (waitForObsSignalBeforePolling)
                {
                    LogService.Info("YouTube 配信待機中です。OBS の配信開始検出後に30秒ポーリングを開始します。");
                }
                else
                {
                    LogService.Info("YouTube 配信が見つかりません。30秒ごとの配信確認を開始します。");
                    StartBroadcastPolling();
                }
                return;
            }

            IsConnected = true;
            _ = Task.Run(() => PollLoopAsync(_cts.Token));
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

        public void Disconnect()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
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
                        LogService.Info($"YouTube 配信を検出しました: liveChatId={_liveChatId}");
                        BroadcastDetected?.Invoke(this, EventArgs.Empty);
                        await PollLoopAsync(cancellationToken);
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

        private async Task PollLoopAsync(CancellationToken cancellationToken)
        {
            string pageToken = null;
            int intervalMs = 5000;
            long failureCount = 0;

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    string url = "https://www.googleapis.com/youtube/v3/liveChat/messages" +
                                 $"?liveChatId={Uri.EscapeDataString(_liveChatId)}" +
                                 "&part=snippet,authorDetails" +
                                 "&maxResults=200";
                    if (!string.IsNullOrEmpty(pageToken))
                        url += $"&pageToken={Uri.EscapeDataString(pageToken)}";

                    var req = new HttpRequestMessage(HttpMethod.Get, url);
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
                    var res = await Http.SendAsync(req, cancellationToken);
                    string json = await res.Content.ReadAsStringAsync(cancellationToken);

                    if (!res.IsSuccessStatusCode)
                    {
                        int statusCode = (int)res.StatusCode;
                        if (statusCode == 401)
                            throw new YouTubeApiException(statusCode, $"YouTube liveChat poll失敗: {statusCode} {json}", GetRetryAfter(res));

                        var delay = CalculateBackoffDelay(++failureCount, GetRetryAfter(res));
                        LogService.Warning($"YouTube liveChat poll失敗: {statusCode}。{delay.TotalSeconds:F1}秒後に再試行します");
                        await Task.Delay(delay, cancellationToken);
                        continue;
                    }

                    failureCount = 0;

                    var obj = JObject.Parse(json);
                    pageToken = obj["nextPageToken"]?.ToString();
                    intervalMs = obj["pollingIntervalMillis"]?.Value<int>() ?? 5000;

                    if (obj["items"] is not JArray items)
                    {
                        await Task.Delay(intervalMs, cancellationToken);
                        continue;
                    }

                    foreach (var item in items)
                    {
                        string messageId = item["id"]?.ToString();
                        if (string.IsNullOrEmpty(messageId) || _seenMessageIds.Contains(messageId))
                            continue;

                        AddSeenMessageId(messageId);

                        var notification = BuildNotification(item);
                        if (notification != null)
                            NotificationReceived?.Invoke(this, notification);
                    }

                    await Task.Delay(intervalMs, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                LogService.Info("YouTube Live Chat ポーリング終了（キャンセル）");
            }
            catch (Exception ex)
            {
                LogService.Error("YouTube Live Chat ポーリングエラー", ex);

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

        private static OverlayNotification BuildNotification(JToken item)
        {
            string type = item["snippet"]?["type"]?.ToString() ?? "textMessageEvent";
            string username = item["authorDetails"]?["displayName"]?.ToString() ?? "YouTubeUser";
            string message = item["snippet"]?["displayMessage"]?.ToString() ?? string.Empty;

            return type switch
            {
                "textMessageEvent" => new OverlayNotification
                {
                    SourcePlatform = "YouTube",
                    Type = NotificationType.Chat,
                    Username = username,
                    DisplayText = message,
                    Fragments = new List<object> { new TextFragment { Text = message } },
                    UserColor = "#FFFFFF"
                },
                "superChatEvent" => new OverlayNotification
                {
                    SourcePlatform = "YouTube",
                    Type = NotificationType.Reward,
                    Username = username,
                    DisplayText = "Super Chat",
                    SubText = message
                },
                "newSponsorEvent" or "memberMilestoneChatEvent" => new OverlayNotification
                {
                    SourcePlatform = "YouTube",
                    Type = NotificationType.Subscribe,
                    Username = username,
                    DisplayText = "メンバーシップ",
                    SubText = message
                },
                _ => null
            };
        }
    }
}
