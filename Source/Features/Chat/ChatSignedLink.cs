using System;
using System.Globalization;

namespace KMHPatch.Features.Chat
{
    // Only stops the client offering a retry that cannot succeed, since a message with no media reference has nothing to ask.
    internal static class ChatSignedLink
    {
        internal static DateTime? ExpiryUtc(string url)
        {
            string ex = QueryValue(url, "ex");
            if (string.IsNullOrEmpty(ex)) return null;
            if (!long.TryParse(ex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long seconds)) return null;
            if (seconds <= 0) return null;
            try { return DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime; }
            catch { return null; }
        }

        internal static bool IsExpired(string url, DateTime utcNow)
        {
            DateTime? expiry = ExpiryUtc(url);
            return expiry.HasValue && expiry.Value <= utcNow;
        }

        internal static bool IsExpired(string url) => IsExpired(url, DateTime.UtcNow);

        private static string QueryValue(string url, string key)
        {
            if (string.IsNullOrEmpty(url)) return "";
            int q = url.IndexOf('?');
            if (q < 0) return "";
            foreach (string part in url.Substring(q + 1).Split('&'))
            {
                int eq = part.IndexOf('=');
                if (eq <= 0) continue;
                if (string.Equals(part.Substring(0, eq), key, StringComparison.OrdinalIgnoreCase))
                    return part.Substring(eq + 1);
            }
            return "";
        }
    }
}
