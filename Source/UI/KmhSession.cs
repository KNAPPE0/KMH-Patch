using System;

namespace KMHPatch.UI
{
    // The server matches usernames case-insensitively, so every comparison must route through here.
    internal static class KmhSession
    {
        public static string Me => SessionHandler.Username ?? "";

        // False when the candidate is empty, so two unknown users never match each other.
        public static bool Same(string a, string b)
            => !string.IsNullOrEmpty(a) && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        public static bool IsMe(string username) => Same(username, Me);
    }
}
