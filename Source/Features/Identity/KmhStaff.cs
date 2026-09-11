using System;
using System.Collections.Generic;
using UnityEngine;

namespace KMHPatch.Features.Identity
{
    // Read-only presentation with no local override, so editing local files cannot manufacture a badge.
    internal static class KmhStaff
    {
        private sealed class Badge
        {
            public string Label = "";
            public Color  Color = Color.white;
        }

        private static readonly Dictionary<string, Badge> _badges =
            new Dictionary<string, Badge>(StringComparer.OrdinalIgnoreCase);

        // Server switch: the next server's staff are not this one's.
        public static void Clear() { _badges.Clear(); _roles.Clear(); KmhCacheEvents.Bump(); }

        // A malformed entry is skipped, not thrown: a handshake failing on a cosmetic field would kill the session.
        public static void ApplyWire(string wire)
        {
            _badges.Clear();
            // A badge changes every chat line that names its holder, and this arrives on the hello rather than a snapshot.
            KmhCacheEvents.Bump();
            if (string.IsNullOrEmpty(wire)) return;

            foreach (string part in wire.Split(';'))
            {
                if (string.IsNullOrWhiteSpace(part)) continue;
                string[] bits = part.Split(':');
                if (bits.Length < 3) continue;

                string role  = (bits[0] ?? "").Trim().ToLowerInvariant();
                string label = (bits[1] ?? "").Trim();
                if (role.Length == 0 || label.Length == 0) continue;
                // A label is concatenated into rich text, so markup is refused outright rather than stripped.
                if (label.IndexOf('<') >= 0 || label.IndexOf('>') >= 0) continue;
                if (label.Length > 12) label = label.Substring(0, 12);

                Color c = UI.KmhTheme.TryHex(bits[2], out Color parsed) ? parsed : Color.white;
                _badges[role] = new Badge { Label = label, Color = c };
            }
        }

        // Counted for the join log: "no badges" and "badges nobody holds" are different failures.
        public static int RoleCount  => _roles.Count;
        public static int BadgeCount => _badges.Count;

        public static bool IsStaff(string username) => !string.IsNullOrEmpty(RoleOf(username));

        // Only decides whether to show a staff action; the server re-checks every one, so a lie here is simply refused.
        public static bool CanModerate(string username)
        {
            switch (RoleOf(username))
            {
                case "owner": case "developer": case "admin": case "moderator": return true;
                default: return false;
            }
        }

        // Carried on the hello, so a historic message badges correctly even when its author is offline.
        private static readonly Dictionary<string, string> _roles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static void ApplyRoles(string wire)
        {
            _roles.Clear();
            if (string.IsNullOrWhiteSpace(wire)) return;
            foreach (string pair in wire.Split(';'))
            {
                if (string.IsNullOrWhiteSpace(pair)) continue;
                string[] bits = pair.Split(':');
                if (bits.Length < 2) continue;
                string user = (bits[0] ?? "").Trim();
                string role = (bits[1] ?? "").Trim().ToLowerInvariant();
                if (user.Length == 0 || role.Length == 0) continue;
                _roles[user] = role;
            }
        }

        // An unknown name, including an unlinked Discord display name, is not staff, which is the right answer.
        public static string RoleOf(string username)
        {
            if (string.IsNullOrWhiteSpace(username)) return "";

            // The bootstrap map first: it covers every config-listed name, online or not.
            if (_roles.TryGetValue(username.Trim(), out string mapped)) return mapped;

            // Then the snapshot, the only source for a live RWT admin on no list, which needs RWT's own user store.
            try
            {
                var entries = PlayerStats.PlayerStatsCache.Entries;
                if (entries == null) return "";
                foreach (var e in entries)
                    if (e != null && string.Equals(e.Username, username, StringComparison.OrdinalIgnoreCase))
                        return e.StaffRole ?? "";
            }
            catch { }
            return "";
        }

        // Empty for everyone who is not staff, so callers can concatenate it unconditionally.
        public static string Tag(string username, string suffix = " ")
        {
            if (!TryBadgeForRole(RoleOf(username), out string label, out Color c)) return "";
            return $"<color={UI.KmhTheme.Hex(c)}>[{label}]</color>{suffix}";
        }

        // For callers that draw their own rect rather than concatenating rich text.
        public static bool TryBadge(string username, out string label, out Color color)
            => TryBadgeForRole(RoleOf(username), out label, out color);

        // Split from the username lookup so parsing and fallbacks can be checked without a live roster.
        public static bool TryBadgeForRole(string role, out string label, out Color color)
        {
            label = ""; color = Color.white;
            if (string.IsNullOrEmpty(role)) return false;
            if (!_badges.TryGetValue(role, out Badge b) || string.IsNullOrEmpty(b.Label)) return false;
            label = b.Label; color = b.Color;
            return true;
        }

        // Said in words on hover. "Mod" is not self-explanatory to someone who just joined.
        public static string Tooltip(string username)
        {
            string role = RoleOf(username);
            if (string.IsNullOrEmpty(role)) return null;
            switch (role)
            {
                case "owner":     return "Server owner.";
                case "developer": return "Developer on this server's team.";
                case "admin":     return "Server administrator.";
                case "moderator": return "Moderator - can remove messages and act on reports.";
                case "op":        return "Trusted operator on this server.";
                default:          return null;
            }
        }
    }
}
