using System;

namespace KMHPatch.UI
{
    // Canonical "who am I / is this me" helpers. Username matching is case-insensitive everywhere on the server, so a
    // single Same/IsMe here replaces the per-dialog `me = SessionHandler.Username ?? ""` + local `Eq(x,y)` copies and
    // removes the risk of a stray case-sensitive comparison slipping in.
    internal static class KmhSession
    {
        // Local player's username, never null ("" when not signed in).
        public static string Me => SessionHandler.Username ?? "";

        // Case-insensitive username equality. False when the candidate is empty, so two "unknown" users never match.
        public static bool Same(string a, string b)
            => !string.IsNullOrEmpty(a) && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        // True when the given username is the local player.
        public static bool IsMe(string username) => Same(username, Me);
    }
}
