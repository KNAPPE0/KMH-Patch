using GameClient.Misc;
using KMHPatch.Dialogs;
using KMHPatch.Features.Guilds;
using KMHPatch.Features.Marketplace;
using KMHPatch.Features.PlayerStats;
using KMHPatch.Features.Quests;
using KMHPatch.Features.Treasury;
using KMHPatch.SubProtocol;
using KMHPatch.UI;
using RimWorld;
using UnityEngine;
using Verse;
using static GameClient.Hooks.TCPNetwork.ClientNetwork;

namespace KMHPatch.Tabs
{
    // Single in-game entry point for KMH: live connection status, the live dashboard, and a button per feature
    // (gated on IsKmhServer). Wired via 1.6/Defs/MainButtonDefs/KMH.xml -> <tabWindowClass>
    public class MainTabWindow_KMH : MainTabWindow
    {
        public override Vector2 RequestedTabSize => new Vector2(600f, 560f);

        private Vector2 _scroll;
        private float   _viewHeight = 800f; // updated on Layout only - see below
        private bool    _drawErrorLogged;   // one-shot so a draw failure can't flood the log

        public override void DoWindowContents(Rect rect)
        {
            // whole tab scrolls so the growing dashboard can't push buttons off; height updates ONLY on Layout -
            // changing it between Layout and Repaint blanks the window
            Rect viewRect = new Rect(0f, 0f, rect.width - 16f, _viewHeight);
            Widgets.BeginScrollView(rect, ref _scroll, viewRect);

            Listing_Standard listing = new Listing_Standard();
            listing.Begin(viewRect);

            // Guard the whole body: a single feature/dashboard draw failure must never blank the tab or crash the
            // game. End()/EndScrollView() still run below so the GUI groups stay balanced
            try
            {

            Text.Font = GameFont.Medium;
            listing.Label(Constants.DisplayName);
            Text.Font = GameFont.Small;

            listing.Gap(6f);
            DrawConnectionStatus(listing);

            // Live dashboard - only shown when connected to a KMH server, since every metric pulls from a cache
            // that stays empty until a snapshot push arrives
            if (KmhDispatcher.IsKmhServer)
            {
                listing.Gap(8f);
                KMHDashboard.Draw(listing, rect);
            }

            listing.GapLine(12f);

            Text.Font = GameFont.Medium;
            listing.Label("Features");
            Text.Font = GameFont.Small;
            listing.Gap(4f);

            // Gated on IsKmhServer so we don't surface features a stock server can't serve
            if (KmhDispatcher.IsKmhServer)
            {
                if (IconButton.DrawListingButton(listing, KMHTextures.Guild, "Guild Hall",
                        tooltip: "Manage your guild - members, ranks, alliances, perks, MOTD."))
                {
                    Find.WindowStack.Add(new Dialog_KMHGuildHall());
                }
                listing.Gap(6f);

                if (IconButton.DrawListingButton(listing, KMHTextures.Quests, "Quest Board",
                        tooltip: "Browse, claim, post, and submit cross-server quests."))
                {
                    Find.WindowStack.Add(new Dialog_KMHQuestBoard());
                }
                listing.Gap(6f);

                if (IconButton.DrawListingButton(listing, KMHTextures.Marketplace, "Marketplace",
                        tooltip: "Browse and buy from open listings; post your own from a selected caravan."))
                {
                    Find.WindowStack.Add(new Dialog_KMHMarketplace());
                }
                listing.Gap(6f);

                if (IconButton.DrawListingButton(listing, KMHTextures.Treasury, "Treasury",
                        tooltip: "View your personal or guild treasury; deposit and withdraw silver and items."))
                {
                    Find.WindowStack.Add(new Dialog_KMHTreasury());
                }
                listing.Gap(6f);

                if (IconButton.DrawListingButton(listing, KMHTextures.Marketplace, "Sites",
                        tooltip: "Build custom production sites, join others as a worker, and set reward destinations."))
                {
                    Find.WindowStack.Add(new Features.Sites.Dialog_KMHSites());
                }
                listing.Gap(6f);

                if (IconButton.DrawListingButton(listing, KMHTextures.Leaderboard, "Server Standings",
                        tooltip: "Player & guild standings plus colony, colonist, trade, contract, battle, site and reputation records."))
                {
                    Find.WindowStack.Add(new Features.Standings.Dialog_KMHStandings());
                }
                listing.Gap(6f);

                if (IconButton.DrawListingButton(listing, KMHTextures.Ping, "Send KMH ping",
                        tooltip: "Diagnostic round-trip through the KMH sub-protocol. Result lands in the KMH log."))
                {
                    // result lands in the KMH log as 'Pong received (round-trip N ms)'
                    KmhHandshakeHandler.SendPing();
                }

                // always visible - admin is verified in the dialog + server-side, and a not-yet-op'd host learns
                // how to become admin from it
                listing.Gap(6f);
                if (IconButton.DrawListingButton(listing, KMHTextures.About, "Config Enforcement",
                        tooltip: "Admin: lock players' Mod Options to the server and manage the safe-mods list."))
                {
                    Find.WindowStack.Add(new Features.Enforcement.Dialog_KMHEnforcement());
                }
            }
            else
            {
                listing.Label("Connect to a KMH-enabled server to access KMH features.");
            }

            listing.GapLine(12f);

            if (IconButton.DrawListingButton(listing, KMHTextures.About, "Open KMH About",
                    tooltip: "Welcome dialog with KMH overview + links."))
            {
                Find.WindowStack.Add(new Dialog_KMHAbout());
            }

            listing.Gap(6f);
            if (IconButton.DrawListingButton(listing, KMHTextures.Log, "View KMH log",
                    tooltip: "Open the in-game log viewer scoped to KMH messages."))
            {
                Find.WindowStack.Add(new Dialog_KMHLogViewer());
            }

            }
            catch (System.Exception ex)
            {
                if (!_drawErrorLogged)
                {
                    _drawErrorLogged = true;
                    KMHPatch.Diagnostics.KmhLog.Error($"KMH tab draw failed: {ex}");
                }
                listing.Label("<color=#ff8080>The KMH tab hit an error and couldn't finish drawing. See the KMH log.</color>");
            }

            float contentH = listing.CurHeight;
            listing.End();
            Widgets.EndScrollView();

            // Resize the scroll content to fit, but ONLY on Layout so viewRect is identical between the Layout and
            // Repaint passes of the same frame. (Resizing every frame is what blanked the tab before.)
            if (Event.current.type == EventType.Layout)
                _viewHeight = contentH + 16f;
        }

