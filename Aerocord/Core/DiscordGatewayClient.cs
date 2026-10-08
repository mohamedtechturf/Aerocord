using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Aerocord.Core
{
    public class DiscordGatewayClient
    {
        private const string GatewayUrl = "wss://gateway.discord.gg/?v=10&encoding=json";

        private ClientWebSocket _ws;
        private readonly string _token;
        private CancellationTokenSource _cts;
        private Task _heartbeatTask;
        private Task _receiveTask;
        private int? _lastSequence;
        private int _heartbeatIntervalMs = 41250;
        private string _sessionId;
        private volatile bool _identified;

        public string CurrentUserId { get; private set; }

        // Events consumed by the UI layer
        public event Action<JNode> Ready;
        public event Action<DiscordGuild> GuildAvailable;
        public event Action<DiscordMessage> MessageCreated;
        public event Action<string, PresenceStatus> PresenceUpdated;
        public event Action<string, string> TypingStarted; // channelId, userId
        public event Action Disconnected;
        public event Action<Exception> ConnectionError;
        // Voice signaling, consumed by DiscordVoiceClient
        public event Action<string, string, string> VoiceServerUpdated; // guildId, token, endpoint
        public event Action<string> VoiceStateSessionUpdated; // our own session_id

        public DiscordGatewayClient(string token)
        {
            _token = token;
        }

        public async Task ConnectAsync()
        {
            _cts = new CancellationTokenSource();
            _ws = new ClientWebSocket();
            await _ws.ConnectAsync(new Uri(GatewayUrl), _cts.Token).ConfigureAwait(false);
            _receiveTask = Task.Run(() => ReceiveLoopAsync(_cts.Token));
        }

        public async Task DisconnectAsync()
        {
            try
            {
                if (_cts != null) _cts.Cancel();
                if (_ws != null && _ws.State == WebSocketState.Open)
                    await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None).ConfigureAwait(false);
            }
            catch { /* best-effort shutdown */ }
        }

        private async Task ReceiveLoopAsync(CancellationToken token)
        {
            var buffer = new byte[16 * 1024];
            try
            {
                while (!token.IsCancellationRequested && _ws.State == WebSocketState.Open)
                {
                    using (var ms = new System.IO.MemoryStream())
                    {
                        WebSocketReceiveResult result;
                        do
                        {
                            result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), token).ConfigureAwait(false);
                            if (result.MessageType == WebSocketMessageType.Close)
                            {
                                Disconnected?.Invoke();
                                return;
                            }
                            ms.Write(buffer, 0, result.Count);
                        } while (!result.EndOfMessage);

                        string json = Encoding.UTF8.GetString(ms.ToArray());
                        HandleFrame(json);
                    }
                }
            }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested)
                    ConnectionError?.Invoke(ex);
            }
            finally
            {
                Disconnected?.Invoke();
            }
        }

        private void HandleFrame(string json)
        {
            JNode n;
            try { n = Json.ParseNode(json); }
            catch { return; }

            int op = (int)n["op"].AsLong(-1);
            if (!n["s"].IsNull) _lastSequence = (int)n["s"].AsLong();

            switch (op)
            {
                case 10: // HELLO
                    _heartbeatIntervalMs = (int)n["d"]["heartbeat_interval"].AsLong(41250);
                    _heartbeatTask = Task.Run(() => HeartbeatLoopAsync(_cts.Token));
                    _ = IdentifyAsync();
                    break;

                case 0: // DISPATCH
                    HandleDispatch(n["t"].AsString(""), n["d"]);
                    break;

                case 11: // HEARTBEAT ACK
                    break;

                case 1: // server requests immediate heartbeat
                    _ = SendHeartbeatAsync();
                    break;

                case 7: // RECONNECT requested
                case 9: // INVALID SESSION
                    Disconnected?.Invoke();
                    break;
            }
        }

        private void HandleDispatch(string eventType, JNode d)
        {
            switch (eventType)
            {
                case "READY":
                    _sessionId = d["session_id"].AsString();
                    CurrentUserId = d["user"]["id"].AsString();
                    Ready?.Invoke(d);
                    foreach (var g in d["guilds"].Items())
                    {
                        // READY often includes unavailable guild stubs; full data
                        if (!g["unavailable"].AsBool(false) && g.Has("channels"))
                            GuildAvailable?.Invoke(DiscordGuild.FromJson(g));
                    }
                    break;

                case "GUILD_CREATE":
                    GuildAvailable?.Invoke(DiscordGuild.FromJson(d));
                    break;

                case "MESSAGE_CREATE":
                    var msg = DiscordMessage.FromJson(d);
                    if (msg.Author != null) msg.IsFromSelf = msg.Author.Id == CurrentUserId;
                    MessageCreated?.Invoke(msg);
                    break;

                case "PRESENCE_UPDATE":
                    string uid = d["user"]["id"].AsString();
                    var status = PresenceStatusHelper.FromApiString(d["status"].AsString("offline"));
                    if (!string.IsNullOrEmpty(uid))
                        PresenceUpdated?.Invoke(uid, status);
                    break;

                case "TYPING_START":
                    TypingStarted?.Invoke(d["channel_id"].AsString(), d["user_id"].AsString());
                    break;

                case "VOICE_SERVER_UPDATE":
                    VoiceServerUpdated?.Invoke(
                        d["guild_id"].AsString(),
                        d["token"].AsString(),
                        d["endpoint"].AsString());
                    break;

                case "VOICE_STATE_UPDATE":
                    if (d["user_id"].AsString() == CurrentUserId)
                        VoiceStateSessionUpdated?.Invoke(d["session_id"].AsString());
                    break;
            }
        }

        private async Task IdentifyAsync()
        {
            var payload = new Dictionary<string, object>
            {
                { "op", 2 },
                { "d", new Dictionary<string, object>
                    {
                        { "token", _token },
                        { "properties", new Dictionary<string, object>
                            {
                                { "os", "Windows" },
                                { "browser", "Aerocord" },
                                { "device", "Aerocord" }
                            }
                        },
                        { "compress", false },
                        { "presence", new Dictionary<string, object>
                            {
                                { "status", "online" },
                                { "since", 0 },
                                { "activities", new List<object>() },
                                { "afk", false }
                            }
                        }
                    }
                }
            };
            await SendAsync(payload).ConfigureAwait(false);
            _identified = true;
        }

        private async Task HeartbeatLoopAsync(CancellationToken token)
        {
            // Jitter the first beat as the gateway spec recommends.
            await Task.Delay((int)(_heartbeatIntervalMs * 0.5), token).ConfigureAwait(false);
            while (!token.IsCancellationRequested)
            {
                await SendHeartbeatAsync().ConfigureAwait(false);
                await Task.Delay(_heartbeatIntervalMs, token).ConfigureAwait(false);
            }
        }

        private async Task SendHeartbeatAsync()
        {
            var payload = new Dictionary<string, object>
            {
                { "op", 1 },
                { "d", _lastSequence.HasValue ? (object)_lastSequence.Value : null }
            };
            await SendAsync(payload).ConfigureAwait(false);
        }

        public async Task SendPresenceUpdateAsync(PresenceStatus status, string customStatusText)
        {
            var activities = new List<object>();
            if (!string.IsNullOrEmpty(customStatusText))
            {
                activities.Add(new Dictionary<string, object>
                {
                    { "name", "Custom Status" },
                    { "type", 4 },
                    { "state", customStatusText }
                });
            }
            var payload = new Dictionary<string, object>
            {
                { "op", 3 },
                { "d", new Dictionary<string, object>
                    {
                        { "since", 0 },
                        { "status", PresenceStatusHelper.ToApiString(status) },
                        { "activities", activities },
                        { "afk", false }
                    }
                }
            };
            await SendAsync(payload).ConfigureAwait(false);
        }

        /// <summary>Used by DiscordVoiceClient to join/leave a voice channel.</summary>
        public async Task SendVoiceStateUpdateAsync(string guildId, string channelId, bool selfMute, bool selfDeaf)
        {
            var payload = new Dictionary<string, object>
            {
                { "op", 4 },
                { "d", new Dictionary<string, object>
                    {
                        { "guild_id", guildId },
                        { "channel_id", channelId },
                        { "self_mute", selfMute },
                        { "self_deaf", selfDeaf }
                    }
                }
            };
            await SendAsync(payload).ConfigureAwait(false);
        }

        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);

        private async Task SendAsync(object payload)
        {
            if (_ws == null || _ws.State != WebSocketState.Open) return;
            string json = Json.Write(payload);
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            await _sendLock.WaitAsync().ConfigureAwait(false);
            try
            {
                await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            finally
            {
                _sendLock.Release();
            }
        }
    }
}
