using System;
using System.Collections.Generic;
using KMHPatch.Features.LinkedAccounts.Dto;

namespace KMHPatch.Features.LinkedAccounts
{
    // Every dialog rendering a username calls Format(), so linked players look the same everywhere.
    public static class LinkedAccountsCache
    {
        // RWT usernames are case-preserving but compared case-insensitively, and the server's casing may differ.
        private static Dictionary<string, string> _links
            = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static DateTime LastUpdatedUtc { get; private set; } = DateTime.MinValue;

        public static bool HasSnapshot { get; private set; } = false;

        public static event Action Updated;

        public static bool IsLinked(string username)
        {
            return !string.IsNullOrEmpty(username)
                && _links.TryGetValue(username, out string d)
                && !string.IsNullOrEmpty(d);
        }

        public static string DiscordNameFor(string username)
        {
            if (string.IsNullOrEmpty(username)) return null;
            return _links.TryGetValue(username, out string d) ? d : null;
        }

        // DM channel ids are lowercased by construction, so a name parsed back out of one needs its casing from the roster.
        public static string Canonical(string username)
        {
            if (string.IsNullOrEmpty(username)) return username;
            try
            {
                var all = PlayerStats.PlayerStatsCache.Entries;
                if (all != null)
                    foreach (var e in all)
                        if (e != null && string.Equals(e.Username, username, System.StringComparison.OrdinalIgnoreCase))
                            return e.Username;
            }
            catch { }
            return username;   // roster not loaded yet - show what we have rather than nothing
        }

        public static string Format(string username)
        {
            if (string.IsNullOrEmpty(username)) return "<color=grey>(unknown)</color>";
            username = Canonical(username);
            // Here rather than at each render site, so "who is staff" is answerable everywhere instead of only in chat.
            string badge = Identity.KmhStaff.Tag(username);
            // From the theme, not a literal, or recolouring the Discord mark gives two blues for one idea on one screen.
            string name = IsLinked(username)
                        ? $"<color={UI.KmhTheme.Hex(UI.KmhTheme.DiscordSrc)}>{username}</color>"
                        : username;
            return badge + name;
        }

        internal static void Apply(LinkedAccountsSnapshot snapshot)
        {
            // Replace wholesale - server sends the full map on every push, never deltas, so we don't need to merge
            _links = snapshot?.Links != null
                ? new Dictionary<string, string>(snapshot.Links, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            LastUpdatedUtc = DateTime.UtcNow;
            HasSnapshot    = true;

            KmhCacheEvents.Raise(Updated, "Linked accounts");
        }

        internal static void Clear()
        {
            _links         = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            LastUpdatedUtc = DateTime.MinValue;
            HasSnapshot    = false;
        }
    }
}
