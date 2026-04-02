using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace TwitchChatOverlay.Services
{
    public class TwitchApiService
    {
        private readonly HttpClient _httpClient = new HttpClient();

        public async Task<(string UserId, string Login)> GetCurrentUserAsync(string accessToken, string clientId)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "https://api.twitch.tv/helix/users");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Headers.Add("Client-Id", clientId);

            var response = await _httpClient.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                throw new Exception($"ユーザー情報の取得に失敗: {content}");

            var json = JObject.Parse(content);
            var data = json["data"]?[0];
            if (data == null)
                throw new Exception("ユーザー情報が見つかりません");

            return (data["id"]!.ToString(), data["login"]!.ToString());
        }

        /// <summary>
        /// 保存済みトークンが有効か検証する。有効なら (true, login) を返す。
        /// </summary>
        public async Task<(bool IsValid, string Login, string UserId, int ExpiresIn)> ValidateTokenAsync(string accessToken)
        {
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, "https://id.twitch.tv/oauth2/validate");
                request.Headers.Authorization = new AuthenticationHeaderValue("OAuth", accessToken);

                var response = await _httpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                    return (false, null, null, 0);

                var content = await response.Content.ReadAsStringAsync();
                var json = JObject.Parse(content);
                string login = json["login"]?.ToString();
                string userId = json["user_id"]?.ToString();
                int expiresIn = json["expires_in"]?.Value<int>() ?? 0;
                return (true, login, userId, expiresIn);
            }
            catch (Exception ex)
            {
                LogService.Warning("トークン検証中にネットワークエラーが発生しました", ex);
                return (false, null, null, 0);
            }
        }

        public async Task CreateEventSubSubscriptionsAsync(
            string accessToken, string clientId,
            string sessionId, string broadcasterUserId, string userId)
        {
            var subscriptions = new[]
            {
                new
                {
                    type = "channel.chat.message", version = "1",
                    condition = new { broadcaster_user_id = broadcasterUserId, user_id = userId }
                },
                new
                {
                    type = "channel.channel_points_custom_reward_redemption.add", version = "1",
                    condition = new { broadcaster_user_id = broadcasterUserId, user_id = (string)null }
                },
                new
                {
                    type = "channel.raid", version = "1",
                    condition = new { broadcaster_user_id = (string)null, user_id = (string)null }
                },
                new
                {
                    type = "channel.follow", version = "2",
                    condition = new { broadcaster_user_id = broadcasterUserId, user_id = broadcasterUserId }
                },
                new
                {
                    type = "channel.subscribe", version = "1",
                    condition = new { broadcaster_user_id = broadcasterUserId, user_id = (string)null }
                },
                new
                {
                    type = "channel.subscription.gift", version = "1",
                    condition = new { broadcaster_user_id = broadcasterUserId, user_id = (string)null }
                },
                new
                {
                    type = "channel.subscription.message", version = "1",
                    condition = new { broadcaster_user_id = broadcasterUserId, user_id = (string)null }
                },
                new
                {
                    type = "channel.hype_train.begin", version = "2",
                    condition = new { broadcaster_user_id = broadcasterUserId, user_id = (string)null }
                },
                new
                {
                    type = "channel.hype_train.end", version = "2",
                    condition = new { broadcaster_user_id = broadcasterUserId, user_id = (string)null }
                },
            };

            foreach (var sub in subscriptions)
            {
                await CreateSingleSubscriptionAsync(accessToken, clientId, sessionId, sub.type, sub.version, sub.condition, broadcasterUserId, userId);
            }
        }

        private async Task CreateSingleSubscriptionAsync(
            string accessToken, string clientId, string sessionId,
            string type, string version, object conditionOverride,
            string broadcasterUserId, string userId)
        {
            object condition = type switch
            {
                "channel.chat.message" => new
                {
                    broadcaster_user_id = broadcasterUserId,
                    user_id = userId
                },
                "channel.channel_points_custom_reward_redemption.add" => new
                {
                    broadcaster_user_id = broadcasterUserId
                },
                "channel.raid" => new
                {
                    to_broadcaster_user_id = broadcasterUserId
                },
                "channel.follow" => new
                {
                    broadcaster_user_id = broadcasterUserId,
                    moderator_user_id = broadcasterUserId
                },
                _ => new
                {
                    broadcaster_user_id = broadcasterUserId
                }
            };

            var body = new
            {
                type,
                version,
                condition,
                transport = new
                {
                    method = "websocket",
                    session_id = sessionId
                }
            };

            var json = JsonConvert.SerializeObject(body);
            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.twitch.tv/helix/eventsub/subscriptions");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Headers.Add("Client-Id", clientId);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                // 409 Conflict (既に存在) は無視する
                if ((int)response.StatusCode != 409)
                    throw new Exception($"EventSubサブスクリプション作成失敗 ({type}): {error}");
            }
        }
    }
}
