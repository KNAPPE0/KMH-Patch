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

namespace KMHPatch.Tabs
{
    public class MainTabWindow_KMH : MainTabWindow
    {
        // Both axes yield to the screen, or on a narrow display the tab itself is off-screen and scrolling cannot help.
        public override Vector2 RequestedTabSize
            => new Vector2(Mathf.Clamp(Verse.UI.screenWidth - 40f, MinTabWidth, MaxTabWidth),
                           Mathf.Max(MinTabHeight, Mathf.Min(MaxTabHeight, Verse.UI.screenHeight - 120f)));

        internal const float MaxTabHeight = 640f;
        internal const float MinTabHeight = 200f;
        internal const float MaxTabWidth  = 600f;
        internal const float MinTabWidth  = 320f;

        // Pure so the reachability invariant below can be checked without a running game.
        internal static float HeaderHeight(int connectionLines)
            => 32f + 6f + Mathf.Max(0, connectionLines) * 22f + 32f + 4f;   // title, gap, status, "Features", gap

        internal static bool ActionsReachableWithoutScrolling(float panelHeight, int connectionLines, int buttonsWanted)
            => HeaderHeight(connectionLines) + Mathf.Max(0, buttonsWanted) * IconButton.ListingLineHeight <= panelHeight;

        public override void PreOpen()
        {
            base.PreOpen();
            try { KmhRefresh.RequestAll(); } catch { }
        }

        private Vector2 _scroll;
        // BeginScrollView reads _viewHeight and the draw writes _measuredHeight, so the viewRect cannot change mid-frame.
        private float _viewHeight     = 1040f;
        private float _measuredHeight = 1040f;
        private bool _drawErrorLogged;
        private bool _dashErrorLogged;

        // DoWindowContents runs once per EVENT, so a pending toggle is gated on the frame number to apply exactly once.
        private int _lastToggleFrame = -1;

