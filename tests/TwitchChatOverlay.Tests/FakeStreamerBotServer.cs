using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TwitchChatOverlay.Tests;

/// <summary>
/// Streamer.bot の WebSocket Server を最小限に模擬するテスト用サーバー。
/// Hello / Authenticate / Subscribe / Ping のハンドシェイクと Event 送信をサポートする。
/// </summary>
internal sealed class FakeStreamerBotServer : IAsyncDisposable
{
    private const string WebSocketGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly TaskCompletionSource<TcpClient> _clientAccepted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly string _salt = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
    private readonly string _challenge = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));

    private NetworkStream _stream;
    private Task _acceptLoop;

    private FakeStreamerBotServer(string password)
    {
        this.Password = password;
        this._listener = new TcpListener(IPAddress.Loopback, 0);
    }

    public string Password { get; }

    public int Port => ((IPEndPoint)this._listener.LocalEndpoint).Port;

    /// <summary>Subscribe で要求されたイベント一覧。</summary>
    public JsonObject ReceivedSubscriptions { get; private set; }

    /// <summary>クライアントから Authenticate が届いたか。</summary>
    public bool AuthenticateReceived { get; private set; }

    /// <summary>クライアントから送られた authentication 文字列（検証用）。</summary>
    public string ReceivedAuthentication { get; private set; }

    /// <summary>認証を通過したか。</summary>
    public bool Authenticated { get; private set; }

    /// <summary>認証を必須にするか（Streamer.bot の「Enforce」相当）。</summary>
    public bool EnforceAuthentication { get; set; }

    /// <summary>Subscribe リクエストの受信回数。</summary>
    public int SubscribeCount { get; private set; }

    /// <summary>クライアントから Pong が届いたか。</summary>
    public bool PongReceived { get; private set; }

    /// <summary>Subscribe に対する応答を差し替えるフック（失敗応答の再現用）。</summary>
    public Func<JsonObject, JsonObject> SubscribeResponseFactory { get; set; }

    /// <summary>クライアントからのメッセージをそのまま記録する（デバッグ用）。</summary>
    public List<JsonObject> ReceivedMessages { get; } = [];

    public static FakeStreamerBotServer Create(string password = "")
    {
        return new FakeStreamerBotServer(password);
    }

    public Task StartAsync()
    {
        this._listener.Start();
        this._acceptLoop = Task.Run(() => this.AcceptLoopAsync(this._cts.Token));
        return Task.CompletedTask;
    }

    public async Task<TcpClient> WaitForClientAsync(TimeSpan timeout)
    {
        var completed = await Task.WhenAny(this._clientAccepted.Task, Task.Delay(timeout));
        if (completed != this._clientAccepted.Task)
        {
            throw new TimeoutException("テスト用 Streamer.bot サーバーにクライアントが接続しませんでした。");
        }

        return await this._clientAccepted.Task;
    }

    public async Task SendEventAsync(string source, string type, JsonObject data)
    {
        var payload = new JsonObject
        {
            ["timeStamp"] = DateTime.Now.ToString("o"),
            ["event"] = new JsonObject
            {
                ["source"] = source,
                ["type"] = type,
            },
            ["data"] = data,
        };

        await this.SendRawAsync(payload.ToJsonString());
    }

    public Task SendPingAsync()
    {
        return this.SendRawAsync("{\"request\":\"Ping\"}");
    }

    public async Task SendRawAsync(string json)
    {
        var stream = this._stream ?? throw new InvalidOperationException("クライアントがまだ接続されていません。");
        await this._sendLock.WaitAsync();
        try
        {
            await WriteFrameAsync(stream, Encoding.UTF8.GetBytes(json));
        }
        finally
        {
            _ = this._sendLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        this._cts.Cancel();
        this._listener.Stop();
        try
        {
            if (this._stream != null)
            {
                this._stream.Dispose();
            }

            if (this._acceptLoop != null)
            {
                await this._acceptLoop.ConfigureAwait(false);
            }
        }
        catch
        {
            // 破棄時の例外は無視
        }

        this._cts.Dispose();
        this._sendLock.Dispose();
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            var client = await this._listener.AcceptTcpClientAsync(cancellationToken);
            this._stream = client.GetStream();
            await HandshakeAsync(this._stream, cancellationToken);
            _ = this._clientAccepted.TrySetResult(client);

            await this.SendHelloAsync();

            while (!cancellationToken.IsCancellationRequested)
            {
                var frame = await ReadFrameAsync(this._stream, cancellationToken);
                if (frame == null)
                {
                    break;
                }

                using var document = JsonDocument.Parse(frame);
                var message = JsonNode.Parse(document.RootElement.GetRawText())!.AsObject();
                this.ReceivedMessages.Add(message);
                await this.HandleMessageAsync(message);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _ = this._clientAccepted.TrySetException(ex);
        }
    }

    private async Task HandleMessageAsync(JsonObject message)
    {
        var request = message["request"]?.GetValue<string>();
        var id = message["id"]?.GetValue<string>();

        switch (request)
        {
            case "Authenticate":
                this.AuthenticateReceived = true;
                this.ReceivedAuthentication = message["authentication"]?.GetValue<string>();
                var expected = ComputeAuthentication(this.Password, this._salt, this._challenge);
                if (string.Equals(expected, this.ReceivedAuthentication, StringComparison.Ordinal))
                {
                    this.Authenticated = true;
                    await this.SendRawAsync(new JsonObject { ["id"] = id, ["status"] = "ok" }.ToJsonString());
                }
                else
                {
                    await this.SendRawAsync(new JsonObject
                    {
                        ["id"] = id,
                        ["status"] = "error",
                        ["error"] = "Authentication failed.",
                    }.ToJsonString());
                }

                break;

            case "GetInfo":
                if (this.EnforceAuthentication && !this.Authenticated)
                {
                    await this.SendRawAsync(new JsonObject
                    {
                        ["id"] = id,
                        ["status"] = "error",
                        ["error"] = "Not authenticated.",
                    }.ToJsonString());
                    break;
                }

                var info = new JsonObject
                {
                    ["id"] = id,
                    ["status"] = "ok",
                    ["info"] = new JsonObject
                    {
                        ["name"] = "Streamer.bot",
                        ["version"] = "1.0.4",
                        ["os"] = "Windows",
                        ["instanceUuid"] = "00000000-0000-0000-0000-000000000000",
                    },
                };
                await this.SendRawAsync(info.ToJsonString());
                break;

            case "Subscribe":
                if (this.EnforceAuthentication && !this.Authenticated)
                {
                    await this.SendRawAsync(new JsonObject
                    {
                        ["id"] = id,
                        ["status"] = "error",
                        ["error"] = "Not authenticated.",
                    }.ToJsonString());
                    break;
                }

                this.SubscribeCount++;
                this.ReceivedSubscriptions = message["events"]?.DeepClone().AsObject();
                var response = this.SubscribeResponseFactory != null
                    ? this.SubscribeResponseFactory(message)
                    : new JsonObject { ["id"] = id, ["status"] = "ok" };
                await this.SendRawAsync(response.ToJsonString());
                break;

            case "Ping":
                await this.SendRawAsync("{\"request\":\"Pong\"}");
                break;

            case "Pong":
                this.PongReceived = true;
                break;

            default:
                // GetEvents などの未対応リクエストには ok を返す
                if (!string.IsNullOrEmpty(id))
                {
                    await this.SendRawAsync(new JsonObject { ["id"] = id, ["status"] = "ok" }.ToJsonString());
                }

                break;
        }
    }

    private Task SendHelloAsync()
    {
        var hello = new JsonObject
        {
            ["timestamp"] = DateTime.Now.ToString("o"),
            ["session"] = Guid.NewGuid().ToString(),
            ["request"] = "Hello",
            ["info"] = new JsonObject
            {
                ["instanceId"] = "00000000-0000-0000-0000-000000000000",
                ["name"] = "Streamer.bot",
                ["version"] = "1.0.4",
                ["os"] = "Windows",
                ["mode"] = "ui",
                ["source"] = "websocketServer",
            },
        };

        if (!string.IsNullOrEmpty(this.Password))
        {
            hello["authentication"] = new JsonObject
            {
                ["salt"] = this._salt,
                ["challenge"] = this._challenge,
            };
        }

        return this.SendRawAsync(hello.ToJsonString());
    }

    private static string ComputeAuthentication(string password, string salt, string challenge)
    {
        var secretHash = SHA256.HashData(Encoding.UTF8.GetBytes(password + salt));
        var secret = Convert.ToBase64String(secretHash);
        var authHash = SHA256.HashData(Encoding.UTF8.GetBytes(secret + challenge));
        return Convert.ToBase64String(authHash);
    }

    private static async Task HandshakeAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var requestBuilder = new StringBuilder();
        var buffer = new byte[1024];
        while (!requestBuilder.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                throw new InvalidOperationException("WebSocket ハンドシェイクが途中で切断されました。");
            }

            _ = requestBuilder.Append(Encoding.ASCII.GetString(buffer, 0, read));
        }

        var request = requestBuilder.ToString();
        var keyLine = request
            .Split("\r\n")
            .FirstOrDefault(line => line.StartsWith("Sec-WebSocket-Key:", StringComparison.OrdinalIgnoreCase));
        if (keyLine == null)
        {
            throw new InvalidOperationException("Sec-WebSocket-Key ヘッダーがありません。");
        }

        var key = keyLine["Sec-WebSocket-Key:".Length..].Trim();
        var accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key + WebSocketGuid)));
        var response = "HTTP/1.1 101 Switching Protocols\r\n"
            + "Upgrade: websocket\r\n"
            + "Connection: Upgrade\r\n"
            + $"Sec-WebSocket-Accept: {accept}\r\n\r\n";
        var responseBytes = Encoding.ASCII.GetBytes(response);
        await stream.WriteAsync(responseBytes, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static async Task WriteFrameAsync(NetworkStream stream, byte[] payload)
    {
        using var frame = new MemoryStream();
        frame.WriteByte(0x81); // FIN + text
        if (payload.Length < 126)
        {
            frame.WriteByte((byte)payload.Length);
        }
        else if (payload.Length <= ushort.MaxValue)
        {
            frame.WriteByte(126);
            frame.WriteByte((byte)(payload.Length >> 8));
            frame.WriteByte((byte)(payload.Length & 0xFF));
        }
        else
        {
            frame.WriteByte(127);
            var length = (long)payload.Length;
            for (var i = 7; i >= 0; i--)
            {
                frame.WriteByte((byte)((length >> (8 * i)) & 0xFF));
            }
        }

        frame.Write(payload, 0, payload.Length);
        await stream.WriteAsync(frame.ToArray());
        await stream.FlushAsync();
    }

    private static async Task<string> ReadFrameAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var header = await ReadExactAsync(stream, 2, cancellationToken);
        if (header == null)
        {
            return null;
        }

        var opcode = header[0] & 0x0F;
        if (opcode == 0x8)
        {
            return null; // close
        }

        var masked = (header[1] & 0x80) != 0;
        long length = header[1] & 0x7F;
        if (length == 126)
        {
            var extended = await ReadExactAsync(stream, 2, cancellationToken);
            length = (extended[0] << 8) | extended[1];
        }
        else if (length == 127)
        {
            var extended = await ReadExactAsync(stream, 8, cancellationToken);
            length = 0;
            for (var i = 0; i < 8; i++)
            {
                length = (length << 8) | extended[i];
            }
        }

        var mask = masked ? await ReadExactAsync(stream, 4, cancellationToken) : null;
        var payload = length == 0 ? [] : await ReadExactAsync(stream, (int)length, cancellationToken);
        if (mask != null)
        {
            for (var i = 0; i < payload.Length; i++)
            {
                payload[i] ^= mask[i % 4];
            }
        }

        if (opcode == 0x9)
        {
            return null; // ping は未使用
        }

        return Encoding.UTF8.GetString(payload);
    }

    private static async Task<byte[]> ReadExactAsync(NetworkStream stream, int count, CancellationToken cancellationToken)
    {
        var buffer = new byte[count];
        var offset = 0;
        while (offset < count)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, count - offset), cancellationToken);
            if (read == 0)
            {
                return null;
            }

            offset += read;
        }

        return buffer;
    }
}
