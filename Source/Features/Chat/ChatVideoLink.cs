using System;

namespace KMHPatch.Features.Chat
{
    // Which links are a video on a page. Whether one can also be PLAYED here is ChatYouTube's shorter list, not this.
    internal static class ChatVideoLink
    {
        // Read from the BODY, not the preview image: a thumbnail says nothing about where it came from.
        internal static bool IsWatchPage(string body)
            => !string.IsNullOrEmpty(FirstWatchUrl(body));

        // The first watch url in the body, or "" when there is none.
        internal static string FirstWatchUrl(string body)
        {
            if (string.IsNullOrEmpty(body)) return "";
            foreach (string token in body.Split(' ', '\t', '\n', '\r'))
            {
                string t = token.Trim();
                if (t.Length < 12) continue;
                if (!t.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                    && !t.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) continue;
                if (HostOf(t, out string host) && IsWatchHost(host)) return t;
            }
            return "";
        }

        // Being wrong here only changes a label and a button, never what is fetched, so it stays small not clever.
        private static bool IsWatchHost(string host)
            => host == "youtube.com" || host == "www.youtube.com" || host == "m.youtube.com" || host == "youtu.be"
            || host == "twitch.tv"   || host == "www.twitch.tv"   || host == "clips.twitch.tv"
            || host == "vimeo.com"   || host == "www.vimeo.com";

        private static bool HostOf(string url, out string host)
        {
            host = "";
            try
            {
                if (!Uri.TryCreate(url, UriKind.Absolute, out Uri u) || u == null) return false;
                host = u.Host.ToLowerInvariant();
                return host.Length > 0;
            }
            catch { return false; }
        }

        // What to call the site in a one-line label.
        internal static string SiteOf(string url)
        {
            if (!HostOf(url ?? "", out string host)) return "the web";
            if (host.EndsWith("youtube.com", StringComparison.Ordinal) || host == "youtu.be") return "YouTube";
            if (host.EndsWith("twitch.tv", StringComparison.Ordinal)) return "Twitch";
            if (host.EndsWith("vimeo.com", StringComparison.Ordinal)) return "Vimeo";
            return host;
        }
    }
}