        public override void DoWindowContents(Rect rect)
        {
            try
            {
                if (KmhScroll.NewFrame(ref _lastToggleFrame))
                    _viewHeight = _measuredHeight;

                // Measured rather than assumed: the connection block is 1-4 lines and wraps at narrow widths.
                Listing_Standard header = new Listing_Standard();
                header.Begin(rect);
                Text.Font = GameFont.Medium;
                header.Label(Constants.DisplayName);
                Text.Font = GameFont.Small;
                header.Gap(6f);
                DrawConnectionStatus(header);
                float headerH = header.CurHeight;
                header.End();

                Widgets.DrawLineHorizontal(0f, headerH + 2f, rect.width);

                float bodyY = headerH + HeaderGap;
                float bodyH = rect.height - bodyY;
                if (bodyH >= MinBodyHeight) DrawBodyScroll(new Rect(0f, bodyY, rect.width, bodyH));
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

        private void DrawBodyScroll(Rect rect)
        {
            if (rect.height <= 0f) return;

            // Reserved unconditionally: reserving it only on overflow oscillates, since the bar narrows text and wrapping grows height.
            float viewW = Mathf.Max(1f, rect.width - DialogLayout.ScrollbarReserveWidth);
            float viewH = Mathf.Max(_viewHeight, rect.height);
            Rect viewRect = new Rect(0f, 0f, viewW, viewH);

            // Before drawing: a collapse shrinks content under last frame's offset, which otherwise renders blank.
            _scroll.y = KmhScroll.Clamp(_scroll.y, viewH, rect.height);

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

            _measuredHeight = measured + BottomPad;

            // Again against what was actually measured, so a collapse is honoured without a frame of dead scroll.
            _scroll.y = KmhScroll.Clamp(_scroll.y, Mathf.Max(_measuredHeight, rect.height), rect.height);
        }

        // The window's bottom edge sits on RimWorld's main-button bar, so a flush control reads as cut off.
        private const float BottomPad = 12f;

        private const float HeaderGap = 8f;

        // A tall header on a short window can consume the whole body, and BeginScrollView throws on a negative rect.
        private const float MinBodyHeight = 60f;

        private void DrawFullBody(Listing_Standard listing, Rect rect)
        {

            // Buttons before the status table: the table grows a row per feature and would push the actions under the fold.
            Text.Font = GameFont.Medium;
            listing.Label("Features");
            Text.Font = GameFont.Small;
            listing.Gap(4f);

            DrawFeatureButtons(listing);

            if (KmhDispatcher.IsKmhServer)
            {
                listing.GapLine(12f);

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
                    if (IconButton.DrawListingButton(listing, KMHTextures.Sites, "Sites",
                            tooltip: "Build custom production sites, join others as a worker, and set reward destinations."))
                        Find.WindowStack.Add(new Features.Sites.Dialog_KMHSites());
                });

                // Hidden without the capability: an entry that only ever says "not supported" is worse than no entry.
                SafeButton(listing, "Roadworks", () =>
                {
                    if (!Features.Roadworks.RoadworksHandler.Available) return;
                    if (IconButton.DrawListingButton(listing, KMHTextures.Roadworks, "Roadworks",
                            tooltip: "Plan and track road projects built from your Roadworks site."))
                        Find.WindowStack.Add(new Features.Roadworks.Dialog_KMHRoadworks());
                });

                SafeButton(listing, "Communications", () =>
                {
                    bool on = Features.KmhFeatures.IsEnabled("chat") || Features.KmhFeatures.IsEnabled("mail");
                    if (!on) { listing.Label("<color=#9A9A9A>Communications - disabled by server</color>"); return; }
                    int unread = Features.Chat.ChatCache.TotalUnread() + Features.Mail.MailCache.Unread;
                    string label = unread > 0 ? $"Communications ({unread} unread)" : "Communications";
                    if (IconButton.DrawListingButton(listing, KMHTextures.Comms, label,
                            tooltip: "Live server chat and player mail in one place. Pick mail recipients from the standings roster - online or offline."))
                        Features.Comms.Dialog_KMHComms.Open();
                });

                SafeButton(listing, "World", () =>
                {
                    if (FeatureButton(listing, "world", KMHTextures.World, "World",
                            "Living World: active server-wide events and the full set of co-op / competitive global quests."))
                        Find.WindowStack.Add(new Features.World.Dialog_KMHWorld());
                });

                SafeButton(listing, "Server Standings", () =>
                {
                    if (FeatureButton(listing, "standings", KMHTextures.Leaderboard, "Server Standings",
                            "Player & guild standings plus colony, colonist, trade, contract, battle, site and reputation records."))
                        Find.WindowStack.Add(new Features.Standings.Dialog_KMHStandings());
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

            DrawAdvancedTools(listing);
        }

        // Its own window, not an in-panel expand: that would change the body height the scroll view is sized from.
        private void DrawAdvancedTools(Listing_Standard listing)
        {
            listing.GapLine(12f);

            SafeButton(listing, "Advanced & tools", () =>
            {
                if (IconButton.DrawListingButton(listing, KMHTextures.Log, "Advanced & tools",
                        tooltip: "KMH log, Discord linking, known servers, ping and config enforcement."))
                    Dialogs.Dialog_KMHAdvancedTools.Open();
            });

            if (KmhDispatcher.IsKmhServer && !Features.LinkedAccounts.LinkedAccountsCache.IsLinked(KmhSession.Me))
                listing.Label("<color=#E2C16B>Discord not linked - link it under Advanced & tools.</color>");
        }

        private static readonly System.Collections.Generic.HashSet<string> _buttonErrors
            = new System.Collections.Generic.HashSet<string>();

        private static void SafeButton(Listing_Standard listing, string label, System.Action draw)
        {
            float before = listing.CurHeight;
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

            // Only space a row that drew: these bail early when a feature is off, and blank gaps read as missing buttons.
            if (listing.CurHeight > before) listing.Gap(6f);
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

                // Plain '&': Unity rich text does not decode HTML entities, so an escaped one reaches the player literally.
                if (Diagnostics.KmhDebugUplink.Active)
                    listing.Label("<color=#E2C16B>● Sharing your KMH log with this server - stop it under Advanced & tools.</color>");
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