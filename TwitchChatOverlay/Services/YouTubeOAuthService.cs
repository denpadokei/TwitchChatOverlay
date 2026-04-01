using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace TwitchChatOverlay.Services
{
    public class YouTubeTokenResponse
    {
        [JsonProperty("access_token")]
        public string AccessToken { get; set; }

        [JsonProperty("refresh_token")]
        public string RefreshToken { get; set; }

        [JsonProperty("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonProperty("scope")]
        public string Scope { get; set; }

        [JsonProperty("token_type")]
        public string TokenType { get; set; }
    }

    public class YouTubeOAuthService
    {
        private const string AuthEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
        private const string TokenEndpoint = "https://oauth2.googleapis.com/token";
        private static readonly HttpClient Http = new();

        private readonly string[] _scopes =
        {
            "https://www.googleapis.com/auth/youtube.readonly"
        };

        private readonly string _clientSecret;

        public YouTubeOAuthService(string clientSecret = null)
        {
            _clientSecret = clientSecret;
        }

        public async Task<YouTubeTokenResponse> AuthorizeAsync(string clientId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(clientId))
                throw new InvalidOperationException("YouTubeClientId が未設定です。build/local.props を設定してください。");

            string redirectUri = "http://127.0.0.1:18765/callback/";
            string state = Guid.NewGuid().ToString("N");
            string codeVerifier = CreateCodeVerifier();
            string codeChallenge = CreateCodeChallenge(codeVerifier);

            string authUrl = BuildAuthorizationUrl(clientId, redirectUri, state, codeChallenge);

            using var listener = new HttpListener();
            listener.Prefixes.Add(redirectUri);
            listener.Start();

            Process.Start(new ProcessStartInfo
            {
                FileName = authUrl,
                UseShellExecute = true
            });

            var contextTask = listener.GetContextAsync();
            var completed = await Task.WhenAny(contextTask, Task.Delay(TimeSpan.FromMinutes(3), cancellationToken));
            if (completed != contextTask)
                throw new TimeoutException("YouTube OAuth のコールバック待機がタイムアウトしました。");

            var context = await contextTask;
            string code = context.Request.QueryString["code"];
            string returnedState = context.Request.QueryString["state"];
            string error = context.Request.QueryString["error"];

            await WriteCallbackResponseAsync(context.Response, error);

            if (!string.IsNullOrEmpty(error))
                throw new Exception($"YouTube OAuth エラー: {error}");

            if (string.IsNullOrEmpty(code))
                throw new Exception("YouTube OAuth コールバックに code が含まれていません。");

            if (!string.Equals(state, returnedState, StringComparison.Ordinal))
                throw new Exception("YouTube OAuth の state が一致しません。");

            var token = await ExchangeCodeAsync(clientId, code, codeVerifier, redirectUri, cancellationToken);
            return token;
        }

        public async Task<YouTubeTokenResponse> RefreshTokenAsync(string clientId, string refreshToken, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(clientId))
                throw new InvalidOperationException("YouTubeClientId が未設定です。");
            if (string.IsNullOrWhiteSpace(refreshToken))
                throw new InvalidOperationException("YouTube refresh token が未設定です。");

            var parameters = new List<KeyValuePair<string, string>>
            {
                new("client_id", clientId),
                new("grant_type", "refresh_token"),
                new("refresh_token", refreshToken)
            };
            if (!string.IsNullOrWhiteSpace(_clientSecret))
                parameters.Add(new("client_secret", _clientSecret));

            var request = new FormUrlEncodedContent(parameters);

            var response = await Http.PostAsync(TokenEndpoint, request, cancellationToken);
            string json = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new Exception($"YouTube トークン更新失敗: {(int)response.StatusCode} {json}");

            var token = JsonConvert.DeserializeObject<YouTubeTokenResponse>(json)
                ?? throw new Exception("YouTube トークン更新レスポンスの解析に失敗しました。");

            if (string.IsNullOrEmpty(token.RefreshToken))
                token.RefreshToken = refreshToken;

            return token;
        }

        private string BuildAuthorizationUrl(string clientId, string redirectUri, string state, string codeChallenge)
        {
            string scope = Uri.EscapeDataString(string.Join(" ", _scopes));
            return $"{AuthEndpoint}?client_id={Uri.EscapeDataString(clientId)}" +
                   $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
                   "&response_type=code" +
                   $"&scope={scope}" +
                   "&access_type=offline" +
                   "&prompt=consent" +
                   $"&state={Uri.EscapeDataString(state)}" +
                   $"&code_challenge={Uri.EscapeDataString(codeChallenge)}" +
                   "&code_challenge_method=S256";
        }

        private async Task<YouTubeTokenResponse> ExchangeCodeAsync(
            string clientId,
            string code,
            string codeVerifier,
            string redirectUri,
            CancellationToken cancellationToken)
        {
            var parameters = new List<KeyValuePair<string, string>>
            {
                new("client_id", clientId),
                new("code", code),
                new("code_verifier", codeVerifier),
                new("redirect_uri", redirectUri),
                new("grant_type", "authorization_code")
            };
            if (!string.IsNullOrWhiteSpace(_clientSecret))
                parameters.Add(new("client_secret", _clientSecret));

            var request = new FormUrlEncodedContent(parameters);

            var response = await Http.PostAsync(TokenEndpoint, request, cancellationToken);
            string json = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                string message = json;
                try
                {
                    var obj = JObject.Parse(json);
                    message = obj["error_description"]?.Value<string>() ?? obj["error"]?.Value<string>() ?? json;
                }
                catch
                {
                }

                throw new Exception($"YouTube トークン取得失敗: {message}");
            }

            var token = JsonConvert.DeserializeObject<YouTubeTokenResponse>(json)
                ?? throw new Exception("YouTube トークンレスポンスの解析に失敗しました。");

            return token;
        }

        private static async Task WriteCallbackResponseAsync(HttpListenerResponse response, string error)
        {
            string html = string.IsNullOrEmpty(error)
                ? "<html><body><h3>YouTube OAuth 完了</h3><p>アプリに戻ってください。</p></body></html>"
                : $"<html><body><h3>YouTube OAuth 失敗</h3><p>{WebUtility.HtmlEncode(error)}</p></body></html>";
            byte[] body = Encoding.UTF8.GetBytes(html);
            response.ContentType = "text/html; charset=utf-8";
            response.ContentLength64 = body.Length;
            await response.OutputStream.WriteAsync(body, 0, body.Length);
            response.OutputStream.Close();
        }

        private static string CreateCodeVerifier()
        {
            byte[] bytes = new byte[32];
            RandomNumberGenerator.Fill(bytes);
            return Base64UrlEncode(bytes);
        }

        private static string CreateCodeChallenge(string codeVerifier)
        {
            using var sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(Encoding.ASCII.GetBytes(codeVerifier));
            return Base64UrlEncode(hash);
        }

        private static string Base64UrlEncode(byte[] data)
        {
            return Convert.ToBase64String(data)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }
    }
}
