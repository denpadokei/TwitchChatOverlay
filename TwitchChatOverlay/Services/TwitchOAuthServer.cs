using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace TwitchChatOverlay.Services
{
    public class DeviceTokenResponse
    {
        [JsonProperty("access_token")]
        public string AccessToken { get; set; }

        [JsonProperty("refresh_token")]
        public string RefreshToken { get; set; }

        [JsonProperty("scope")]
        public string[] Scope { get; set; }
    }

    public class TwitchOAuthServer
    {
        private readonly string _clientId;
        private readonly string[] _scopes =
        {
            "user:read:chat",
            "channel:read:redemptions",
            "moderator:read:followers",
            "channel:read:subscriptions",
            "channel:read:hype_train"
        };

        private static readonly HttpClient _http = new();

        public TwitchOAuthServer(string clientId)
        {
            _clientId = clientId;
        }

        /// <summary>
        /// リフレッシュトークンを使ってアクセストークンを更新する。
        /// </summary>
        public async Task<DeviceTokenResponse> RefreshTokenAsync(string refreshToken)
        {
            var request = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("client_id", _clientId),
                new KeyValuePair<string, string>("grant_type", "refresh_token"),
                new KeyValuePair<string, string>("refresh_token", refreshToken)
            });

            var response = await _http.PostAsync("https://id.twitch.tv/oauth2/token", request);
            var json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                var baseMessage = $"トークンリフレッシュ失敗 (StatusCode: {(int)response.StatusCode})";
                try
                {
                    var obj = JObject.Parse(json);
                    var error = (string?)obj["error"];
                    var message = (string?)obj["message"];

                    if (!string.IsNullOrEmpty(error) || !string.IsNullOrEmpty(message))
                    {
                        var detail = string.Join(": ", new[] { error, message }.Where(s => !string.IsNullOrEmpty(s)));
                        baseMessage = $"{baseMessage}: {detail}";
                    }
                }
                catch (JsonException)
                {
                    // 応答本文がJSONでない場合は、ステータスコードのみを使用する
                }

                throw new Exception(baseMessage);
            }
            return JsonConvert.DeserializeObject<DeviceTokenResponse>(json)
                ?? throw new Exception("リフレッシュレスポンスの解析に失敗しました");
        }

        /// <summary>
        /// Device Authorization Grant フローでTwitch認可を行い、アクセストークンを返す。
        /// リダイレクトURLは不要。ユーザーはブラウザでコードを入力するだけ。
        /// </summary>
        /// <param name="onUserCode">ユーザーに表示すべきコードを受け取るコールバック</param>
        /// <param name="cancellationToken"></param>
        public async Task<DeviceTokenResponse> AuthorizeAsync(
            Action<string, string> onUserCode,
            CancellationToken cancellationToken = default)
        {
            // Step 1: デバイス認可リクエスト
            var deviceRequest = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("client_id", _clientId),
                new KeyValuePair<string, string>("scopes", string.Join(" ", _scopes))
            });

            var deviceResponse = await _http.PostAsync(
                "https://id.twitch.tv/oauth2/device", deviceRequest, cancellationToken);

            var deviceJson = await deviceResponse.Content.ReadAsStringAsync(cancellationToken);
            if (!deviceResponse.IsSuccessStatusCode)
                throw new Exception($"デバイス認可リクエスト失敗: {deviceJson}");

            var deviceData = JObject.Parse(deviceJson);
            string deviceCode = deviceData["device_code"]?.Value<string>()
                ?? throw new Exception("device_code が取得できませんでした");
            string userCode = deviceData["user_code"]?.Value<string>() ?? "";
            string verificationUri = deviceData["verification_uri"]?.Value<string>()
                ?? "https://www.twitch.tv/activate";
            // 完全なURIがあればそちらを使う（コードが自動入力される）
            string verificationUriComplete = deviceData["verification_uri_complete"]?.Value<string>()
                ?? verificationUri;
            int interval = deviceData["interval"]?.Value<int>() ?? 5;
            int expiresIn = deviceData["expires_in"]?.Value<int>() ?? 1800;

            // UIにコードを通知
            onUserCode(userCode, verificationUri);

            // ブラウザを開く
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = verificationUriComplete,
                    UseShellExecute = true
                });
            }
            catch { /* ブラウザが開けなくても継続 */ }

            // Step 2: トークンが発行されるまでポーリング
            var deadline = DateTime.UtcNow.AddSeconds(expiresIn);
            while (DateTime.UtcNow < deadline)
            {
                await Task.Delay(TimeSpan.FromSeconds(interval), cancellationToken);

                var pollRequest = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("client_id", _clientId),
                    new KeyValuePair<string, string>("device_code", deviceCode),
                    new KeyValuePair<string, string>("grant_type",
                        "urn:ietf:params:oauth:grant-type:device_code")
                });

                var pollResponse = await _http.PostAsync(
                    "https://id.twitch.tv/oauth2/token", pollRequest, cancellationToken);
                var pollJson = await pollResponse.Content.ReadAsStringAsync(cancellationToken);

                if (pollResponse.IsSuccessStatusCode)
                    return JsonConvert.DeserializeObject<DeviceTokenResponse>(pollJson);

                var errData = JObject.Parse(pollJson);
                string msg = errData["message"]?.Value<string>()
                    ?? errData["error"]?.Value<string>() ?? "";

                switch (msg)
                {
                    case "authorization_pending":
                        continue;
                    case "slow_down":
                        interval += 5;
                        continue;
                    default:
                        throw new Exception($"認可エラー: {msg}");
                }
            }

            throw new Exception("認可がタイムアウトしました（30分以内に認可してください）");
        }
    }
}