using Newtonsoft.Json.Linq;
using System;
using System.Net.WebSockets;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TwitchChatOverlay.Services
{
    public enum ObsConnectionFailureReason
    {
        None,
        Unavailable,
        AuthenticationFailed,
        ConfigurationError,
        ProtocolError,
        Unknown,
    }

    public sealed class ObsConnectionResult
    {
        public bool IsConnected { get; init; }

        public ObsConnectionFailureReason FailureReason { get; init; }

        public string Message { get; init; }

        public bool ShouldRetry => this.FailureReason == ObsConnectionFailureReason.Unavailable;

        public static ObsConnectionResult Success()
        {
            return new ObsConnectionResult
            {
                IsConnected = true,
                FailureReason = ObsConnectionFailureReason.None,
            };
        }
    }

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

        public event EventHandler ConnectionStateChanged;
        public event EventHandler<ObsStreamingStateChangedEventArgs> StreamingStateChanged;

        public async Task<ObsConnectionResult> ConnectAsync(string host, int port, string password, CancellationToken cancellationToken = default)
        {
            await this._connectLock.WaitAsync(cancellationToken);
            try
            {
                if (this.IsConnected)
                {
                    return ObsConnectionResult.Success();
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

                this.SetConnectionState(true);
                _ = Task.Run(() => this.ReceiveLoopAsync(this._cts.Token));
                LogService.Info("OBS WebSocket 接続完了");
                return ObsConnectionResult.Success();
            }
            catch (Exception ex)
            {
                LogService.Warning("OBS WebSocket 接続失敗", ex);
                this.Disconnect();
                return CreateFailureResult(ex);
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
            var wasStreaming = this.IsStreaming;
            this.IsStreaming = false;
            if (wasStreaming)
            {
                StreamingStateChanged?.Invoke(this, new ObsStreamingStateChangedEventArgs { IsStreaming = false });
            }

            this.SetConnectionState(false);
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
                this.SetConnectionState(false);
            }
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
                    throw CreateCloseException(result.CloseStatus, result.CloseStatusDescription);
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

        private static ObsConnectionResult CreateFailureResult(Exception ex)
        {
            if (ex is ObsConnectionException obsConnectionException)
            {
                return new ObsConnectionResult
                {
                    FailureReason = obsConnectionException.FailureReason,
                    Message = obsConnectionException.Message,
                };
            }

            var socketException = FindSocketException(ex);
            if (socketException != null && IsRetryableSocketError(socketException.SocketErrorCode))
            {
                return new ObsConnectionResult
                {
                    FailureReason = ObsConnectionFailureReason.Unavailable,
                    Message = "OBS が見つかりません。10 秒後に再試行します。",
                };
            }

            if (ex is OperationCanceledException)
            {
                return new ObsConnectionResult
                {
                    FailureReason = ObsConnectionFailureReason.Unknown,
                    Message = "OBS 接続がキャンセルされました。",
                };
            }

            return new ObsConnectionResult
            {
                FailureReason = ObsConnectionFailureReason.Unknown,
                Message = $"OBS接続エラー: {ex.Message}",
            };
        }

        private static Exception CreateCloseException(WebSocketCloseStatus? closeStatus, string closeDescription)
        {
            var closeCode = closeStatus.HasValue ? (int)closeStatus.Value : 0;
            var description = string.IsNullOrWhiteSpace(closeDescription) ? "詳細不明" : closeDescription;

            return closeCode switch
            {
                4005 => new ObsConnectionException(ObsConnectionFailureReason.AuthenticationFailed, "OBS WebSocket の認証に失敗しました。パスワードを確認してください。"),
                4006 => new ObsConnectionException(ObsConnectionFailureReason.ConfigurationError, "OBS WebSocket の認証情報が不足しています。設定を確認してください。"),
                4007 => new ObsConnectionException(ObsConnectionFailureReason.ConfigurationError, "OBS WebSocket の識別情報が不正です。設定を確認してください。"),
                4008 => new ObsConnectionException(ObsConnectionFailureReason.ProtocolError, "OBS WebSocket の RPC バージョンが互換ではありません。OBS 側の設定を確認してください。"),
                4009 => new ObsConnectionException(ObsConnectionFailureReason.AuthenticationFailed, "OBS WebSocket の認証に失敗しました。パスワードを確認してください。"),
                _ => new ObsConnectionException(ObsConnectionFailureReason.ProtocolError, $"OBS WebSocket がクローズされました。code={closeCode}, reason={description}"),
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

        private sealed class ObsConnectionException : Exception
        {
            public ObsConnectionException(ObsConnectionFailureReason failureReason, string message)
                : base(message)
            {
                this.FailureReason = failureReason;
            }

            public ObsConnectionFailureReason FailureReason { get; }
        }
    }
}
