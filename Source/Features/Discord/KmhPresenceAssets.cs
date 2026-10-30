using System;
using System.Collections.Generic;
using System.Linq;

namespace KMHPatch.Features.Discord
{
    // Which art keys Discord confirmed, learned from its own replies; nothing is fetched to find out.
    internal static class KmhPresenceAssets
    {
        private static readonly object Gate = new object();
        private static readonly Dictionary<string, bool> _found = new Dictionary<string, bool>(StringComparer.Ordinal);

        internal static IEnumerable<string> IconKeys()
        {
            foreach (PresenceIcon icon in Enum.GetValues(typeof(PresenceIcon)))
                if (icon != PresenceIcon.None) yield return KmhPresenceCatalog.AssetFor(icon);
        }

        // Runs on the library's thread; the reply names an uploaded key by its numeric id.
        internal static void NoteEcho(string large, string small)
        {
            KmhDiscordRpc.Presence sent = KmhRichPresence.LastSent;
            if (sent == null) return;
            lock (Gate)
            {
                if (!string.IsNullOrEmpty(sent.LargeImageKey)) _found[sent.LargeImageKey] = (large ?? "").StartsWith("id:", StringComparison.Ordinal);
                if (!string.IsNullOrEmpty(sent.SmallImageKey)) _found[sent.SmallImageKey] = (small ?? "").StartsWith("id:", StringComparison.Ordinal);
            }
        }

        internal static bool? Found(string key)
        {
            lock (Gate) return _found.TryGetValue(key ?? "", out bool f) ? f : (bool?)null;
        }

        internal static string StatusLine()
        {
            bool? logo = Found(KmhRichPresence.LargeAsset);
            if (logo == null) return UI.KmhText.Get("KMH_RP_ArtUnknown", "not checked yet - Discord confirms each picture the first time it is shown");
            if (logo == false) return UI.KmhText.Get("KMH_RP_ArtNoIds", "Discord did not confirm the logo, so the pictures cannot be checked from here");
            List<string> keys = IconKeys().ToList();
            int found = keys.Count(k => Found(k) == true);
            int missing = keys.Count(k => Found(k) == false);
            return UI.KmhText.Get("KMH_RP_ArtStatus", "logo found; activity pictures: {0} found, {1} missing, {2} not shown yet",
                                  found, missing, keys.Count - found - missing);
        }

        internal static void ResetForTest() { lock (Gate) _found.Clear(); }
    }
}
