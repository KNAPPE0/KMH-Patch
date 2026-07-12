using System;
using System.Collections.Generic;
using KMHPatch.Features.LinkedAccounts.Dto;

namespace KMHPatch.Features.LinkedAccounts
{
    // Static client cache of in-game username -> Discord display name. Every
    // dialog that renders a username should call Format() instead of writing the raw username so Discord-linked
    // players get the standard visual treatment everywhere
    public static class LinkedAccountsCache
    {
        // Case-insensitive - server might be inconsistent in casing, and RWT usernames are case-preserving but
        // compared case-insensitively elsewhere in the codebase
        private static Dictionary<string, string> _links
            = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static DateTime LastUpdatedUtc { get; private set; } = DateTime.MinValue;

        // True once at least one snapshot has been received. Format() works either way - just returns the plain
        // username when the cache is empty
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

        // Render a username with the standard Discord-aware treatment: linked users get the Discord brand color,
        // unlinked stay default. Calling Format() at every username render site means a single change here (e.g.
        // adding an icon) ripples through every feature dialog.
        public static string Format(string username)
        {
            if (string.IsNullOrEmpty(username)) return "<color=grey>(unknown)</color>";
            if (!IsLinked(username))            return username;
            // Discord brand blue. Slight saturation drop so it stays readable in the RimWorld UI
            return $"<color=#5865F2>{username}</color>";
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
