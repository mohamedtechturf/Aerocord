using System;
using System.Collections.Generic;
using System.Linq;

namespace Aerocord.Core
{
    public class AppState
    {
        public static AppState Current;

        public DiscordRestClient Rest;
        public DiscordGatewayClient Gateway;
        public DiscordVoiceClient Voice;
        public DiscordUser Me;
        public PresenceStatus MyStatus = PresenceStatus.Online;
        public string MyCustomStatus = "";

        public readonly List<DiscordGuild> Guilds = new List<DiscordGuild>();
        public readonly List<DiscordChannel> PrivateChannels = new List<DiscordChannel>();
        public readonly List<DiscordRelationship> Friends = new List<DiscordRelationship>();
        public readonly Dictionary<string, PresenceStatus> PresenceByUserId = new Dictionary<string, PresenceStatus>();
        public readonly Dictionary<string, List<DiscordMessage>> MessageCache = new Dictionary<string, List<DiscordMessage>>();

        public event Action ReadyAndPopulated;
        public event Action<DiscordMessage> MessageArrived;
        public event Action<string, PresenceStatus> PresenceChanged;
        public event Action<string, string> TypingSeen;
        public event Action Disconnected;

        public AppState(string token)
        {
            Rest = new DiscordRestClient(token);
            Gateway = new DiscordGatewayClient(token);
            Gateway.GuildAvailable += g =>
            {
                if (!Guilds.Any(x => x.Id == g.Id)) Guilds.Add(g);
            };
            Gateway.MessageCreated += m =>
            {
                if (!MessageCache.ContainsKey(m.ChannelId))
                    MessageCache[m.ChannelId] = new List<DiscordMessage>();
                MessageCache[m.ChannelId].Add(m);
                MessageArrived?.Invoke(m);
            };
            Gateway.PresenceUpdated += (uid, status) =>
            {
                PresenceByUserId[uid] = status;
                PresenceChanged?.Invoke(uid, status);
            };
            Gateway.TypingStarted += (cid, uid) => TypingSeen?.Invoke(cid, uid);
            Gateway.Disconnected += () => Disconnected?.Invoke();
            Gateway.Ready += async d =>
            {
                Voice = new DiscordVoiceClient(Gateway, Gateway.CurrentUserId);
                await PopulateAfterReadyAsync().ConfigureAwait(false);
                ReadyAndPopulated?.Invoke();
            };
        }

        private async System.Threading.Tasks.Task PopulateAfterReadyAsync()
        {
            try
            {
                Me = await Rest.GetCurrentUserAsync().ConfigureAwait(false);
                var privateChannels = await Rest.GetPrivateChannelsAsync().ConfigureAwait(false);
                PrivateChannels.Clear();
                PrivateChannels.AddRange(privateChannels);

                try
                {
                    var relationships = await Rest.GetRelationshipsAsync().ConfigureAwait(false);
                    Friends.Clear();
                    Friends.AddRange(relationships.Where(r => r.Type == 1));
                }
                catch
                {
                    // Relationships endpoint can be unavailable in some account states; not fatal.
                }
            }
            catch (Exception)
            {
                // Surfaced to the user via Disconnected/ConnectionError handlers in MainForm.
            }
        }

        public List<DiscordMessage> GetCachedMessages(string channelId)
        {
            List<DiscordMessage> list;
            return MessageCache.TryGetValue(channelId, out list) ? list : new List<DiscordMessage>();
        }
    }
}
