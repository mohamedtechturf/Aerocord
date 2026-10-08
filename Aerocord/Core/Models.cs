using System;
using System.Collections.Generic;

namespace Aerocord.Core
{
    public enum PresenceStatus
    {
        Online,
        Idle,
        DoNotDisturb,
        Invisible,
        Offline
    }

    public static class PresenceStatusHelper
    {
        public static string ToApiString(PresenceStatus s)
        {
            switch (s)
            {
                case PresenceStatus.Online: return "online";
                case PresenceStatus.Idle: return "idle";
                case PresenceStatus.DoNotDisturb: return "dnd";
                case PresenceStatus.Invisible: return "invisible";
                default: return "offline";
            }
        }

        public static PresenceStatus FromApiString(string s)
        {
            switch ((s ?? "").ToLowerInvariant())
            {
                case "online": return PresenceStatus.Online;
                case "idle": return PresenceStatus.Idle;
                case "dnd": return PresenceStatus.DoNotDisturb;
                case "invisible": return PresenceStatus.Invisible;
                default: return PresenceStatus.Offline;
            }
        }
    }

    public class DiscordUser
    {
        public string Id;
        public string Username;
        public string Discriminator; // "0" for new-style unique usernames
        public string GlobalName;
        public string AvatarHash;
        public PresenceStatus Status = PresenceStatus.Offline;
        public string CustomStatusText;

        public string DisplayName
        {
            get
            {
                if (!string.IsNullOrEmpty(GlobalName)) return GlobalName;
                if (!string.IsNullOrEmpty(Discriminator) && Discriminator != "0")
                    return Username + "#" + Discriminator;
                return Username;
            }
        }

        public static DiscordUser FromJson(JNode n)
        {
            if (n.IsNull) return null;
            return new DiscordUser
            {
                Id = n["id"].AsString(),
                Username = n["username"].AsString(""),
                Discriminator = n["discriminator"].AsString("0"),
                GlobalName = n["global_name"].AsString(null),
                AvatarHash = n["avatar"].AsString(null)
            };
        }
    }

    public enum ChannelKind
    {
        GuildText,
        GuildVoice,
        DirectMessage,
        GroupDirectMessage,
        GuildCategory,
        Other
    }

    public class DiscordChannel
    {
        public string Id;
        public string GuildId;
        public string Name;
        public ChannelKind Kind;
        public int Position;
        public string ParentId;
        // For DMs: the other participant(s)
        public List<DiscordUser> Recipients = new List<DiscordUser>();

        public string DisplayName
        {
            get
            {
                if (Kind == ChannelKind.DirectMessage && Recipients.Count > 0)
                    return Recipients[0].DisplayName;
                if (Kind == ChannelKind.GroupDirectMessage)
                    return string.IsNullOrEmpty(Name) ? "Group Chat" : Name;
                return Name;
            }
        }

        public static ChannelKind KindFromType(int type)
        {
            switch (type)
            {
                case 0: return ChannelKind.GuildText;
                case 1: return ChannelKind.DirectMessage;
                case 2: return ChannelKind.GuildVoice;
                case 3: return ChannelKind.GroupDirectMessage;
                case 4: return ChannelKind.GuildCategory;
                default: return ChannelKind.Other;
            }
        }

        public static DiscordChannel FromJson(JNode n)
        {
            var c = new DiscordChannel();
            c.Id = n["id"].AsString();
            c.GuildId = n["guild_id"].AsString(null);
            c.Name = n["name"].AsString("");
            c.Kind = KindFromType((int)n["type"].AsLong(0));
            c.Position = (int)n["position"].AsLong(0);
            c.ParentId = n["parent_id"].AsString(null);
            foreach (var r in n["recipients"].Items())
            {
                var u = DiscordUser.FromJson(r);
                if (u != null) c.Recipients.Add(u);
            }
            return c;
        }
    }

    public class DiscordGuild
    {
        public string Id;
        public string Name;
        public string IconHash;
        public List<DiscordChannel> Channels = new List<DiscordChannel>();

        public static DiscordGuild FromJson(JNode n)
        {
            var g = new DiscordGuild();
            g.Id = n["id"].AsString();
            g.Name = n["name"].AsString("");
            g.IconHash = n["icon"].AsString(null);
            foreach (var ch in n["channels"].Items())
            {
                var c = DiscordChannel.FromJson(ch);
                c.GuildId = g.Id;
                g.Channels.Add(c);
            }
            return g;
        }
    }

    public class DiscordMessage
    {
        public string Id;
        public string ChannelId;
        public string Content;
        public DiscordUser Author;
        public DateTime Timestamp;
        public bool IsFromSelf;

        public static DiscordMessage FromJson(JNode n)
        {
            var m = new DiscordMessage();
            m.Id = n["id"].AsString();
            m.ChannelId = n["channel_id"].AsString();
            m.Content = n["content"].AsString("");
            m.Author = DiscordUser.FromJson(n["author"]);
            DateTime ts;
            DateTime.TryParse(n["timestamp"].AsString(""), out ts);
            m.Timestamp = ts;
            return m;
        }
    }
    
    public class DiscordRelationship
    {
        public DiscordUser User;
        public int Type; // 1 = friend, 2 = blocked, 3 = incoming request, 4 = outgoing request
    }
}
