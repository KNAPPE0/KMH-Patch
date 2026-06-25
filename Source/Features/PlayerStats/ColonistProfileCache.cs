using System;
using System.Collections.Generic;
using KMHPatch.Features.PlayerStats.Dto;

namespace KMHPatch.Features.PlayerStats
{
    // Caches on-demand colonist profiles so player cards can render once the detail reply lands.
    public static class ColonistProfileCache
    {
        private static readonly Dictionary<string, ColonistProfile> _byUser
            = new Dictionary<string, ColonistProfile>(StringComparer.OrdinalIgnoreCase);

        // Fires when any colonist detail arrives (open cards re-read their entry).
        public static event Action Updated;

        internal static void Apply(string username, ColonistProfile detail)
        {
            if (string.IsNullOrEmpty(username)) return;
            _byUser[username] = detail;   // may be null = "no colonist for this player"
            try { Updated?.Invoke(); } catch (Exception ex) { Diagnostics.KmhLog.Warn($"Colonist cache subscriber threw: {ex.Message}"); }
        }

        // True once a reply (even an empty one) has arrived for this user.
        public static bool Has(string username) => !string.IsNullOrEmpty(username) && _byUser.ContainsKey(username);

        public static ColonistProfile Get(string username)
            => !string.IsNullOrEmpty(username) && _byUser.TryGetValue(username, out ColonistProfile d) ? d : null;

        internal static void Clear() => _byUser.Clear();
    }
}
