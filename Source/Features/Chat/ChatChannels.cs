using System;

namespace KMHPatch.Features.Chat
{
    // The DM id must be byte-identical to the server's, or the two sides key different channels for one pair.
    public static class ChatChannels
    {
        public const string Server = "server";
        private const string GuildPrefix = "guild:";
        private const string DmPrefix    = "dm:";

        public static string Guild(string guildName) => GuildPrefix + (guildName ?? "");

        private const char Sep = '|';

        // A separator in a username builds an id the server's parser rejects, so refuse to build it at all.
        public static bool CanDm(string username)
            => !string.IsNullOrEmpty(username) && username.IndexOf(Sep) < 0;

        public static string Dm(string a, string b)
        {
            if (!CanDm(a) || !CanDm(b)) return "";
            a = a.ToLowerInvariant();
            b = b.ToLowerInvariant();
            bool aFirst = string.CompareOrdinal(a, b) <= 0;
            return aFirst ? $"{DmPrefix}{a}{Sep}{b}" : $"{DmPrefix}{b}{Sep}{a}";
        }

        public static bool IsDm(string channel)    => channel != null && channel.StartsWith(DmPrefix, StringComparison.Ordinal);
        public static bool IsGuild(string channel)  => channel != null && channel.StartsWith(GuildPrefix, StringComparison.Ordinal);

        public static bool TryParseDm(string channel, out string a, out string b)
        {
            a = b = null;
            if (!IsDm(channel)) return false;
            string rest = channel.Substring(DmPrefix.Length);
            int bar = rest.IndexOf('|');
            if (bar <= 0 || bar >= rest.Length - 1) return false;
            a = rest.Substring(0, bar);
            b = rest.Substring(bar + 1);
            // The server's rule exactly: accepting an id it refuses shows a channel that swallows every message sent to it.
            return a.Length > 0 && b.Length > 0 && b.IndexOf(Sep) < 0;
        }

        // The other participant in a DM (lower-cased id form), or null. Callers resolve display casing from the roster.
        public static string OtherParty(string channel, string me)
        {
            if (!TryParseDm(channel, out string a, out string b)) return null;
            me = (me ?? "").ToLowerInvariant();
            return me == a ? b : a;
        }
    }
}
