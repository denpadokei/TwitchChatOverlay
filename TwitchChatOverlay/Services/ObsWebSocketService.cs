using System;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace TwitchChatOverlay.Services
{
    public class ObsStreamingStateChangedEventArgs : EventArgs
    {
        public bool IsStreaming { get; init; }
    }

    public class ObsWebSocketService
    {
        private ClientWebSocket _webSocket;
        private CancellationTokenSource _cts;
        private readonly SemaphoreSlim _connectLock = new(1, 1);

        public bool IsConnected { get; private set; }
        public bool IsStreaming { get; private set; }

        public event EventHandler<ObsStreamingStateChangedEventArgs> StreamingStateChanged;

        public async Task<bool> ConnectAsync(string host, int port, string password, CancellationToken cancellationToken = default)
        {
            await _connectLock.WaitAsync(cancellationToken);
            try
            {
                if (IsConnected)
                    return true;

                Disconnect();
                _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                _webSocket = new ClientWebSocket();

                string targetHost = string.IsNullOrWhiteSpace(host) ? "127.0.0.1" : host;
                int targetPort = port > 0 ? port : 4455;
                await _webSocket.ConnectAsync(new Uri($"ws://{targetHost}:{targetPort}"), _cts.Token);

                var hello = await ReceiveJsonAsync(_webSocket, _cts.Token);
                int op = hello["op"]?.Value<int>() ?? -1;
                if (op != 0)
                    throw new Exception("OBS Hello メッセージを受信できませんでした。");

                int rpcVersion = hello["d"]?["rpcVersion"]?.Value<int>() ?? 1;
                string authentication = BuildAuthentication(password, hello["d"]?["authentication"]);

                var identifyData = new JObject
                {
                    ["rpcVersion"] = rpcVersion
                };
                if (!string.IsNullOrEmpty(authentication))
                    identifyData["authentication"] = authentication;

                await SendJsonAsync(_webSocket, new JObject
                {
                    ["op"] = 1,
                    ["d"] = identifyData
                }, _cts.Token);

                var identified = await ReceiveJsonAsync(_webSocket, _cts.Token);
                int identifiedOp = identified["op"]?.Value<int>() ?? -1;
                if (identifiedOp != 2)
                    throw new Exception($"OBS Identify に失敗しました。op={identifiedOp}");

                IsConnected = true;
                _ = Task.Run(() => ReceiveLoopAsync(_cts.Token));
                LogService.Info("OBS WebSocket 接続完了");
                return true;
            }
            catch (Exception ex)
            {
                LogService.Warning("OBS WebSocket 接続失敗", ex);
                Disconnect();
                return false;
            }
            finally
            {
                _connectLock.Release();
            }
        }

        public void Disconnect()
        {
            try
            {
                _cts?.Cancel();
            }
            catch
            {
            }

            _webSocket?.Dispose();
            _webSocket = null;
            _cts?.Dispose();
            _cts = null;
            IsConnected = false;
            IsStreaming = false;
        }

        private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested && _webSocket?.State == WebSocketState.Open)
                {
                    JObject msg = await ReceiveJsonAsync(_webSocket, cancellationToken);
                    int op = msg["op"]?.Value<int>() ?? -1;
                    if (op != 5)
                        continue;

                    string eventType = msg["d"]?["eventType"]?.ToString();
                    if (!string.Equals(eventType, "StreamStateChanged", StringComparison.Ordinal))
                        continue;

                    bool isStreaming = msg["d"]?["eventData"]?["outputActive"]?.Value<bool>() ?? false;
                    if (IsStreaming == isStreaming)
                        continue;

                    IsStreaming = isStreaming;
                    StreamingStateChanged?.Invoke(this, new ObsStreamingStateChangedEventArgs { IsStreaming = isStreaming });
                }
            }
            catch (OperationCanceledException)
            {
                LogService.Info("OBS WebSocket 受信ループ終了（キャンセル）");
            }
            catch (Exception ex)
            {
                LogService.Warning("OBS WebSocket 受信ループエラー", ex);
            }
            finally
            {
                IsConnected = false;
            }
        }

        private static async Task SendJsonAsync(ClientWebSocket socket, JObject payload, CancellationToken cancellationToken)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(payload.ToString(Newtonsoft.Json.Formatting.None));
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
                    throw new Exception("OBS WebSocket がクローズされました。");

                builder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                if (result.EndOfMessage)
                    return JObject.Parse(builder.ToString());
            }
        }

        private static string BuildAuthentication(string password, JToken authToken)
        {
            if (authToken == null)
                return null;

            string challenge = authToken["challenge"]?.ToString();
            string salt = authToken["salt"]?.ToString();
            if (string.IsNullOrEmpty(challenge) || string.IsNullOrEmpty(salt))
                return null;

            string basePassword = password ?? string.Empty;
            byte[] secretHash = SHA256.HashData(Encoding.UTF8.GetBytes(basePassword + salt));
            string secret = Convert.ToBase64String(secretHash);
            byte[] authHash = SHA256.HashData(Encoding.UTF8.GetBytes(secret + challenge));
            return Convert.ToBase64String(authHash);
        }
    }
}
