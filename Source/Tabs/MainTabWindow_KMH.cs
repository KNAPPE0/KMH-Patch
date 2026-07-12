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
    // Single in-game entry point for KMH: connection status, dashboard, and feature buttons.
    // The window stays compact, but the whole body scrolls so lower feature buttons can load below.
    public class MainTabWindow_KMH : MainTabWindow
    {
        public override Vector2 RequestedTabSize
            => new Vector2(600f, Mathf.Min(640f, Verse.UI.screenHeight - 120f));

        public override void PreOpen()
        {
            base.PreOpen();
            try { KmhRefresh.RequestAll(); } catch { }
        }

        private Vector2 _scroll;
        private float _viewHeight = 1040f;   // seed; self-corrects to the measured content height after the first frame
        private bool _drawErrorLogged;
        private bool _dashErrorLogged;

        public override void DoWindowContents(Rect rect)
        {
            try
            {
                DrawWholeTabScroll(rect);
            }
            catch (System.Exception ex)
            {
                if (!_drawErrorLogged)
                {
                    _drawErrorLogged = true;
                    KMHPatch.Diagnostics.KmhLog.Error($"KMH tab draw failed: {ex}");
                }

                Text.Anchor = TextAnchor.UpperLeft;
                Text.Font = GameFont.Medium;
                Widgets.Label(new Rect(0f, 0f, rect.width, 32f), Constants.DisplayName);
                Text.Font = GameFont.Small;
                Widgets.Label(new Rect(0f, 38f, rect.width, rect.height - 38f),
                    "<color=#ff8080>The KMH tab hit an error and couldn't draw. See KMH log.</color>");
            }
        }

        private void DrawWholeTabScroll(Rect rect)
        {
            // View height tracks the content measured LAST frame, so there is no dead space when the body is short
            // and no clipping when it's tall. The value only changes between frames, so BeginScrollView gets the
            // same viewRect on this frame's Layout and Repaint passes (a mid-frame change is what desynced IMGUI and
            // vanished buttons before). Content is deterministic, so it settles in one frame with no visible jitter.
            float viewH = Mathf.Max(_viewHeight, rect.height);
            Rect viewRect = new Rect(0f, 0f, rect.width - DialogLayout.ScrollbarReserveWidth, viewH);

            _scroll.y = Mathf.Clamp(_scroll.y, 0f, Mathf.Max(0f, viewH - rect.height));

            Widgets.BeginScrollView(rect, ref _scroll, viewRect);

            Listing_Standard listing = new Listing_Standard();
            listing.Begin(viewRect);

            float measured = viewH;
            try
            {
                DrawFullBody(listing, rect);
            }
            catch (System.Exception ex)
            {
                if (!_drawErrorLogged)
                {
                    _drawErrorLogged = true;
                    KMHPatch.Diagnostics.KmhLog.Error($"KMH tab body draw failed: {ex}");
                }

                listing.Label("<color=#ff8080>KMH tab failed to finish drawing. See KMH log.</color>");
            }
            finally
            {
                measured = listing.CurHeight;
                listing.End();
                Widgets.EndScrollView();
            }

            // Feed the measured height forward for next frame (+6 keeps the last button off the bottom edge).
            _viewHeight = measured + 6f;
        }

        private void DrawFullBody(Listing_Standard listing, Rect rect)
        {
            Text.Font = GameFont.Medium;
            listing.Label(Constants.DisplayName);
            Text.Font = GameFont.Small;

            listing.Gap(6f);
            DrawConnectionStatus(listing);

            if (KmhDispatcher.IsKmhServer)
            {
                listing.Gap(8f);

                try
                {
                    KMHDashboard.Draw(listing, rect);
                }
                catch (System.Exception ex)
                {
                    if (!_dashErrorLogged)
                    {
                        _dashErrorLogged = true;
                        KMHPatch.Diagnostics.KmhLog.Error($"KMH dashboard draw failed: {ex}");
                    }

                    listing.Label("<color=#E2C16B>(dashboard unavailable - see KMH log)</color>");
                }

                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
            }

            listing.GapLine(12f);

            Text.Font = GameFont.Medium;
            listing.Label("Features");
            Text.Font = GameFont.Small;
            listing.Gap(4f);

            DrawFeatureButtons(listing);
        }

        private void DrawFeatureButtons(Listing_Standard listing)
        {
            if (KmhDispatcher.IsKmhServer || KmhDashboardState.KmhConfirmed)
            {
                if (!KmhDispatcher.IsKmhServer)
                    listing.Label("<color=#E2C16B>Reconnecting to KMH… showing your last known features.</color>");

                SafeButton(listing, "Guild Hall", () =>
                {
                    if (FeatureButton(listing, "guilds", KMHTextures.Guild, "Guild Hall",
                            "Manage your guild - members, ranks, alliances, perks, MOTD."))
                        Find.WindowStack.Add(new Dialog_KMHGuildHall());
                });

                SafeButton(listing, "Quest Board", () =>
                {
                    if (FeatureButton(listing, "quests", KMHTextures.Quests, "Quest Board",
                            "Browse, claim, post, and submit cross-server quests."))
                        Find.WindowStack.Add(new Dialog_KMHQuestBoard());
                });

                SafeButton(listing, "Marketplace", () =>
                {
                    if (FeatureButton(listing, "marketplace", KMHTextures.Marketplace, "Marketplace",
                            "Browse and buy from open listings; post your own from a selected caravan."))
                        Find.WindowStack.Add(new Dialog_KMHMarketplace());
                });

                SafeButton(listing, "Treasury", () =>
                {
                    if (FeatureButton(listing, "treasury", KMHTextures.Treasury, "Treasury",
                            "View your personal or guild treasury; deposit and withdraw silver and items."))
                        Find.WindowStack.Add(new Dialog_KMHTreasury());
                });

                SafeButton(listing, "Sites", () =>
                {
                    if (IconButton.DrawListingButton(listing, KMHTextures.Marketplace, "Sites",
                            tooltip: "Build custom production sites, join others as a worker, and set reward destinations."))
                        Find.WindowStack.Add(new Features.Sites.Dialog_KMHSites());
                });

                SafeButton(listing, "Server Standings", () =>
                {
                    if (FeatureButton(listing, "standings", KMHTextures.Leaderboard, "Server Standings",
                            "Player & guild standings plus colony, colonist, trade, contract, battle, site and reputation records."))
                        Find.WindowStack.Add(new Features.Standings.Dialog_KMHStandings());
                });

                SafeButton(listing, "Send KMH ping", () =>
                {
                    if (IconButton.DrawListingButton(listing, KMHTextures.Ping, "Send KMH ping",
                            tooltip: "Diagnostic round-trip through the KMH sub-protocol. Result lands in the KMH log."))
                        KmhHandshakeHandler.SendPing();
                });

                SafeButton(listing, "Config Enforcement", () =>
                {
                    if (IconButton.DrawListingButton(listing, KMHTextures.About, "Config Enforcement",
                            tooltip: "Admin: lock players' Mod Options to the server and manage the safe-mods list."))
                        Find.WindowStack.Add(new Features.Enforcement.Dialog_KMHEnforcement());
                });

                SafeButton(listing, "Link Discord", () =>
                {
                    if (!Features.LinkedAccounts.LinkedAccountsCache.IsLinked(KmhSession.Me)
                        && IconButton.DrawListingButton(listing, KMHTextures.About, "Link Discord",
                            tooltip: "Get a one-time code to link your Discord account - no chat commands needed."))
                        Features.LinkedAccounts.LinkedAccountsHandler.RequestCode();
                });
            }
            else
            {
                listing.Label("Connect to a KMH-enabled server to access KMH features.");
            }

            listing.GapLine(12f);

            SafeButton(listing, "How KMH works", () =>
            {
                if (IconButton.DrawListingButton(listing, KMHTextures.About, "How KMH works (tutorial)",
                        tooltip: "Plain-language guide to every KMH system: treasury, marketplace, auctions, wants, quests, sites, guilds, events, standings."))
                    Find.WindowStack.Add(new Dialog_KMHTutorial());
            });

            SafeButton(listing, "Open KMH About", () =>
            {
                if (IconButton.DrawListingButton(listing, KMHTextures.About, "Open KMH About",
                        tooltip: "Welcome dialog with KMH overview + links."))
                    Find.WindowStack.Add(new Dialog_KMHAbout());
            });

            SafeButton(listing, "View KMH log", () =>
            {
                if (IconButton.DrawListingButton(listing, KMHTextures.Log, "View KMH log",
                        tooltip: "Open the in-game log viewer scoped to KMH messages."))
                    Find.WindowStack.Add(new Dialog_KMHLogViewer());
            });

            SafeButton(listing, "KMH Servers", () =>
            {
                if (IconButton.DrawListingButton(listing, KMHTextures.About, "KMH Servers",
                        tooltip: "KMH servers you've connected to, with their KMH and RWT versions."))
                    Find.WindowStack.Add(new Features.Servers.Dialog_KMHServers());
            });
        }

        private static readonly System.Collections.Generic.HashSet<string> _buttonErrors
            = new System.Collections.Generic.HashSet<string>();

        private static void SafeButton(Listing_Standard listing, string label, System.Action draw)
        {
            try
            {
                draw();
            }
            catch (System.Exception ex)
            {
                if (_buttonErrors.Add(label))
                    KMHPatch.Diagnostics.KmhLog.Error($"KMH tab button '{label}' failed to draw: {ex}");

                listing.Label($"<color=#ff8080>{label} - failed to draw (see KMH log)</color>");
            }

            listing.Gap(6f);
        }

        private static bool FeatureButton(Listing_Standard listing, string feature, Texture2D icon, string label, string tooltip)
        {
            if (!Features.KmhFeatures.IsEnabled(feature))
            {
                listing.Label($"<color=#9A9A9A>{label} - disabled by server</color>");
                return false;
            }

            return IconButton.DrawListingButton(listing, icon, label, tooltip: tooltip);
        }

        private static void DrawConnectionStatus(Listing_Standard listing)
        {
            bool isConnected = SessionHandler.CurrentNetworkState == ClientNetworkState.Connected;
            string endpoint = string.IsNullOrEmpty(Network.Ip)
                ? "(unknown)"
                : $"{Network.Ip}:{Network.Port}";

            if (!isConnected)
            {
                listing.Label("<color=grey>● Not connected to any server</color>");
                return;
            }

            if (KmhDispatcher.IsKmhServer)
            {
                string serverLabel = string.IsNullOrEmpty(KmhDispatcher.ServerName)
                    ? endpoint
                    : $"'{KmhDispatcher.ServerName}' ({endpoint})";

                listing.Label(
                    $"<color=#7CD37C>● Connected to KMH server {serverLabel} (protocol v{KmhDispatcher.ServerProtocolVersion})</color>"
                );

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