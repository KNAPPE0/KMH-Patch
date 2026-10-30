using System;
using KMHPatch.UI;

namespace KMHPatch.SubProtocol
{
    // A server's hello name is only its own claim; a listed card for the address this game dialled is Global's word.
    internal static class KmhServerLabel
    {
        private static string _cached = "", _cachedKey;
        private static int _cachedAt;

        // Main thread only: it reads the directory cards. Drawn every frame, so it is held for two seconds.
        internal static string Current()
        {
            string key = KmhDispatcher.ServerId + "\n" + KmhDispatcher.ServerName;
            int now = Environment.TickCount;
            if (key == _cachedKey && unchecked(now - _cachedAt) < 2000) return _cached;
            Features.Global.ServerCardDto card = Features.Global.GlobalHttp.IsFirstParty ? Features.Discord.KmhRichPresence.CardForCurrentServer() : null;
            _cached = For(card?.Name, KmhDispatcher.ServerName, Features.Catalog.CatalogPushGuard.CurrentEndpoint());
            _cachedKey = key;
            _cachedAt = now;
            return _cached;
        }

        internal static string For(string listed, string claimed, string endpoint)
        {
            if (!string.IsNullOrEmpty(listed)) return listed;
            string name = KmhBrandWords.StripMarks(claimed ?? "");
            if (name.Length > 0 && !KmhBrandWords.ClaimsAuthority(name)) return name;
            return string.IsNullOrEmpty(endpoint) ? KmhText.Get("KMH_Server_Unlisted", "an unlisted server")
                                                  : KmhText.Get("KMH_Server_UnlistedAt", "unlisted server {0}", endpoint);
        }
    }
}
