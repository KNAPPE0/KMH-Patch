using System;
using System.Collections.Generic;
using KMHPatch.Features.PlayerStats.Dto;

namespace KMHPatch.Features.PlayerStats
{
    public static class ColonistProfileCache
    {
        private static readonly Dictionary<string, ColonistProfile> _byUser
            = new Dictionary<string, ColonistProfile>(StringComparer.OrdinalIgnoreCase);

        public static event Action Updated;

        internal static void Apply(string username, ColonistProfile detail)
        {
            if (string.IsNullOrEmpty(username)) return;
            _byUser[username] = detail;   // null: this player has no colonist
            KmhCacheEvents.Raise(Updated, "Colonist profile");
        }

        public static bool Has(string username) => !string.IsNullOrEmpty(username) && _byUser.ContainsKey(username);

        public static ColonistProfile Get(string username)
            => !string.IsNullOrEmpty(username) && _byUser.TryGetValue(username, out ColonistProfile d) ? d : null;

        internal static void Clear() => _byUser.Clear();
    }
}
