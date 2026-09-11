using System;
using System.Collections.Generic;

namespace KMHPatch.Features.Roadworks
{
    // Canonical segment identity: order-independent, layer-aware.
    internal static class RoadKeys
    {
        // Mirrored byte-for-byte on the server, or KMH stops recognising its own roads.
        public static string For(int layerA, int tileA, int layerB, int tileB)
        {
            string ka = layerA + ":" + tileA, kb = layerB + ":" + tileB;
            return string.CompareOrdinal(ka, kb) <= 0 ? ka + "|" + kb : kb + "|" + ka;
        }

        // Mirrored byte-for-byte on the server: the client quotes with this, the server charges with it.
        public static List<string> NewSegmentKeys(List<string> routeKeys, HashSet<string> existing)
        {
            var outp = new List<string>();
            if (routeKeys == null) return outp;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string k in routeKeys)
            {
                if (string.IsNullOrEmpty(k)) continue;
                if (!seen.Add(k)) continue;
                if (existing != null && existing.Contains(k)) continue;
                outp.Add(k);
            }
            return outp;
        }
    }
}
