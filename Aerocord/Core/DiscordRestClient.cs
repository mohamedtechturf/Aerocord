using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace Aerocord.Core
{
    public class DiscordRestClient
    {
        public const string ApiBase = "https://discord.com/api/v10";

        private readonly HttpClient _http;
        public string Token { get; private set; }

        public DiscordRestClient(string token)
        {
            Token = token;
            _http = new HttpClient();
            _http.DefaultRequestHeaders.Clear();
            // NOTE: no "Bot " prefix -> user token auth.
            _http.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", token);
            _http.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            string superProps = Convert.ToBase64String(Encoding.UTF8.GetBytes(
                "{\"os\":\"Windows\",\"browser\":\"Chrome\",\"device\":\"\",\"referrer\":\"\",\"referring_domain\":\"\"}"));
            _http.DefaultRequestHeaders.TryAddWithoutValidation("X-Super-Properties", superProps);
        }

        private async Task<JNode> GetAsync(string path)
        {
            HttpResponseMessage resp = await _http.GetAsync(ApiBase + path).ConfigureAwait(false);
            string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                throw new DiscordApiException((int)resp.StatusCode, body);
            return string.IsNullOrEmpty(body) ? new JNode(null) : Json.ParseNode(body);
        }

        private async Task<JNode> PostAsync(string path, object payload)
        {
            string json = payload == null ? "{}" : Json.Write(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            HttpResponseMessage resp = await _http.PostAsync(ApiBase + path, content).ConfigureAwait(false);
            string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                throw new DiscordApiException((int)resp.StatusCode, body);
            return string.IsNullOrEmpty(body) ? new JNode(null) : Json.ParseNode(body);
        }

        private async Task<JNode> PatchAsync(string path, object payload)
        {
            string json = payload == null ? "{}" : Json.Write(payload);
            var req = new HttpRequestMessage(new HttpMethod("PATCH"), ApiBase + path);
            req.Content = new StringContent(json, Encoding.UTF8, "application/json");
            HttpResponseMessage resp = await _http.SendAsync(req).ConfigureAwait(false);
            string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                throw new DiscordApiException((int)resp.StatusCode, body);
            return string.IsNullOrEmpty(body) ? new JNode(null) : Json.ParseNode(body);
        }

        public async Task<DiscordUser> GetCurrentUserAsync()
        {
            JNode n = await GetAsync("/users/@me").ConfigureAwait(false);
            return DiscordUser.FromJson(n);
        }

        public async Task<List<DiscordGuild>> GetGuildsAsync()
        {
            JNode n = await GetAsync("/users/@me/guilds").ConfigureAwait(false);
            var result = new List<DiscordGuild>();
            foreach (var g in n.Items())
            {
                result.Add(new DiscordGuild
                {
                    Id = g["id"].AsString(),
                    Name = g["name"].AsString(""),
                    IconHash = g["icon"].AsString(null)
                });
            }
            return result;
        }

        public async Task<List<DiscordChannel>> GetGuildChannelsAsync(string guildId)
        {
            JNode n = await GetAsync("/guilds/" + guildId + "/channels").ConfigureAwait(false);
            var result = new List<DiscordChannel>();
            foreach (var c in n.Items())
            {
                var chan = DiscordChannel.FromJson(c);
                chan.GuildId = guildId;
                result.Add(chan);
            }
            return result;
        }

        public async Task<List<DiscordChannel>> GetPrivateChannelsAsync()
        {
            JNode n = await GetAsync("/users/@me/channels").ConfigureAwait(false);
            var result = new List<DiscordChannel>();
            foreach (var c in n.Items())
                result.Add(DiscordChannel.FromJson(c));
            return result;
        }

        public async Task<List<DiscordRelationship>> GetRelationshipsAsync()
        {
            JNode n = await GetAsync("/users/@me/relationships").ConfigureAwait(false);
            var result = new List<DiscordRelationship>();
            foreach (var r in n.Items())
            {
                result.Add(new DiscordRelationship
                {
                    User = DiscordUser.FromJson(r["user"]),
                    Type = (int)r["type"].AsLong(0)
                });
            }
            return result;
        }

        public async Task<List<DiscordMessage>> GetMessagesAsync(string channelId, int limit = 50)
        {
            JNode n = await GetAsync("/channels/" + channelId + "/messages?limit=" + limit).ConfigureAwait(false);
            var result = new List<DiscordMessage>();
            foreach (var m in n.Items())
                result.Add(DiscordMessage.FromJson(m));
            result.Reverse(); // API returns newest-first; we want chronological order
            return result;
        }

        public async Task<DiscordMessage> SendMessageAsync(string channelId, string content)
        {
            var payload = new Dictionary<string, object> { { "content", content } };
            JNode n = await PostAsync("/channels/" + channelId + "/messages", payload).ConfigureAwait(false);
            return DiscordMessage.FromJson(n);
        }

        public async Task SendTypingAsync(string channelId)
        {
            await PostAsync("/channels/" + channelId + "/typing", null).ConfigureAwait(false);
        }

        public async Task<DiscordChannel> CreateDmAsync(string recipientUserId)
        {
            var payload = new Dictionary<string, object> { { "recipient_id", recipientUserId } };
            JNode n = await PostAsync("/users/@me/channels", payload).ConfigureAwait(false);
            return DiscordChannel.FromJson(n);
        }
    }

    public class DiscordApiException : Exception
    {
        public int StatusCode { get; private set; }
        public string ResponseBody { get; private set; }

        public DiscordApiException(int statusCode, string body)
            : base("Discord API error " + statusCode + ": " + body)
        {
            StatusCode = statusCode;
            ResponseBody = body;
        }
    }
}
