using System;

namespace KMHPatch.Features.Chat
{
    internal static class ChatMediaLabel
    {
        internal static string Kind(string url)
            => LooksLikeGif(url) ? "GIF" : "image";

        internal static string KindOf(string url, bool animated)
            => animated ? (LooksLikeGif(url) ? "GIF" : "animated image") : "image";

        internal static string Badge(string url) => LooksLikeGif(url) ? "GIF" : "ANIM";

        private static bool LooksLikeGif(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            int q = url.IndexOf('?');
            string path = q >= 0 ? url.Substring(0, q) : url;
            return path.EndsWith(".gif", System.StringComparison.OrdinalIgnoreCase);
        }

        // Below this a url does no harm, so it is left alone; the signed Discord links run past 200 characters.
        private const int LongUrlChars = 80;

        internal static string DisplayBody(string body, string mediaUrl, out bool compacted)
        {
            compacted = false;
            string text = (body ?? "").Trim();
            if (text.Length == 0 || string.IsNullOrEmpty(mediaUrl)) return body ?? "";

            if (text.IndexOf(' ') >= 0 || text.IndexOf('\n') >= 0) return body ?? "";
            if (!text.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return body ?? "";

            if (!SameMedia(text, mediaUrl)) return body ?? "";
            if (!IsDirectMediaFile(text)) return body ?? "";           // a share page stays readable
            if (text.Length < LongUrlChars && !IsSigned(text)) return body ?? "";

            compacted = true;
            return Label(text);
        }

        // "file.gif · media.discordapp.net": a recognisable file name and the host a click would contact.
        internal static string Label(string url)
        {
            try
            {
                Uri uri = new Uri(url);
                string[] seg = uri.AbsolutePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                string file = seg.Length > 0 ? seg[seg.Length - 1] : "";
                if (file.Length > 40) file = file.Substring(0, 37) + "...";
                return file.IndexOf('.') > 0 ? $"{file} · {uri.Host}" : uri.Host;
            }
            catch { return "media"; }
        }

        // Host and path only: the vetted url can differ from the body's by a ?format= hint the server appended.
        internal static bool SameMedia(string a, string b)
        {
            try
            {
                Uri ua = new Uri(a), ub = new Uri(b);
                return string.Equals(ua.Host, ub.Host, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(ua.AbsolutePath, ub.AbsolutePath, StringComparison.Ordinal);
            }
            catch { return false; }
        }

        private static readonly string[] MediaExtensions =
            { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".mp4", ".webm", ".mov", ".mkv", ".m4v" };

        internal static bool IsDirectMediaFile(string url)
        {
            try
            {
                string path = new Uri(url).AbsolutePath.ToLowerInvariant();
                foreach (string ext in MediaExtensions)
                    if (path.EndsWith(ext, StringComparison.Ordinal)) return true;
                return false;
            }
            catch { return false; }
        }

        private static bool IsSigned(string url)
            => url.IndexOf("hm=", StringComparison.OrdinalIgnoreCase) >= 0
            && url.IndexOf("ex=", StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
