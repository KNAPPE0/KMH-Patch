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
            => TitleRowH + 6f + Mathf.Max(0, connectionLines) * 22f + HeaderGap;

        // Two to a row, so the whole list fits a 720p screen without scrolling.
        internal static bool ActionsReachableWithoutScrolling(float panelHeight, int connectionLines, int buttonsWanted)
            => HeaderHeight(connectionLines) + Mathf.CeilToInt(Mathf.Max(0, buttonsWanted) / 2f) * (TileH + TileGap) <= panelHeight;

        internal const float TitleRowH = 34f, TileH = 32f, TileGap = 6f;

        public override void PreOpen()
        {
            base.PreOpen();
            // Throttled: changes are pushed and windows poll their own, and refreshing all on every open re-parsed the market.
            System.DateTime now = System.DateTime.UtcNow;
            if (TabRefreshDue(_lastRefreshServer, _lastRefreshUtc, KmhDispatcher.ServerId, now))
            {
                _lastRefreshServer = KmhDispatcher.ServerId; _lastRefreshUtc = now;
                try { KmhRefresh.RequestAll(); } catch { }
            }
            // The cached view only: the dashboard row reads it, and asking Global again is the window's job.
            try { Features.Federation.FederationHandler.RequestSnapshot(false); } catch { }
        }

        internal static readonly System.TimeSpan TabRefreshEvery = System.TimeSpan.FromSeconds(60);
        private static string _lastRefreshServer;
        private static System.DateTime _lastRefreshUtc = System.DateTime.MinValue;

        internal static bool TabRefreshDue(string lastServer, System.DateTime lastUtc, string server, System.DateTime now)
            => lastServer != server || now - lastUtc >= TabRefreshEvery;

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
                DrawBrandRow(header.GetRect(TitleRowH));
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
                    KMHPatch.Diagnostics.KmhLog.Error($"Colony Nexus tab draw failed: {ex}");
                }

                Text.Anchor = TextAnchor.UpperLeft;
                Text.Font = GameFont.Medium;
                Widgets.Label(new Rect(0f, 0f, rect.width, 32f), Constants.BrandName);
                Text.Font = GameFont.Small;
                Widgets.Label(new Rect(0f, 38f, rect.width, 48f),
                    "<color=#f07167>" + KmhText.Get("KMH_Tab_DrawError", "The Colony Nexus tab hit an error and couldn't draw. See Colony Nexus log.") + "</color>");
                if (rect.height >= 130f && Widgets.ButtonText(new Rect(0f, 92f, Mathf.Min(260f, rect.width), 32f),
                        KmhText.Get("KMH_Error_Report", "Report this problem")))
                    Features.Reports.KmhReportClient.Open(Features.Reports.ReportKinds.Bug,
                        KmhText.Get("KMH_Error_ReportTitle", "{0} could not draw", "The Colony Nexus tab"), ex.Message);
            }
        }

        private void DrawBodyScroll(Rect rect)
        {
            if (rect.height <= 0f) return;

            // Always reserved: reserving only on overflow oscillates, as the bar narrows text and wrapping grows height.
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
                    KMHPatch.Diagnostics.KmhLog.Error($"Colony Nexus tab body draw failed: {ex}");
                }

                listing.Label("<color=#f07167>" + KmhText.Get("KMH_Tab_DrawFailed", "Colony Nexus tab failed to finish drawing. See Colony Nexus log.") + "</color>");
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

        private static void DrawBrandRow(Rect row)
        {
            KmhBrand.DrawMark(new Rect(row.x, row.y + 1f, 32f, 32f));
            KmhBrand.DrawWordmark(new Rect(row.x + 40f, row.y + 6f, 160f, 22f), Color.white, TextAnchor.MiddleLeft);
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleRight;
            GUI.color = new Color(1f, 1f, 1f, 0.45f);
            Widgets.Label(new Rect(row.x, row.y, row.width - 4f, row.height), BuildTag);
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
        }

        private static readonly string BuildTag = $"{typeof(MainTabWindow_KMH).Assembly.GetName().Version.ToString(3)} · {KmhProtocol.UiBuildTag}";

        private void DrawFullBody(Listing_Standard listing, Rect rect)
        {
            // Buttons before the status table: the table grows a row per feature and would push the actions under the fold.
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
                        KMHPatch.Diagnostics.KmhLog.Error($"Colony Nexus dashboard draw failed: {ex}");
                    }

                    listing.Label("<color=#f0c04f>" + KmhText.Get("KMH_Tab_DashboardUnavailable", "(dashboard unavailable - see Colony Nexus log)") + "</color>");
                }

                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
            }
        }

        // Off: the server turned the feature off; shown greyed so its absence is explained.
        private sealed class Tile
        {
            public string Label, Tip;
            public Texture2D Icon;
            public bool Off;
            public System.Action Open;
        }

        private static Tile Feature(string feature, Texture2D icon, string label, string tip, System.Action open)
            => new Tile { Label = label, Icon = icon, Tip = tip, Open = open, Off = feature != null && !Features.KmhFeatures.IsEnabled(feature) };

        // Rebuilt twice a second rather than on every GUI event: labels carry live counts, none needs to be frame-exact.
        internal const float TilesMaxAge = 0.5f;
        private System.Collections.Generic.List<Tile> _featureTiles, _toolTiles;
        private float _tilesBuiltAt = -1f;

        internal static bool TilesDue(float now, float builtAt) => builtAt < 0f || now < builtAt || now - builtAt >= TilesMaxAge;

        private void DrawFeatureButtons(Listing_Standard listing)
        {
            float now = Time.realtimeSinceStartup;
            if (_toolTiles == null || TilesDue(now, _tilesBuiltAt))
            {
                _featureTiles = FeatureTiles();
                _toolTiles    = ToolTiles();
                _tilesBuiltAt = now;
            }
            if (KmhDispatcher.IsKmhServer || KmhDashboardState.KmhConfirmed)
            {
                if (!KmhDispatcher.IsKmhServer)
                    listing.Label("<color=#f0c04f>" + KmhText.Get("KMH_Tab_Reconnecting", "Reconnecting to Colony Nexus… showing your last known features.") + "</color>");
                DrawGrid(listing, _featureTiles);
            }
            else
            {
                listing.Label(KmhText.Get("KMH_Tab_ConnectFirst", "Connect to a Colony Nexus server to use its features."));
            }

            listing.GapLine(12f);

            if (KmhDispatcher.IsKmhServer && !Features.LinkedAccounts.LinkedAccountsCache.IsLinked(KmhSession.Me))
                listing.Label("<color=#f0c04f>" + KmhText.Get("KMH_Tab_DiscordNotLinked", "Discord not linked - link it under Account & links.") + "</color>");

            // Its own windows, not an in-panel expand: that would change the body height the scroll view is sized from.
            DrawGrid(listing, _toolTiles);
        }

        private static System.Collections.Generic.List<Tile> FeatureTiles()
        {
            var tiles = new System.Collections.Generic.List<Tile>();
            tiles.Add(Feature("marketplace", KMHTextures.Marketplace, KmhText.Get("KMH_Tab_Marketplace", "Marketplace"),
                KmhText.Get("KMH_Tab_MarketplaceTip", "Browse and buy from open listings; post your own from a selected caravan."), () => Find.WindowStack.Add(new Dialog_KMHMarketplace())));
            tiles.Add(Feature("treasury", KMHTextures.Treasury, KmhText.Get("KMH_Tab_Treasury", "Treasury"),
                KmhText.Get("KMH_Tab_TreasuryTip", "View your personal or guild treasury; deposit and withdraw silver and items."), () => Find.WindowStack.Add(new Dialog_KMHTreasury())));
            tiles.Add(Feature("guilds", KMHTextures.Guild, KmhText.Get("KMH_Tab_GuildHall", "Guild Hall"),
                KmhText.Get("KMH_Tab_GuildHallTip", "Manage your guild - members, ranks, alliances, perks, MOTD."), () => Find.WindowStack.Add(new Dialog_KMHGuildHall())));
            // Hidden, not greyed: a server that does not speak kmh.federation.* has nothing behind the button.
            if (Features.Federation.FederationGate.FederationOffered)
                tiles.Add(Feature(null, KMHTextures.Guild, KmhText.Get("KMH_Fed_Title", "Guild Federation"),
                    KmhText.Get("KMH_Fed_TabTip", "Your guild together with guilds on other servers: one name and tag, shared renown, weekly objectives and small local bonuses."),
                    Features.Federation.Dialog_KMHFederation.Open));
            tiles.Add(Feature("quests", KMHTextures.Quests, KmhText.Get("KMH_Tab_QuestBoard", "Quest Board"),
                KmhText.Get("KMH_Tab_QuestBoardTip", "Browse, claim, post, and submit cross-server quests."), () => Find.WindowStack.Add(new Dialog_KMHQuestBoard())));
            tiles.Add(Feature("world", KMHTextures.World, KmhText.Get("KMH_Tab_World", "World"),
                KmhText.Get("KMH_Tab_WorldTip", "Living World: active server-wide events and the full set of co-op / competitive global quests."), () => Find.WindowStack.Add(new Features.World.Dialog_KMHWorld())));
            tiles.Add(Feature(null, KMHTextures.Sites, KmhText.Get("KMH_Tab_Sites", "Sites"),
                KmhText.Get("KMH_Tab_SitesTip", "Build production sites, join others as a worker, and set where rewards go."), () => Find.WindowStack.Add(new Features.Sites.Dialog_KMHSites())));
            // Hidden without the capability: an entry that only ever says "not supported" is worse than no entry.
            if (Features.Roadworks.RoadworksHandler.Available)
                tiles.Add(Feature(null, KMHTextures.Roadworks, KmhText.Get("KMH_Tab_Roadworks", "Roadworks"),
                    KmhText.Get("KMH_Tab_RoadworksTip", "Plan and track road projects built from your Roadworks site."), () => Find.WindowStack.Add(new Features.Roadworks.Dialog_KMHRoadworks())));
            int unread = Features.KmhUnread.Comms();
            tiles.Add(new Tile
            {
                Label = unread > 0 ? KmhText.Get("KMH_Tab_CommsUnread", "Communications ({0} unread)", unread) : KmhText.Get("KMH_Tab_Comms", "Communications"),
                Icon = KMHTextures.Comms,
                Tip = KmhText.Get("KMH_Tab_CommsTip", "Live server chat and player mail in one place. Pick mail recipients from the standings roster - online or offline."),
                Off = !Features.KmhFeatures.IsEnabled("chat") && !Features.KmhFeatures.IsEnabled("mail"),
                Open = () => Features.Comms.Dialog_KMHComms.Open(),
            });
            tiles.Add(Feature("standings", KMHTextures.Leaderboard, KmhText.Get("KMH_St_Title", "Server Standings"),
                KmhText.Get("KMH_Tab_StandingsTip", "Player & guild standings plus colony, colonist, trade, contract, battle, site and reputation records."), () => Find.WindowStack.Add(new Features.Standings.Dialog_KMHStandings())));
            if (Features.Federation.FederationGate.TreasuryOffered)
            {
                tiles.Add(Feature(null, KMHTextures.Treasury, KmhText.Get("KMH_Tab_ConnectedTreasury", "Connected Treasury"),
                    KmhText.Get("KMH_Tab_ConnectedTreasuryTip", "Silver held by your Colony Nexus account, which follows you between servers."), Dialogs.Dialog_KMHConnectedTreasury.Open));
                tiles.Add(Feature(null, KMHTextures.Guild, KmhText.Get("KMH_Tab_ConnectedGuildTreasury", "Connected Guild Treasury"),
                    KmhText.Get("KMH_Tab_ConnectedGuildTreasuryTip", "Silver held by your guild at Colony Nexus Global, reachable from every server the guild has linked."), Dialogs.Dialog_KMHConnectedGuildTreasury.Open));
            }
            return tiles;
        }

        private static System.Collections.Generic.List<Tile> ToolTiles()
            => new System.Collections.Generic.List<Tile>
            {
                Feature(null, KMHTextures.About, KmhText.Get("KMH_Tab_Account", "Account & links"),
                    KmhText.Get("KMH_Tab_AccountTip", "Your Colony Nexus player identity, and the Steam and Discord accounts linked to it."), () => Dialogs.Dialog_KMHAccount.Open()),
                // Global, not this server's: shown on every server and none, beside the account it needs.
                Feature(null, KMHTextures.Comms, FriendsLabel(Features.KmhUnread.FriendRequests()),
                    KmhText.Get("KMH_Tab_FriendsTip", "Your Colony Nexus friends on every server: who is online and where, friend requests, and players you blocked. Needs a linked account."),
                    Dialogs.Dialog_KMHFriends.Open),
                Feature(null, KMHTextures.Log, KmhText.Get("KMH_Tab_Advanced", "Advanced & tools"),
                    KmhText.Get("KMH_Tab_AdvancedTip", "Colony Nexus log, known servers, ping, performance and config enforcement."), Dialogs.Dialog_KMHAdvancedTools.Open),
                Feature(null, KMHTextures.About, KmhText.Get("KMH_Tab_HowItWorks", "How Colony Nexus works"),
                    KmhText.Get("KMH_Tab_HowItWorksTip", "Plain-language guide to every Colony Nexus system: treasury, marketplace, auctions, wants, quests, sites, guilds, events, standings."),
                    () => Find.WindowStack.Add(new Dialog_KMHTutorial())),
                Feature(null, KMHTextures.About, KmhText.Get("KMH_Tab_About", "About Colony Nexus"),
                    KmhText.Get("KMH_Tab_AboutTip", "Welcome dialog with Colony Nexus overview + links."), () => Find.WindowStack.Add(new Dialog_KMHAbout())),
                Feature(null, KMHTextures.Log, KmhText.Get("KMH_Report_Button", "Report a bug or suggestion"),
                    KmhText.Get("KMH_Report_ButtonTip", "Tell the server owner and the Colony Nexus team what went wrong or what you would like. A screenshot and diagnostics can go with it - you see both before anything is sent."),
                    () => Features.Reports.KmhReportClient.Open()),
                Feature(null, KMHTextures.Log, KmhText.Get("KMH_MyReports_Title", "My reports"),
                    KmhText.Get("KMH_MyReports_Tip", "Every report sent from this game: whether it reached the server and the developers, and what the developers did with it."),
                    () => Features.Reports.Dialog_KMHMyReports.Open()),
            };

        // The count is the requests waiting for this player's answer.
        internal static string FriendsLabel(int pending)
            => pending > 0 ? KmhText.Get("KMH_Tab_FriendsPending", "Friends ({0} pending)", pending)
                           : KmhText.Get("KMH_Tab_Friends", "Friends");

        private static readonly System.Collections.Generic.HashSet<string> _buttonErrors
            = new System.Collections.Generic.HashSet<string>();

        // Two to a row: ten one-per-row buttons run under the fold on 720p.
        private static void DrawGrid(Listing_Standard listing, System.Collections.Generic.List<Tile> tiles)
        {
            for (int i = 0; i < tiles.Count; i += 2)
            {
                Rect row = listing.GetRect(TileH);
                if (i + 1 >= tiles.Count)
                {
                    DrawTile(new Rect(row.x, row.y, row.width, TileH), tiles[i]);
                    listing.Gap(TileGap);
                    break;
                }
                float w = (row.width - TileGap) / 2f;
                DrawTile(new Rect(row.x, row.y, w, TileH), tiles[i]);
                DrawTile(new Rect(row.x + w + TileGap, row.y, w, TileH), tiles[i + 1]);
                listing.Gap(TileGap);
            }
        }

        private static void DrawTile(Rect r, Tile t)
        {
            try
            {
                if (t.Off)
                {
                    Widgets.DrawBoxSolid(r, new Color(1f, 1f, 1f, 0.04f));
                    Text.Anchor = TextAnchor.MiddleCenter;
                    GUI.color = new Color(1f, 1f, 1f, 0.4f);
                    Widgets.Label(r, KmhText.Get("KMH_Tab_Off", "{0} (off)", t.Label));
                    GUI.color = Color.white;
                    Text.Anchor = TextAnchor.UpperLeft;
                    TooltipHandler.TipRegion(r, KmhText.Get("KMH_Tab_OffTip", "This server has turned {0} off.", t.Label));
                    return;
                }
                if (IconButton.Draw(r, t.Icon, t.Label, t.Tip)) t.Open?.Invoke();
            }
            catch (System.Exception ex)
            {
                if (_buttonErrors.Add(t.Label))
                    KMHPatch.Diagnostics.KmhLog.Error($"Colony Nexus tab button '{t.Label}' failed to draw: {ex}");
                Widgets.Label(r, $"<color=#f07167>{t.Label} - failed to draw</color>");
            }
        }

        private static void DrawConnectionStatus(Listing_Standard listing)
        {
            bool isConnected = SessionHandler.CurrentNetworkState == ClientNetworkState.Connected;
            string endpoint = string.IsNullOrEmpty(Network.Ip)
                ? "(unknown)"
                : $"{Network.Ip}:{Network.Port}";

            if (!isConnected)
            {
                listing.Label("<color=#A9AFB9>● " + KmhText.Get("KMH_Tab_NotConnected", "Not connected to any server") + "</color>");
                return;
            }

            if (KmhDispatcher.IsKmhServer)
            {
                // Name only: the address and protocol are support details, so they go in the tooltip.
                string name = KmhDisplayText.Inert(KmhServerLabel.Current(), 64);
                string serverLabel = $"'{name}' ({endpoint})";
                Rect line = listing.Label("<color=#4cc38a>● " + KmhText.Get("KMH_Tab_ConnectedTo", "Connected to {0}", name) + "</color>");
                TooltipHandler.TipRegion(line, $"{serverLabel}\nColony Nexus protocol v{KmhDispatcher.ServerProtocolVersion}");

                // Plain '&': Unity rich text does not decode HTML entities, so an escaped one reaches the player literally.
                if (Diagnostics.KmhDebugUplink.Active)
                    listing.Label("<color=#f0c04f>● " + KmhText.Get("KMH_Tab_SharingLog", "Sharing your Colony Nexus log with this server - stop it under Advanced & tools.") + "</color>");
            }
            else
            {
                listing.Label("<color=#D3D37C>● " + KmhText.Get("KMH_Tab_NoHandshake", "Connected to {0} - no Colony Nexus handshake", endpoint) + "</color>");
                listing.Label("<color=#A9AFB9>" + KmhText.Get("KMH_Tab_NoHandshakeWhy",
                    "This is normal for stock RimWorld Together servers. Colony Nexus features stay hidden until the server completes the handshake.") + "</color>");
            }
        }
    }
}