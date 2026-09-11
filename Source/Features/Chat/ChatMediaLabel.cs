using System;

namespace KMHPatch.Features.Chat
{
    // A body that is ONLY a media url becomes a short label: a signed CDN url runs 200 characters and buries the row.
    internal static class ChatMediaLabel
    {
        // Before the bytes arrive only the url can say: calling a static webp "animated" would be a guess as fact.
        internal static string Kind(string url)
            => LooksLikeGif(url) ? "GIF" : "image";

        // The name once the bytes are in hand, where "animated" is known rather than inferred.
        internal static string KindOf(string url, bool animated)
            => animated ? (LooksLikeGif(url) ? "GIF" : "animated image") : "image";

        // The corner badge on a moving picture. An animated png is not a gif, and saying so on a sticker was wrong.
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

        // The original text, or a compact label when the body is only the media url the preview already shows.
        internal static string DisplayBody(string body, string mediaUrl, out bool compacted)
        {
            compacted = false;
            string text = (body ?? "").Trim();
            if (text.Length == 0 || string.IsNullOrEmpty(mediaUrl)) return body ?? "";

            // The body has to be JUST the url. Any other word and it is someone talking.
            if (text.IndexOf(' ') >= 0 || text.IndexOf('\n') >= 0) return body ?? "";
            if (!text.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return body ?? "";

            if (!SameMedia(text, mediaUrl)) return body ?? "";
            if (!IsDirectMediaFile(text)) return body ?? "";           // a share page stays readable
            if (text.Length < LongUrlChars && !IsSigned(text)) return body ?? "";

            compacted = true;
            return Label(text);
        }

        // "Shiroket-3.gif · media.discordapp.net" - a filename someone might recognise, and who they would be talking to.
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

        // A path ending in a media extension is a CDN blob; anything else could be a link worth reading.
        internal static bool IsDirectMediaFile(string url)
        {
            try
            {
                string path = new Uri(url).AbsolutePath.ToLowerInvariant();
                foreach (string ext in new[] { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".mp4", ".webm", ".mov", ".mkv", ".m4v" })
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