        private static void DrawConnectionStatus(Listing_Standard listing)
        {
            // Read RWT's session state - this stays valid even when KmhDispatcher hasn't been initialized yet
            // (which can happen briefly during the first frame after connect)
            bool   isConnected = SessionHandler.CurrentNetworkState == ClientNetworkState.Connected;
            string endpoint    = string.IsNullOrEmpty(Network.Ip)
                ? "(unknown)"
                : $"{Network.Ip}:{Network.Port}";

            if (!isConnected)
            {
                listing.Label("<color=grey>● Not connected to any server</color>");
                return;
            }

            if (KmhDispatcher.IsKmhServer)
            {
                listing.Label(
                    $"<color=#7CD37C>● Connected to KMH server {endpoint} (protocol v{KmhDispatcher.ServerProtocolVersion})</color>"
                );

                // Tiny diagnostics line - handy when the server addon is being built/tested, harmless otherwise
                if (!string.IsNullOrEmpty(KmhDispatcher.LastReceivedKind))
                {
                    System.TimeSpan since = System.DateTime.UtcNow - KmhDispatcher.LastReceivedAt;
                    listing.Label(
                        $"<color=grey>Last KMH packet: '{KmhDispatcher.LastReceivedKind}' " +
                        $"({(int)since.TotalSeconds}s ago, {KmhDispatcher.ReceivedCount} total this session)</color>"
                    );
                }
            }
            else
            {
                listing.Label($"<color=#D3D37C>● Connected to {endpoint} - no KMH handshake</color>");
                listing.Label(
                    "<color=grey>This is normal for stock RimWorld Together servers. " +
                    "KMH-specific features stay hidden until the server completes the handshake.</color>"
                );
            }
        }
    }
}
