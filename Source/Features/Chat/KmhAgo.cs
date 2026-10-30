using System;

namespace KMHPatch.Features.Chat
{
    internal static class KmhAgo
    {
        internal static string Since(long utcTicks)
        {
            if (utcTicks <= 0) return "";
            try
            {
                double seconds = (DateTime.UtcNow - new DateTime(utcTicks, DateTimeKind.Utc)).TotalSeconds;
                if (seconds < 60d) return UI.KmhText.Get("KMH_Ago_JustNow", "just now");
                return UI.KmhText.Get("KMH_Ago_Ago", "{0} ago", Span((long)seconds));
            }
            catch { return ""; }
        }

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
