using System;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace TwitchChatOverlay.Services
{
    public class TwitchTokenExchange
    {
        private readonly string _clientId;
        private readonly string _clientSecret;
        private readonly string _redirectUri = "http://localhost:8534";
        private readonly HttpClient _httpClient = new HttpClient();

        public TwitchTokenExchange(string clientId, string clientSecret)
        {
            _clientId = clientId;
            _clientSecret = clientSecret;
        }

        /// <summary>
        /// 認可コードをOAuthトークンに交換
        /// </summary>
        public async Task<TokenResponse> ExchangeCodeForTokenAsync(string authCode)
        {
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Post,
                    "https://id.twitch.tv/oauth2/token");

                request.Content = new FormUrlEncodedContent(new[]
                {
                    new System.Collections.Generic.KeyValuePair<string, string>("client_id", _clientId),
                    new System.Collections.Generic.KeyValuePair<string, string>("client_secret", _clientSecret),
                    new System.Collections.Generic.KeyValuePair<string, string>("code", authCode),
                    new System.Collections.Generic.KeyValuePair<string, string>("grant_type", "authorization_code"),
                    new System.Collections.Generic.KeyValuePair<string, string>("redirect_uri", _redirectUri)
                });

                HttpResponseMessage response = await _httpClient.SendAsync(request);
                string content = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var tokenResponse = JsonConvert.DeserializeObject<TokenResponse>(content);
                    return tokenResponse;
                }
                else
                {
                    throw new Exception($"トークン交換に失敗しました: {content}");
                }
            }
            catch (Exception ex)
            {
                LogService.Error("トークン交換エラー", ex);
                throw new Exception($"トークン交換エラー: {ex.Message}", ex);
            }
        }
    }

    public class TokenResponse
    {
        [JsonProperty("access_token")]
        public string AccessToken { get; set; }

        [JsonProperty("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonProperty("refresh_token")]
        public string RefreshToken { get; set; }

        [JsonProperty("scope")]
        public string[] Scope { get; set; }

        [JsonProperty("token_type")]
        public string TokenType { get; set; }
    }
}
