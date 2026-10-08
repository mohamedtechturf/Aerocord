using System;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Aerocord.Core
{
    /// EXPERIMENTAL / BEST-EFFORT voice support.
    ///
    /// What this class actually does:
    ///   1. Tells Discord (via the main gateway) that you want to join a voice channel.
    ///   2. Connects to the per-guild voice gateway and completes the handshake
    ///      (IDENTIFY, SELECT_PROTOCOL, UDP IP discovery, SESSION_DESCRIPTION).
    ///   3. At that point Discord shows you as "connected" to the voice channel.
    ///
    /// What it deliberately does NOT do:
    ///   Encode/decode or actually transmit audio. Real voice requires an Opus
    ///   codec and libsodium (or equivalent AEAD) encryption of RTP packets.
    ///   Those are native/NuGet dependencies (e.g. Concentus for managed Opus,
    ///   NAudio for mic capture/playback, Sodium.Core for encryption) that this
    ///   offline build environment cannot fetch. The UDP socket and secret key
    ///   below are exactly what you'd hook a real encoder into - search the
    ///   "TODO(audio)" markers for where to add it once you add those NuGet
    ///   packages in Visual Studio on your own machine.
    public class DiscordVoiceClient
    {
        private readonly DiscordGatewayClient _mainGateway;
        private readonly string _userId;

        private ClientWebSocket _voiceWs;
        private UdpClient _udp;
        private CancellationTokenSource _cts;

        private string _guildId, _channelId, _sessionId, _voiceToken, _endpoint;
        private uint _ssrc;
        private IPEndPoint _voiceServerEndpoint;
        private byte[] _secretKey; // TODO(audio): use this + libsodium to encrypt RTP payloads

        public event Action VoiceReady;   // handshake complete, "connected" in the channel
        public event Action<string> VoiceError;
        public event Action VoiceLeft;

        public bool IsConnected { get; private set; }

        public DiscordVoiceClient(DiscordGatewayClient mainGateway, string userId)
        {
            _mainGateway = mainGateway;
            _userId = userId;
            _mainGateway.VoiceServerUpdated += OnVoiceServerUpdated;
            _mainGateway.VoiceStateSessionUpdated += OnVoiceStateSessionUpdated;
        }

        public async Task JoinAsync(string guildId, string channelId)
        {
            _guildId = guildId;
            _channelId = channelId;
            _cts = new CancellationTokenSource();
            // self-mute/self-deaf = true by default since we can't actually send/receive audio yet.
            await _mainGateway.SendVoiceStateUpdateAsync(guildId, channelId, true, true).ConfigureAwait(false);
        }

        public async Task LeaveAsync()
        {
            try
            {
                if (_guildId != null)
                    await _mainGateway.SendVoiceStateUpdateAsync(_guildId, null, true, true).ConfigureAwait(false);
                if (_cts != null) _cts.Cancel();
                if (_voiceWs != null && _voiceWs.State == WebSocketState.Open)
                    await _voiceWs.CloseAsync(WebSocketCloseStatus.NormalClosure, "leave", CancellationToken.None).ConfigureAwait(false);
                if (_udp != null) _udp.Close();
            }
            catch { /* best effort */ }
            finally
            {
                IsConnected = false;
                VoiceLeft?.Invoke();
            }
        }

        private void OnVoiceStateSessionUpdated(string sessionId)
        {
            _sessionId = sessionId;
            TryBeginHandshake();
        }

        private void OnVoiceServerUpdated(string guildId, string token, string endpoint)
        {
            if (guildId != _guildId) return;
            _voiceToken = token;
            _endpoint = endpoint;
            TryBeginHandshake();
        }

        private bool _handshakeStarted;

        private void TryBeginHandshake()
        {
            if (_handshakeStarted) return;
            if (string.IsNullOrEmpty(_sessionId) || string.IsNullOrEmpty(_voiceToken) || string.IsNullOrEmpty(_endpoint)) return;
            _handshakeStarted = true;
            _ = ConnectVoiceWebSocketAsync();
        }

        private async Task ConnectVoiceWebSocketAsync()
        {
            try
            {
                string host = _endpoint.Split(':')[0];
                _voiceWs = new ClientWebSocket();
                await _voiceWs.ConnectAsync(new Uri("wss://" + host + "/?v=8"), _cts.Token).ConfigureAwait(false);
                _ = ReceiveLoopAsync();

                var identify = new Dictionary<string, object>
                {
                    { "op", 0 },
                    { "d", new Dictionary<string, object>
                        {
                            { "server_id", _guildId },
                            { "user_id", _userId },
                            { "session_id", _sessionId },
                            { "token", _voiceToken }
                        }
                    }
                };
                await SendAsync(identify).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                VoiceError?.Invoke("Voice connect failed: " + ex.Message);
            }
        }

        private async Task ReceiveLoopAsync()
        {
            var buffer = new byte[8 * 1024];
            try
            {
                while (_voiceWs.State == WebSocketState.Open && !_cts.IsCancellationRequested)
                {
                    using (var ms = new System.IO.MemoryStream())
                    {
                        WebSocketReceiveResult result;
                        do
                        {
                            result = await _voiceWs.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token).ConfigureAwait(false);
                            if (result.MessageType == WebSocketMessageType.Close) return;
                            ms.Write(buffer, 0, result.Count);
                        } while (!result.EndOfMessage);
                        await HandleVoiceFrameAsync(Encoding.UTF8.GetString(ms.ToArray())).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex)
            {
                if (!_cts.IsCancellationRequested)
                    VoiceError?.Invoke("Voice socket error: " + ex.Message);
            }
        }

        private async Task HandleVoiceFrameAsync(string json)
        {
            JNode n = Json.ParseNode(json);
            int op = (int)n["op"].AsLong(-1);
            switch (op)
            {
                case 8: // HELLO -> start heartbeating
                    int interval = (int)n["d"]["heartbeat_interval"].AsLong(5000);
                    _ = VoiceHeartbeatLoopAsync(interval);
                    break;

                case 2: // READY -> ssrc/ip/port for UDP, do IP discovery
                    _ssrc = (uint)n["d"]["ssrc"].AsLong();
                    string ip = n["d"]["ip"].AsString();
                    int port = (int)n["d"]["port"].AsLong();
                    _voiceServerEndpoint = new IPEndPoint(IPAddress.Parse(ip), port);
                    await DoIpDiscoveryAsync().ConfigureAwait(false);
                    break;

                case 4: // SESSION_DESCRIPTION -> secret key, handshake complete
                    var keyList = n["d"]["secret_key"];
                    _secretKey = new byte[keyList.Count];
                    for (int i = 0; i < keyList.Count; i++)
                        _secretKey[i] = (byte)keyList[i].AsLong();
                    IsConnected = true;
                    VoiceReady?.Invoke();
                    break;
            }
        }

        private async Task DoIpDiscoveryAsync()
        {
            _udp = new UdpClient();
            _udp.Connect(_voiceServerEndpoint);

            // Discord IP discovery packet: type(2) + length(2) + ssrc(4) + address(64) + port(2)
            byte[] packet = new byte[74];
            packet[0] = 0x00; packet[1] = 0x01; // request
            packet[2] = 0x00; packet[3] = 70;   // length
            byte[] ssrcBytes = BitConverter.GetBytes(_ssrc);
            if (BitConverter.IsLittleEndian) Array.Reverse(ssrcBytes);
            Array.Copy(ssrcBytes, 0, packet, 4, 4);

            await _udp.SendAsync(packet, packet.Length).ConfigureAwait(false);
            UdpReceiveResult result = await _udp.ReceiveAsync().ConfigureAwait(false);
            string externalIp = Encoding.ASCII.GetString(result.Buffer, 8, 64).TrimEnd('\0');
            int externalPort = (result.Buffer[72] << 8) | result.Buffer[73];
            // Discord actually sends port little-endian; re-read correctly:
            externalPort = result.Buffer[72] | (result.Buffer[73] << 8);

            var selectProtocol = new Dictionary<string, object>
            {
                { "op", 1 },
                { "d", new Dictionary<string, object>
                    {
                        { "protocol", "udp" },
                        { "data", new Dictionary<string, object>
                            {
                                { "address", externalIp },
                                { "port", externalPort },
                                { "mode", "xsalsa20_poly1305" }
                            }
                        }
                    }
                }
            };
            await SendAsync(selectProtocol).ConfigureAwait(false);
        }

        private async Task VoiceHeartbeatLoopAsync(int intervalMs)
        {
            while (_voiceWs != null && _voiceWs.State == WebSocketState.Open && !_cts.IsCancellationRequested)
            {
                var hb = new Dictionary<string, object> { { "op", 3 }, { "d", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() } };
                try { await SendAsync(hb).ConfigureAwait(false); } catch { return; }
                try { await Task.Delay(intervalMs, _cts.Token).ConfigureAwait(false); } catch { return; }
            }
        }

        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);

        private async Task SendAsync(object payload)
        {
            if (_voiceWs == null || _voiceWs.State != WebSocketState.Open) return;
            byte[] bytes = Encoding.UTF8.GetBytes(Json.Write(payload));
            await _sendLock.WaitAsync().ConfigureAwait(false);
            try
            {
                await _voiceWs.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            finally { _sendLock.Release(); }
        }
    }
}
