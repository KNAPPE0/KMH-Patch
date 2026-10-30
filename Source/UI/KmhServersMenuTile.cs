using System;
using KMHPatch.Features.Global;
using UnityEngine;
using Verse;

namespace KMHPatch.UI
{
    // Reads the last completed refresh and never waits on Global: a directory timeout must not freeze the menu.
    internal static class KmhServersMenuTile
    {
        private static DateTime _lastKickUtc;

        internal static string Tooltip()
        {
            switch (KmhDirectoryClient.State)
            {
                case GlobalLinkState.Online:
                    return KmhText.Get("KMH_Browser_Tile_Tip",
                        "Browse Colony Nexus servers: see which ones your mods and versions match, and join without typing an address.");
                case GlobalLinkState.Offline:
                    return KmhText.Get("KMH_Browser_Tile_Tip_Offline",
                        "Colony Nexus Global cannot be reached right now, so this list is whatever was last saved. Servers you have "
                      + "joined before are still in Recent, and direct connect still works.");
                case GlobalLinkState.RateLimited:
                    return KmhText.Get("KMH_Browser_Tile_Tip_RateLimited",
                        "Colony Nexus Global asked this client to slow down, so the list refreshes again shortly. Nothing is wrong "
                      + "with your connection and joining still works.");
                case GlobalLinkState.Degraded:
                    return KmhText.Get("KMH_Browser_Tile_Tip_Degraded",
                        "Colony Nexus Global answered but could not serve the list. Servers you have joined before are still in "
                      + "Recent, and direct connect still works.");
                default:
                    return KmhText.Get("KMH_Browser_Tile_Tip_Connecting", "Asking Colony Nexus Global which servers are up…");
            }
        }

        // Refreshed on a timer from the draw call, but only ever as a fire-and-forget background task.
        internal static void Kick()
        {
            if ((DateTime.UtcNow - _lastKickUtc).TotalSeconds < 60) return;
            _lastKickUtc = DateTime.UtcNow;
            KmhDirectoryClient.LoadCacheIfEmpty();
            // Idempotent; the socket keeps the directory current, the polled refresh covers a client that cannot hold one.
            Features.Global.KmhDirectoryStream.Start();
            KmhDirectoryClient.RefreshSummaryAsync();
        }

        internal static Color StatusColorPublic() => StatusColor();

        private static Color StatusColor()
        {
            switch (KmhDirectoryClient.State)
            {
                case GlobalLinkState.Online:      return new Color(0.45f, 0.85f, 0.45f);
                case GlobalLinkState.Offline:     return new Color(0.85f, 0.55f, 0.45f);
                // Amber, not red: the directory is answering.
                case GlobalLinkState.RateLimited: return new Color(0.90f, 0.78f, 0.35f);
                case GlobalLinkState.Degraded:    return new Color(0.90f, 0.70f, 0.40f);
                default:                          return new Color(0.75f, 0.75f, 0.55f);
            }
        }

        public static string StatusLine()
        {
            switch (KmhDirectoryClient.State)
            {
                case GlobalLinkState.Online:      return "● " + KmhText.Get("KMH_Browser_Status_GlobalOnline", "Global online");
                case GlobalLinkState.Offline:     return "● " + KmhText.Get("KMH_Browser_Status_GlobalOffline", "Global offline");
                case GlobalLinkState.RateLimited: return "● " + KmhText.Get("KMH_Browser_Status_GlobalBusy", "Global busy - retrying");
                case GlobalLinkState.Degraded:    return "● " + KmhText.Get("KMH_Browser_Status_GlobalDegraded", "Global degraded");
                default:                          return "● " + KmhText.Get("KMH_Browser_Status_Connecting", "Connecting…");
            }
        }

        // Counted from the rows on screen, not from the totals: "2 you can join" has to mean these two.
        public static int JoinableCount()
        {
            int n = 0;
            foreach (ServerCardDto c in KmhDirectoryClient.Cards)
                if (!KmhCompatibility.IsBlocking(KmhCompatibility.Evaluate(c))) n++;
            return n;
        }

        public static string CountsLine() => CountsLine(true);

        private static string CountsLine(bool withJoinable)
        {
            // Never invented: an unfinished first refresh says so rather than showing a confident zero.
            if (KmhDirectoryClient.State != GlobalLinkState.Online)
                return KmhDirectoryClient.Cards.Count > 0
                    ? KmhText.Get("KMH_Browser_CachedResults", "Showing saved results")
                    : "";

            int servers = KmhDirectoryClient.ServerCount, players = KmhDirectoryClient.PlayerCount;
            // The guarded Get: a translation with a stray brace must not throw out of the main menu every frame.
            string line = (servers == 1 ? KmhText.Get("KMH_Browser_ServerOne", "{0} server", servers)
                                        : KmhText.Get("KMH_Browser_ServerMany", "{0} servers", servers))
                        + "  ·  "
                        + (players == 1 ? KmhText.Get("KMH_Browser_PlayerOne", "{0} player", players)
                                        : KmhText.Get("KMH_Browser_PlayerMany", "{0} players", players));

            int joinable = withJoinable ? JoinableCount() : 0;
            if (joinable > 0)
                line += "  ·  " + KmhText.Get("KMH_Browser_Counts_Joinable", "{0} you can join", joinable);
            return line;
        }

        // The longest form that fits the current font; the dot's colour already says whether Global is up.
        public static string CardLine(float width, out string full)
        {
            string status = StatusLine(), counts = CountsLine(true);
            full = string.IsNullOrEmpty(counts) ? status : status + "  ·  " + counts;
            if (string.IsNullOrEmpty(counts) || Text.CalcSize(full).x <= width) return full.Truncate(width);
            string brief = "● " + counts, briefest = "● " + CountsLine(false);
            if (Text.CalcSize(brief).x <= width) return brief;
            return briefest.Truncate(width);
        }

        // For the test driver, which runs outside the card's own font.
        public static string CardLineForTest(float width)
        {
            GameFont was = Text.Font;
            Text.Font = GameFont.Tiny;
            try { return CardLine(width, out _); }
            finally { Text.Font = was; }
        }
    }
}
