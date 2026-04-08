using Newtonsoft.Json.Linq;
using System;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

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
            await this._connectLock.WaitAsync(cancellationToken);
            try
            {
                if (this.IsConnected)
                {
                    return true;
                }

                this.Disconnect();
                this._cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                this._webSocket = new ClientWebSocket();

                var targetHost = string.IsNullOrWhiteSpace(host) ? "127.0.0.1" : host;
                var targetPort = port > 0 ? port : 4455;
                await this._webSocket.ConnectAsync(new Uri($"ws://{targetHost}:{targetPort}"), this._cts.Token);

                var hello = await ReceiveJsonAsync(this._webSocket, this._cts.Token);
                var op = hello["op"]?.Value<int>() ?? -1;
                if (op != 0)
                {
                    throw new Exception("OBS Hello メッセージを受信できませんでした。");
                }

                var rpcVersion = hello["d"]?["rpcVersion"]?.Value<int>() ?? 1;
                var authentication = BuildAuthentication(password, hello["d"]?["authentication"]);

                var identifyData = new JObject
                {
                    ["rpcVersion"] = rpcVersion
                };
                if (!string.IsNullOrEmpty(authentication))
                {
                    identifyData["authentication"] = authentication;
                }

                await SendJsonAsync(this._webSocket, new JObject
                {
                    ["op"] = 1,
                    ["d"] = identifyData
                }, this._cts.Token);

                var identified = await ReceiveJsonAsync(this._webSocket, this._cts.Token);
                var identifiedOp = identified["op"]?.Value<int>() ?? -1;
                if (identifiedOp != 2)
                {
                    throw new Exception($"OBS Identify に失敗しました。op={identifiedOp}");
                }

                this.IsConnected = true;
                _ = Task.Run(() => this.ReceiveLoopAsync(this._cts.Token));
                LogService.Info("OBS WebSocket 接続完了");
                return true;
            }
            catch (Exception ex)
            {
                LogService.Warning("OBS WebSocket 接続失敗", ex);
                this.Disconnect();
                return false;
            }
            finally
            {
                _ = this._connectLock.Release();
            }
        }

        public void Disconnect()
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
            this.IsConnected = false;
            this.IsStreaming = false;
        }

        private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var socket = this._webSocket;
                    if (socket == null || socket.State != WebSocketState.Open)
                    {
                        break;
                    }

                    var msg = await ReceiveJsonAsync(socket, cancellationToken);
                    var op = msg["op"]?.Value<int>() ?? -1;
                    if (op != 5)
                    {
                        continue;
                    }

                    var eventType = msg["d"]?["eventType"]?.ToString();
                    if (!string.Equals(eventType, "StreamStateChanged", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var isStreaming = msg["d"]?["eventData"]?["outputActive"]?.Value<bool>() ?? false;
                    if (this.IsStreaming == isStreaming)
                    {
                        continue;
                    }

                    this.IsStreaming = isStreaming;
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
                this.IsConnected = false;
            }
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
                    throw new Exception("OBS WebSocket がクローズされました。");
                }

                _ = builder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                if (result.EndOfMessage)
                {
                    return JObject.Parse(builder.ToString());
                }
            }
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
    }
}
