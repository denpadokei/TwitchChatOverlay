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

        public YouTubeApiException(int statusCode, string message) : base(message)
        {
            StatusCode = statusCode;
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

        private readonly HashSet<string> _seenMessageIds = new();
        private CancellationTokenSource _cts;
        private string _accessToken;
        private string _liveChatId;

        public event EventHandler<OverlayNotification> NotificationReceived;
        public event EventHandler<YouTubeConnectionLostEventArgs> ConnectionLost;
        public bool IsConnected { get; private set; }

        public async Task ConnectAsync(string accessToken, string channelName)
        {
            if (string.IsNullOrWhiteSpace(accessToken))
                throw new InvalidOperationException("YouTube OAuth token がありません。");

            Disconnect();
            _accessToken = accessToken;
            _seenMessageIds.Clear();
            _cts = new CancellationTokenSource();

            _liveChatId = await ResolveLiveChatIdAsync(_cts.Token);
            if (string.IsNullOrWhiteSpace(_liveChatId))
                throw new Exception("配信中の liveChatId を取得できませんでした。YouTube Live を開始してから再試行してください。");

            IsConnected = true;
            _ = Task.Run(() => PollLoopAsync(_cts.Token));
            LogService.Info($"YouTube Live Chat 接続開始: liveChatId={_liveChatId}, channel={channelName}");
        }

        public void Disconnect()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            IsConnected = false;
        }

        private async Task<string> ResolveLiveChatIdAsync(CancellationToken cancellationToken)
        {
            string url = "https://www.googleapis.com/youtube/v3/liveBroadcasts" +
                         "?part=snippet&broadcastStatus=active&mine=true&maxResults=1";

            var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
            var res = await Http.SendAsync(req, cancellationToken);
            string json = await res.Content.ReadAsStringAsync(cancellationToken);
            if (!res.IsSuccessStatusCode)
                throw new YouTubeApiException((int)res.StatusCode, $"YouTube liveBroadcasts 取得失敗: {(int)res.StatusCode} {json}");

            var obj = JObject.Parse(json);
            return obj["items"]?[0]?["snippet"]?["liveChatId"]?.ToString();
        }

        private async Task PollLoopAsync(CancellationToken cancellationToken)
        {
            string pageToken = null;
            int intervalMs = 5000;

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
                            throw new YouTubeApiException(statusCode, $"YouTube liveChat poll失敗: {statusCode} {json}");

                        LogService.Warning($"YouTube liveChat poll失敗: {statusCode} {json}");
                        await Task.Delay(intervalMs, cancellationToken);
                        continue;
                    }

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

                        _seenMessageIds.Add(messageId);
                        if (_seenMessageIds.Count > 2000)
                            _seenMessageIds.Clear();

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
