using System;

namespace KMHPatch.Features.Chat
{
    // "3h ago", "12m" - times a person reads at a glance. Pure, so the rounding is pinned rather than eyeballed.
    internal static class KmhAgo
    {
        internal static string Since(long utcTicks)
        {
            if (utcTicks <= 0) return "";
            try
            {
                double seconds = (DateTime.UtcNow - new DateTime(utcTicks, DateTimeKind.Utc)).TotalSeconds;
                if (seconds < 0d) return "just now";
                return seconds < 60d ? "just now" : Span((long)seconds) + " ago";
            }
            catch { return ""; }
        }

        // Whole units only: "2h 30m" is more than anyone reads off a roster row.
        internal static string Span(long seconds)
        {
            if (seconds < 60) return seconds <= 0 ? "0m" : "1m";
            long minutes = seconds / 60;
            if (minutes < 60) return minutes + "m";
            long hours = minutes / 60;
            if (hours < 24) return hours + "h";
            long days = hours / 24;
            return days < 365 ? days + "d" : (days / 365) + "y";
        }
    }
}
