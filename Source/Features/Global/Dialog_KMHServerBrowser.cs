using System;
using System.Collections.Generic;
using KMHPatch.Features.Friends;
using KMHPatch.UI;
using RimWorld;
using UnityEngine;
using Verse;

namespace KMHPatch.Features.Global
{
    internal enum BrowserTab { All, Official, Community, Test, Favorites, Recent, Friends }

    internal sealed class Dialog_KMHServerBrowser : Window
    {
        private const float HeaderH  = 34f;
        private const float TabsH    = 30f;
        private const float FilterH  = 30f;
        private const float ListFrac = 0.42f;
        private const float CardH    = 56f;
        private const float ActionsH = 44f;
        internal const float MinWidth = 760f, MinHeight = 440f;

        private BrowserTab _tab = BrowserTab.All;
        private BrowserSort _sort = BrowserSort.Recommended;
        private readonly HashSet<string> _tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private string _search = "";
        private Vector2 _listScroll;
        private Vector2 _detailScroll;
        // Measured on the previous frame; a fixed guess cut off the end of long details panes.
        private float _detailH = 600f;

        // Selection is a ServerId, never a row index: a realtime delta can reorder the list under the cursor.
        private string _selectedServerId;

        public Dialog_KMHServerBrowser()
        {
            doCloseX = true;
            forcePause = false;
            absorbInputAroundWindow = true;
            closeOnAccept = false;
            draggable = true;
            resizeable = true;
        }

        public override Vector2 InitialSize => new Vector2(Mathf.Min(1400f, Verse.UI.screenWidth - 80f),
                                                           Mathf.Min(900f, Verse.UI.screenHeight - 80f));

        public override void PreOpen()
        {
            base.PreOpen();
            KmhDirectoryClient.LoadCacheIfEmpty();
            KmhDirectoryClient.RefreshSummaryAsync();
            Account.KmhGlobalCapabilities.EnsureProbed();
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (windowRect.width < MinWidth && Verse.UI.screenWidth > MinWidth + 40f) windowRect.width = MinWidth;
            if (windowRect.height < MinHeight && Verse.UI.screenHeight > MinHeight + 40f) windowRect.height = MinHeight;

            KmhDirectoryClient.RefreshIfDue();
            float y = inRect.y;
            DrawHeader(new Rect(inRect.x, y, inRect.width, HeaderH)); y += HeaderH + 4f;
            DrawTabs(new Rect(inRect.x, y, inRect.width, TabsH));     y += TabsH + 4f;
            DrawFilters(new Rect(inRect.x, y, inRect.width, FilterH)); y += FilterH + 6f;

            float bodyH = inRect.yMax - y;
            float listW = Mathf.Floor(inRect.width * ListFrac);
            DrawList(new Rect(inRect.x, y, listW, bodyH));
            DrawDetails(new Rect(inRect.x + listW + 10f, y, inRect.width - listW - 10f, bodyH));
        }

        private void DrawHeader(Rect rect)
        {
            float statusW = Mathf.Min(320f, rect.width * 0.38f);
            Text.Font = GameFont.Medium;
            // A directory typed into the settings is not Colony Nexus's, and the title says so where the list is.
            bool firstParty = GlobalHttp.IsFirstParty;
            string title = firstParty ? KmhText.Get("KMH_Browser_Title", "Colony Nexus Server Browser")
                : KmhText.Get("KMH_Browser_TitleUnofficial", "Unofficial directory: {0}", KmhDisplayText.Inert(HostOf(GlobalHttp.BaseUrl), 64));
            Rect titleRect = new Rect(rect.x, rect.y, rect.width - statusW - 10f, rect.height);
            if (!firstParty)
            {
                GUI.color = new Color(0.95f, 0.6f, 0.5f);
                TooltipHandler.TipRegion(titleRect, KmhText.Get("KMH_Browser_UnofficialTip",
                    "This list comes from an address set in Colony Nexus's mod settings, not from Colony Nexus Global. It cannot mark any server official."));
            }
            Widgets.Label(titleRect, title.Truncate(titleRect.width));
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            Rect status = new Rect(rect.xMax - statusW, rect.y, statusW, rect.height);
            Text.Anchor = TextAnchor.MiddleRight;
            GUI.color = KmhServersMenuTile.StatusColorPublic();
            string line = KmhServersMenuTile.StatusLine();
            Widgets.Label(status, line.Truncate(statusW));
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            if (Text.CalcSize(line).x > statusW) TooltipHandler.TipRegion(status, line);
        }

        private static readonly BrowserTab[] Tabs = { BrowserTab.All, BrowserTab.Official, BrowserTab.Community, BrowserTab.Test,
                                                      BrowserTab.Favorites, BrowserTab.Recent, BrowserTab.Friends };

        private void DrawTabs(Rect rect)
        {
            float w = rect.width / Tabs.Length;
            for (int i = 0; i < Tabs.Length; i++)
            {
                Rect r = new Rect(rect.x + i * w, rect.y, w - 4f, rect.height);
                bool active = _tab == Tabs[i];
                string label = TabLabel(Tabs[i]);
                if (Widgets.ButtonText(r, label.Truncate(r.width - 10f))) _tab = Tabs[i];
                // Drawn after the button, which otherwise paints over the highlight.
                if (active)
                {
                    Widgets.DrawHighlightSelected(r);
                    Widgets.DrawBoxSolid(new Rect(r.x + 4f, r.yMax - 3f, r.width - 8f, 3f), new Color(0.45f, 0.75f, 1f));
                }
            }
        }

        private static string TabLabel(BrowserTab tab)
        {
            switch (tab)
            {
                case BrowserTab.Official:  return KmhText.Get("KMH_Browser_Tab_Official", "Official");
                case BrowserTab.Community: return KmhText.Get("KMH_Browser_Tab_Community", "Community");
                case BrowserTab.Test:      return KmhText.Get("KMH_Browser_Tab_Test", "Test");
                case BrowserTab.Favorites: return KmhText.Get("KMH_Browser_Tab_Favorites", "Favourites");
                case BrowserTab.Recent:    return KmhText.Get("KMH_Browser_Tab_Recent", "Recent");
                case BrowserTab.Friends:   return KmhText.Get("KMH_Browser_Tab_Friends", "Friends");
                default:                   return KmhText.Get("KMH_Browser_Tab_All", "All");
            }
        }

        private void DrawFilters(Rect rect)
        {
            float h = rect.height - 4f;
            Rect searchRect = new Rect(rect.x, rect.y, Mathf.Min(360f, rect.width * 0.4f), h);
            _search = DialogLayout.SearchField(searchRect, _search,
                                               KmhText.Get("KMH_Browser_SearchHint", "Search servers…"));

            float x = searchRect.xMax + 8f;
            string sortLabel = KmhText.Get("KMH_Browser_SortBy", "Sort: {0}", BrowserSorting.Label(_sort));
            Rect sortRect = new Rect(x, rect.y, 170f, h);
            if (Widgets.ButtonText(sortRect, sortLabel.Truncate(sortRect.width - 12f))) ShowSortMenu();
            TooltipHandler.TipRegion(sortRect, BrowserSorting.Describe(_sort));
            x = sortRect.xMax + 6f;

            List<string> available = AvailableTags();
            if (available.Count > 0)
            {
                Rect tagRect = new Rect(x, rect.y, 132f, h);
                string label = _tags.Count == 0 ? KmhText.Get("KMH_Browser_Tags", "Tags")
                                                : KmhText.Get("KMH_Browser_TagsCount", "Tags ({0})", _tags.Count);
                if (Widgets.ButtonText(tagRect, label)) ShowTagMenu(available);
                x = tagRect.xMax + 6f;

                if (_tags.Count > 0)
                {
                    Rect clearRect = new Rect(x, rect.y, 70f, h);
                    if (Widgets.ButtonText(clearRect, KmhText.Get("KMH_Browser_TagsClear", "Clear"))) _tags.Clear();
                    x = clearRect.xMax + 6f;
                }
            }

            if (KmhDirectoryClient.ShowingCache && x < rect.xMax - 24f)
            {
                Rect note = new Rect(x, rect.y, rect.xMax - x, h);
                string text = KmhText.Get("KMH_Browser_CachedResults", "Showing saved results");
                if (Text.CalcSize(text).x > note.width) { TooltipHandler.TipRegion(note, text); text = "⚠"; }
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = new Color(0.85f, 0.75f, 0.45f);
                Widgets.Label(note, text);
                GUI.color = Color.white;
                Text.Anchor = TextAnchor.UpperLeft;
            }
        }

        private void ShowSortMenu()
        {
            var opts = new List<FloatMenuOption>();
            foreach (BrowserSort s in new[] { BrowserSort.Recommended, BrowserSort.Players, BrowserSort.Active,
                                              BrowserSort.Recent, BrowserSort.Name })
            {
                BrowserSort captured = s;
                opts.Add(new FloatMenuOption((s == _sort ? "✓ " : "    ") + BrowserSorting.Label(s), () => _sort = captured));
            }
            Find.WindowStack.Add(new FloatMenu(opts));
        }

        private List<string> _tagCache;
        private DateTime _tagCacheAt;
        private readonly Dictionary<string, int> _tagCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // Rebuilt only when the listing changed: this is on the draw path.
        private List<string> AvailableTags()
        {
            if (_tagCache != null && _tagCacheAt == KmhDirectoryClient.LastRefreshUtc) return _tagCache;

            _tagCache = BrowserTags.InUse(KmhDirectoryClient.Cards, _tagCounts);
            _tagCacheAt = KmhDirectoryClient.LastRefreshUtc;
            return _tagCache;
        }

        private void ShowTagMenu(List<string> available)
        {
            var opts = new List<FloatMenuOption>();
            foreach (string tag in available)
            {
                string captured = tag;
                bool on = _tags.Contains(tag);
                _tagCounts.TryGetValue(tag, out int n);
                opts.Add(new FloatMenuOption($"{(on ? "✓ " : "    ")}{BrowserTags.Label(tag)}  ({n})", () =>
                {
                    if (!_tags.Remove(captured)) _tags.Add(captured);
                }));
            }
            Find.WindowStack.Add(new FloatMenu(opts));
        }

        // Keys are precomputed so the per-frame sort never re-derives compatibility on each comparison.
        private struct Ranked { public SortKey Key; public ServerCardDto Card; }

        private readonly List<Ranked> _rows = new List<Ranked>();

        private readonly Dictionary<string, List<string>> _friendsOn = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        // Global returns a friend's server only when it is listed and their privacy allows it.
        private List<ServerCardDto> _friendCards = new List<ServerCardDto>();
        private DateTime _friendCardsAt;

        // Rebuilt at most once a second: this is on the draw path.
        private IReadOnlyList<ServerCardDto> FriendCards()
        {
            DateTime now = DateTime.UtcNow;
            if (now - _friendCardsAt < TimeSpan.FromSeconds(1)) return _friendCards;
            _friendCardsAt = now;
            KmhFriendsClient.RefreshIfDue();
            _friendsOn.Clear();
            foreach (FriendDto f in KmhFriendsClient.OnServers())
            {
                if (!_friendsOn.TryGetValue(f.ServerId, out List<string> names)) _friendsOn[f.ServerId] = names = new List<string>();
                names.Add(f.Shown);
            }
            KmhDirectoryClient.EnsureCardsFor(_friendsOn.Keys);
            var cards = new List<ServerCardDto>();
            foreach (string id in _friendsOn.Keys)
            {
                ServerCardDto c = KmhDirectoryClient.CardById(id);
                if (c != null) cards.Add(c);
            }
            return _friendCards = cards;
        }

        // Local history reaches past the directory's first page; servers no longer listed are counted, not shown.
        private List<ServerCardDto> _historyCards = new List<ServerCardDto>();
        private BrowserTab _historyTab;
        private DateTime _historyAt;
        private int _historyUnlisted;

        private IReadOnlyList<ServerCardDto> HistoryCards(BrowserTab tab)
        {
            DateTime now = DateTime.UtcNow;
            if (tab == _historyTab && now - _historyAt < TimeSpan.FromSeconds(1)) return _historyCards;
            _historyTab = tab; _historyAt = now;
            List<string> ids = new List<string>();
            foreach (Servers.SeenServersStore.Entry e in Servers.SeenServersStore.All())
                if (e.IdentityKnown && (tab == BrowserTab.Recent ? e.LastSeenTicks > 0 : e.Favorite)) ids.Add(e.ServerId);
            KmhDirectoryClient.EnsureCardsFor(ids);
            var cards = new List<ServerCardDto>();
            foreach (string id in ids)
            {
                ServerCardDto c = KmhDirectoryClient.CardById(id);
                if (c != null) cards.Add(c);
            }
            _historyUnlisted = ids.Count - cards.Count;
            return _historyCards = cards;
        }

        private List<ServerCardDto> Visible()
        {
            IReadOnlyList<ServerCardDto> all =
                _tab == BrowserTab.Friends ? FriendCards()
              : _tab == BrowserTab.Favorites || _tab == BrowserTab.Recent ? HistoryCards(_tab)
              : KmhDirectoryClient.Cards;
            _rows.Clear();
            foreach (ServerCardDto c in all)
            {
                if (_tab == BrowserTab.Official && !c.Official) continue;
                if (_tab == BrowserTab.Community && (c.Official || ServerEnvironment.IsTest(c))) continue;
                if (_tab == BrowserTab.Test && !ServerEnvironment.IsTest(c)) continue;
                // Dead rows in the default view make a working browser look broken; search and favourites still find them.
                if (_tab == BrowserTab.All && c.Status == "Offline" && string.IsNullOrEmpty(_search)
                    && !SeenServersStoreBridge.IsFavorite(c.ServerId)) continue;
                if (!BrowserSorting.Matches(_search, c.Name, c.Tags, c.Region)) continue;
                if (!MatchesSelectedTags(c)) continue;
                _rows.Add(new Ranked { Key = KeyFor(c), Card = c });
            }

            _rows.Sort((a, b) => BrowserSorting.Compare(a.Key, b.Key, _sort));

            List<ServerCardDto> shown = new List<ServerCardDto>(_rows.Count);
            foreach (Ranked row in _rows) shown.Add(row.Card);
            return shown;
        }

        private bool MatchesSelectedTags(ServerCardDto card)
        {
            if (_tags.Count == 0) return true;
            foreach (string want in _tags)
            {
                string wanted = BrowserTags.Canonical(want);
                bool found = false;
                foreach (string have in card.Tags)
                    if (string.Equals(BrowserTags.Canonical(have), wanted, StringComparison.Ordinal)) { found = true; break; }
                if (!found) return false;
            }
            return true;
        }

        private static SortKey KeyFor(ServerCardDto c) => new SortKey
        {
            Official       = c.Official,
            Rank           = Rank(c),
            Players        = c.Players,
            PlayersPeak    = c.PlayersPeak,
            PlayersAverage = c.PlayersAverage,
            LastHeartbeat  = c.LastHeartbeatUnix ?? 0,
            Name           = c.Name,
            ServerId       = c.ServerId,
        };

        private static int Rank(ServerCardDto card)
        {
            switch (KmhCompatibility.Evaluate(card))
            {
                case CompatState.Ready:
                case CompatState.ModsOptionalDiffer: return 0;
                case CompatState.Checking:
                case CompatState.ModsUnavailable:    return 1;
                // Fixable by Prepare and join, so ranked above servers that are closed for now.
                case CompatState.ModsAction:
                case CompatState.RwtUnsupported:     return 2;
                // Below the fixable ones: a missing expansion is a purchase, not something Prepare can do.
                case CompatState.ModsNeedExpansion:  return 3;
                case CompatState.Starting:
                case CompatState.Maintenance:
                case CompatState.Full:
                case CompatState.Offline:            return 4;
                case CompatState.GameVersionMismatch:
                case CompatState.ProtocolMismatch:
                case CompatState.NoAddress:          return BrowserSorting.CannotJoinRank;
                default:                             return 3;
            }
        }

        private bool PagedTab => _tab == BrowserTab.All || _tab == BrowserTab.Official || _tab == BrowserTab.Community || _tab == BrowserTab.Test;

        private void DrawList(Rect rect)
        {
            Widgets.DrawMenuSection(rect);
            List<ServerCardDto> rows = Visible();
            bool more = PagedTab && KmhDirectoryClient.HasMore;
            bool unlistedNote = (_tab == BrowserTab.Favorites || _tab == BrowserTab.Recent) && _historyUnlisted > 0;

            if (rows.Count == 0 && !more) { DrawEmptyState(rect.ContractedBy(12f)); return; }

            Rect inner = rect.ContractedBy(4f);
            float footer = (more ? 36f : 0f) + (unlistedNote ? 26f : 0f);
            Rect view = new Rect(0f, 0f, inner.width - 18f, rows.Count * (CardH + 4f) + footer);
            Widgets.BeginScrollView(inner, ref _listScroll, view);

            float y = 0f;
            for (int i = 0; i < rows.Count; i++)
            {
                Rect card = new Rect(0f, y, view.width, CardH);
                if (card.yMax >= _listScroll.y && card.y <= _listScroll.y + inner.height)
                    DrawCard(card, rows[i]);
                y += CardH + 4f;
            }
            if (unlistedNote)
            {
                GUI.color = new Color(0.65f, 0.65f, 0.65f);
                Text.Font = GameFont.Tiny;
                Widgets.Label(new Rect(8f, y + 4f, view.width - 16f, 20f),
                    KmhText.Get("KMH_Browser_HistoryUnlisted", "{0} more you know of are not listed right now.", _historyUnlisted));
                Text.Font = GameFont.Small;
                GUI.color = Color.white;
                y += 26f;
            }
            if (more && Widgets.ButtonText(new Rect(8f, y + 4f, view.width - 16f, 28f), KmhText.Get("KMH_Browser_LoadMore", "Load more servers")))
                KmhDirectoryClient.LoadMore();

            Widgets.EndScrollView();
        }

        private static string HostOf(string url) => Uri.TryCreate(url, UriKind.Absolute, out Uri u) ? u.Host : url ?? "";

        // Drawn in a colour no server name can produce: names are inert and stripped of star glyphs on the way in.
        internal static string OfficialMark(ServerCardDto card)
            => card != null && card.Official ? "<color=#F4B83C>★</color> " : "";

        private static string OfficialTip()
            => KmhText.Get("KMH_Browser_OfficialTip", "Marked official by Colony Nexus Global itself. A server cannot give itself this mark.");

        private void DrawCard(Rect rect, ServerCardDto card)
        {
            bool selected = card.ServerId == _selectedServerId;
            if (selected) Widgets.DrawHighlightSelected(rect);
            else if (Mouse.IsOver(rect)) Widgets.DrawHighlight(rect);

            Rect name = new Rect(rect.x + 8f, rect.y + 4f, rect.width - 120f, 22f);
            Text.Anchor = TextAnchor.MiddleLeft;
            if (card.Official)
            {
                Rect star = new Rect(name.x, name.y, 18f, name.height);
                GUI.color = new Color(0.957f, 0.722f, 0.235f);
                Widgets.Label(star, "★");
                GUI.color = Color.white;
                TooltipHandler.TipRegion(star, OfficialTip());
                name.xMin += 18f;
            }
            string full = card.Name;
            Widgets.Label(name, full.Truncate(name.width));
            if (Text.CalcSize(full).x > name.width) TooltipHandler.TipRegion(name, full);

            Rect pop = new Rect(rect.xMax - 110f, rect.y + 4f, 102f, 22f);
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(pop, card.MaxPlayers > 0 ? $"{card.Players} / {card.MaxPlayers}" : card.Players.ToString());

            float verdictW = Mathf.Min(170f, rect.width * 0.4f);
            Rect meta = new Rect(rect.x + 8f, rect.y + 26f, rect.width - verdictW - 24f, 18f);
            Text.Anchor = TextAnchor.MiddleLeft;
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.72f, 0.72f, 0.72f);
            bool friendsLine = _tab == BrowserTab.Friends && _friendsOn.TryGetValue(card.ServerId, out List<string> here);
            string line = friendsLine
                ? KmhText.Get("KMH_Browser_FriendsHere", "Friends here: {0}", string.Join(", ", _friendsOn[card.ServerId]))
                : MetaLine(card);
            Widgets.Label(meta, line.Truncate(meta.width));
            if (Text.CalcSize(line).x > meta.width) TooltipHandler.TipRegion(meta, line);
            GUI.color = Color.white;

            ModProfileClient.EnsureFetchedForList(card);

            Rect state = new Rect(rect.xMax - verdictW - 8f, rect.y + 26f, verdictW, 18f);
            Text.Anchor = TextAnchor.MiddleRight;
            GUI.color = StateColor(card);
            string verdict = Features.Mods.KmhJoinPreparation.IsFor(card.ServerId)
                ? KmhText.Get("KMH_Browser_Preparing", "Preparing…") : KmhCompatibility.Describe(card);
            Widgets.Label(state, verdict.Truncate(state.width));
            if (Text.CalcSize(verdict).x > state.width) TooltipHandler.TipRegion(state, verdict);
            GUI.color = Color.white;

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;

            if (Widgets.ButtonInvisible(rect) && _selectedServerId != card.ServerId) { _selectedServerId = card.ServerId; _detailScroll = Vector2.zero; }
        }

        private static string MetaLine(ServerCardDto card)
        {
            List<string> parts = new List<string>();
            if (!string.IsNullOrEmpty(card.Region)) parts.Add(card.Region);
            if (card.Languages.Count > 0) parts.Add(string.Join(" / ", card.Languages.ToArray()));
            else if (!string.IsNullOrEmpty(card.PrimaryLanguage)) parts.Add(card.PrimaryLanguage);
            if (!string.IsNullOrEmpty(card.Status)) parts.Add(KmhCompatibility.StatusLabel(card.Status));
            return string.Join("  ·  ", parts.ToArray());
        }

        private static Color StateColor(ServerCardDto card)
        {
            switch (KmhCompatibility.Evaluate(card))
            {
                case CompatState.Checking:           return new Color(0.75f, 0.75f, 0.55f);
                case CompatState.Ready:              return new Color(0.55f, 0.8f, 0.55f);
                case CompatState.ModsOptionalDiffer: return new Color(0.7f, 0.82f, 0.6f);
                case CompatState.ModsAction:         return new Color(0.9f, 0.75f, 0.4f);
                // Not a refusal: Join stays open and RimWorld Together checks at the door.
                case CompatState.ModsUnavailable:    return new Color(0.85f, 0.75f, 0.45f);
                case CompatState.ModsNeedExpansion:  return new Color(0.85f, 0.6f, 0.45f);
                default:                             return new Color(0.85f, 0.45f, 0.45f);
            }
        }

        private void DrawEmptyState(Rect rect)
        {
            string loading = LoadingMessage();
            if (loading != null)
            {
                float w = Mathf.Min(rect.width, 560f), h = Mathf.Min(KmhLoadingPanel.Height(w, false), rect.height);
                KmhLoadingPanel.Draw(new Rect(rect.x + (rect.width - w) / 2f, rect.y + Mathf.Max(0f, (rect.height - h) / 2f), w, h), loading);
                return;
            }
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = new Color(0.75f, 0.75f, 0.75f);
            Widgets.Label(rect, EmptyMessage());
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private string LoadingMessage()
        {
            if (_tab == BrowserTab.Friends)
                return KmhFriendsClient.State == FriendsState.Unknown || KmhFriendsClient.State == FriendsState.Loading
                    ? KmhText.Get("KMH_Browser_Friends_Loading", "Looking for your friends...") : null;
            if (_tab == BrowserTab.Favorites || _tab == BrowserTab.Recent) return null;
            return KmhDirectoryClient.State == GlobalLinkState.Unknown || KmhDirectoryClient.State == GlobalLinkState.Connecting
                ? KmhText.Get("KMH_Browser_Tile_Tip_Connecting", "Asking Colony Nexus Global which servers are up…") : null;
        }

        private string EmptyMessage()
        {
            if (_tab == BrowserTab.Friends)
            {
                switch (KmhFriendsClient.State)
                {
                    case FriendsState.NotLinked:
                        return KmhText.Get("KMH_Browser_Friends_NotLinked", "Sign in to Colony Nexus Global with Steam (Account, on the main menu) to see where your friends are playing.");
                    case FriendsState.Unavailable:
                        return KmhText.Get("KMH_Browser_Friends_Unavailable", "Your friends list could not be loaded right now. It is tried again shortly.");
                    case FriendsState.Unknown:
                    case FriendsState.Loading:
                        return KmhText.Get("KMH_Browser_Friends_Loading", "Looking for your friends...");
                }
                int hidden = KmhFriendsClient.OnlineWithoutServer();
                return hidden > 0
                    ? KmhText.Get("KMH_Browser_Friends_Hidden", "{0} friend(s) online, on a server that is not listed or that they keep private.", hidden)
                    : KmhText.Get("KMH_Browser_Friends_None", "None of your friends are on a listed server right now.");
            }
            if (_tab == BrowserTab.Favorites)
                return _historyUnlisted > 0
                    ? KmhText.Get("KMH_Browser_HistoryUnlisted", "{0} more you know of are not listed right now.", _historyUnlisted)
                    : KmhText.Get("KMH_Browser_Empty_Favorites", "Favourite a server to find it here.");
            if (_tab == BrowserTab.Recent)
                return _historyUnlisted > 0
                    ? KmhText.Get("KMH_Browser_HistoryUnlisted", "{0} more you know of are not listed right now.", _historyUnlisted)
                    : KmhText.Get("KMH_Browser_Empty_Recent", "Servers you join will show up here.");
            if (KmhDirectoryClient.State == GlobalLinkState.Offline)
                return KmhText.Get("KMH_Browser_Empty_Offline", "Unable to reach Colony Nexus Global. Cached results are shown where available.");
            if (KmhDirectoryClient.State == GlobalLinkState.RateLimited)
                return KmhText.Get("KMH_Browser_Empty_RateLimited", "Colony Nexus Global asked this client to slow down. The list refreshes again shortly.");
            if (KmhDirectoryClient.State == GlobalLinkState.Degraded)
                return KmhText.Get("KMH_Browser_Empty_Degraded", "Colony Nexus Global answered but could not serve the list. Cached results are shown where available.");
            return KmhText.Get("KMH_Browser_Empty_All", "No servers found. Try changing your filters.");
        }

        private void DrawDetails(Rect rect)
        {
            Widgets.DrawMenuSection(rect);
            ServerCardDto card = Selected();
            if (card == null)
            {
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = new Color(0.7f, 0.7f, 0.7f);
                Widgets.Label(rect.ContractedBy(12f), KmhText.Get("KMH_Browser_SelectAServer", "Select a server to see its details."));
                GUI.color = Color.white;
                Text.Anchor = TextAnchor.UpperLeft;
                return;
            }

            // Started here rather than on selection so a card that arrives in a refresh is fetched too.
            ModProfileClient.EnsureFetched(card);

            Rect inner = new Rect(rect.x + 12f, rect.y + 12f, rect.width - 24f, rect.height - 24f - ActionsH);
            List<string> details = KmhCompatibility.Details(card);
            float width = inner.width - 18f;
            Rect view = new Rect(0f, 0f, width, Mathf.Max(_detailH, inner.height));
            Widgets.BeginScrollView(inner, ref _detailScroll, view);

            // A Listing that runs past its rect starts a second column off to the right, hiding whole sections.
            Listing_Standard list = new Listing_Standard { maxOneColumn = true };
            list.Begin(new Rect(0f, 0f, width, 99999f));
            Text.Font = GameFont.Medium;
            list.Label(OfficialMark(card) + card.Name, tooltip: card.Official ? OfficialTip() : null);
            Text.Font = GameFont.Small;
            list.GapLine(6f);

            CompatState state = KmhCompatibility.Evaluate(card);
            GUI.color = StateColor(card);
            list.Label(KmhCompatibility.Describe(card));
            GUI.color = Color.white;
            list.Label(ExplainForBrowser(card, state));
            foreach (string line in details) list.Label("  • " + line);
            string rimworld = RimWorldNote(card);
            if (rimworld.Length > 0) { GUI.color = new Color(0.9f, 0.75f, 0.45f); list.Label("  • " + rimworld); GUI.color = Color.white; }
            list.Gap(8f);

            if (!string.IsNullOrEmpty(card.Description))
            {
                list.Label(KmhText.Get("KMH_Browser_Section_About", "About this server"));
                GUI.color = new Color(0.82f, 0.82f, 0.82f);
                list.Label(card.Description);
                GUI.color = Color.white;
                list.Gap(8f);
            }

            string webPage = WebPageUrl(card);
            bool anyLink = !string.IsNullOrEmpty(card.DiscordUrl) || !string.IsNullOrEmpty(card.WebsiteUrl) || webPage.Length > 0;
            if (anyLink)
            {
                list.Label(KmhText.Get("KMH_Browser_Section_Links", "Links"));
                if (webPage.Length > 0)
                    LinkRow(list, KmhText.Get("KMH_Browser_WebPage", "Colony Nexus page"), webPage);
                // Owner-typed links are copied, never opened: a listing must not pick an address for the browser.
                if (!string.IsNullOrEmpty(card.DiscordUrl))
                    LinkRow(list, KmhText.Get("KMH_Browser_Discord", "Discord"), card.DiscordUrl);
                if (!string.IsNullOrEmpty(card.WebsiteUrl))
                    LinkRow(list, KmhText.Get("KMH_Browser_Website", "Website"), card.WebsiteUrl);
                list.Gap(8f);
            }

            list.Label(KmhText.Get("KMH_Browser_Section_Overview", "Overview"));
            Row(list, KmhText.Get("KMH_Browser_Players", "Players"),
                card.MaxPlayers > 0 ? $"{card.Players} / {card.MaxPlayers}" : card.Players.ToString());
            Row(list, KmhText.Get("KMH_Browser_Status", "Status"), KmhCompatibility.StatusLabel(card.Status));
            if (!string.IsNullOrEmpty(card.Region)) Row(list, KmhText.Get("KMH_Browser_Region", "Region"), card.Region);
            if (card.Languages.Count > 0)
                Row(list, KmhText.Get("KMH_Browser_Languages", "Languages"), string.Join(" / ", card.Languages.ToArray()));
            Row(list, KmhText.Get("KMH_Browser_Address", "Address"),
                string.IsNullOrEmpty(card.JoinEndpoint)
                    ? KmhText.Get("KMH_Browser_Address_None", "not published") : card.JoinEndpoint);
            // Covered by the heartbeat signature, so it can be trusted; RWT asks for the password at the door.
            Row(list, KmhText.Get("KMH_Browser_Password", "Password"),
                card.RequiresPassword
                    ? KmhText.Get("KMH_Browser_Password_Yes", "required")
                    : KmhText.Get("KMH_Browser_Password_No", "not required"));
            list.Gap(8f);

            list.Label(KmhText.Get("KMH_Browser_Section_Compatibility", "Compatibility"));
            string mineRw = RimWorldMine();
            Row(list, "RimWorld", card.RimWorldVersion + (RimWorldDiffers(card.RimWorldVersion, mineRw)
                ? "  (" + KmhText.Get("KMH_Browser_YouHave", "you have {0}").Replace("{0}", mineRw) + ")" : ""));
            Row(list, "RimWorld Together", card.RwtVersion + (KmhRwtVersions.Supports(card.RwtVersion)
                ? "" : "  (" + KmhText.Get("KMH_Browser_YouHave", "you have {0}").Replace("{0}", KmhRwtVersions.Mine) + ")"));
            Row(list, "Colony Nexus", card.KmhVersion);
            Row(list, KmhText.Get("KMH_Browser_Protocol", "Colony Nexus protocol"), "v" + card.Protocol);
            Row(list, KmhText.Get("KMH_Browser_Mods", "Mods"), KmhCompatibility.ModsLabel(card));

            _detailH = list.CurHeight + 12f;
            list.End();
            Widgets.EndScrollView();

            DrawActions(new Rect(rect.x + 12f, rect.yMax - ActionsH, rect.width - 24f, ActionsH - 10f), card, state);
        }

        private static string ExplainForBrowser(ServerCardDto card, CompatState state)
        {
            if (state == CompatState.ModsAction)
                return Features.Mods.KmhWorkshop.Available || !NeedsWorkshop(card)
                    ? KmhText.Get("KMH_Browser_Why_ModsPrepare", "Your mod list does not match what this server requires. \"Prepare and join\" downloads what is missing, sets your mod list, restarts RimWorld and joins - you see every step first.")
                    : KmhText.Get("KMH_Browser_Why_ModsNoSteam", "Your mod list does not match, and some of the missing mods come from the Steam Workshop. Start Steam to let Colony Nexus subscribe to them.");
            if (state == CompatState.RwtUnsupported)
                return KmhCompatibility.Explain(card, state) + " " + KmhText.Get("KMH_Browser_Why_RwtPrepare", "\"Prepare and join\" switches to the matching version, and downloads it from its official release page only if it is not on this PC yet.");
            if (state == CompatState.ModsUnavailable)
            {
                ModProfileClient.Entry e = ModProfileClient.Peek(card.ServerId);
                if (e != null && !string.IsNullOrEmpty(e.ErrorCode))
                    return KmhText.Get("KMH_Browser_Why_ModsUnchecked", "This server's mod list could not be read just now ({0}), so it could not be compared. You can still join, and RimWorld Together checks at the door.", e.ErrorCode);
            }
            return KmhCompatibility.Explain(card, state);
        }

        private static bool NeedsWorkshop(ServerCardDto card)
            => (ModProfileClient.Peek(card.ServerId)?.Comparison?.Subscribable().Count ?? 0) > 0;

        private static string RimWorldMine() => Plain(Compat.KmhGameVersion.Full);

        // Compared only as far as the server says, so "1.6" matches every 1.6 build.
        internal static bool RimWorldDiffers(string server, string mine)
        {
            Compat.GameMatch m = Compat.KmhGameVersion.Compare(server, mine);
            return m == Compat.GameMatch.Differs || m == Compat.GameMatch.BuildDiffers;
        }

        private static string Plain(string v)
        {
            if (string.IsNullOrEmpty(v)) return "";
            v = v.Trim();
            int space = v.IndexOf(' ');
            return space > 0 ? v.Substring(0, space) : v;
        }

        // Build differences only: another version entirely is a refusal, explained as the blocker.
        private static string RimWorldNote(ServerCardDto card)
        {
            string mine = RimWorldMine();
            return Compat.KmhGameVersion.Compare(card.RimWorldVersion, mine) == Compat.GameMatch.BuildDiffers
                ? KmhText.Get("KMH_Browser_RimWorldDiffers", "This server runs RimWorld {0} and you have {1}. RimWorld Together may refuse the join until both match; Steam updates RimWorld itself.", Plain(card.RimWorldVersion), Plain(mine))
                : "";
        }

        private void DrawActions(Rect rect, ServerCardDto card, CompatState state)
        {
            ActionLayout(rect.width, Features.Mods.KmhJoinPreparation.CanRestoreModList && !Features.Mods.KmhJoinPreparation.Active,
                         out float favW, out float restoreW, out float primaryW);

            bool fav = SeenServersStoreBridge.IsFavorite(card.ServerId);
            Rect favR = new Rect(rect.x, rect.y, favW, rect.height);
            string favLabel = fav ? KmhText.Get("KMH_Browser_Favourited", "★ Favourite") : KmhText.Get("KMH_Browser_AddFavourite", "☆ Add favourite");
            if (Widgets.ButtonText(favR, favLabel.Truncate(favR.width - 10f)))
                SeenServersStoreBridge.ToggleFavorite(card.ServerId, card.Name);
            TooltipHandler.TipRegion(favR, fav ? KmhText.Get("KMH_Browser_Favourited_Tip", "In your Favourites. Click to remove it.")
                                               : KmhText.Get("KMH_Browser_AddFavourite_Tip", "Keep this server in your Favourites tab."));

            if (restoreW > 0f)
            {
                Rect restore = new Rect(favR.xMax + ActionGap, rect.y, restoreW, rect.height);
                if (Widgets.ButtonText(restore, KmhText.Get("KMH_Prep_Restore", "Restore my mod list").Truncate(restore.width - 10f)))
                    OfferRestore();
                TooltipHandler.TipRegion(restore, KmhText.Get("KMH_Browser_Restore_Tip", "Puts back the mod list and RimWorld Together copy you had before Colony Nexus set them up for a server. RimWorld has to restart to load them."));
            }

            Rect primary = new Rect(rect.xMax - primaryW, rect.y, primaryW, rect.height);
            if (Features.Mods.KmhJoinPreparation.IsFor(card.ServerId))
            {
                string showPrep = KmhText.Get("KMH_Browser_ShowPreparation", "Preparing… (show)");
                if (Widgets.ButtonText(primary, showPrep.Truncate(primary.width - 10f)))
                    Find.WindowStack.Add(new Dialog_KMHPrepareJoin());
                if (Text.CalcSize(showPrep).x > primary.width - 10f) TooltipHandler.TipRegion(primary, showPrep);
                return;
            }
            if (!KmhCompatibility.IsBlocking(state))
            {
                if (Widgets.ButtonText(primary, KmhText.Get("KMH_Browser_Join", "Join"))) Join(card);
                return;
            }
            if (Fixable(card, state))
            {
                if (Widgets.ButtonText(primary, KmhText.Get("KMH_Browser_Prepare", "Prepare and join").Truncate(primary.width - 10f)))
                    Find.WindowStack.Add(new Dialog_KMHPrepareJoin(card));
                TooltipHandler.TipRegion(primary, KmhText.Get("KMH_Browser_Prepare_Tip", "Shows exactly what Colony Nexus would download, switch on or off and restart for this server. Nothing changes until you accept."));
                return;
            }
            GUI.color = new Color(1f, 1f, 1f, 0.45f);
            Widgets.ButtonText(primary, KmhText.Get("KMH_Browser_Join", "Join"), active: false);
            GUI.color = Color.white;
            TooltipHandler.TipRegion(primary, KmhCompatibility.Explain(card));
        }

        internal const float ActionGap = 6f, FavMin = 110f, FavMax = 150f, PrimaryMin = 150f, PrimaryMax = 200f, RestoreMin = 120f, RestoreMax = 190f;

        // Pure so the self-test can pin that the row never overlaps itself at any width.
        internal static void ActionLayout(float width, bool restore, out float fav, out float restoreW, out float primary)
        {
            primary = Mathf.Clamp(width * 0.34f, PrimaryMin, PrimaryMax);
            fav = Mathf.Clamp(width * 0.26f, FavMin, FavMax);
            restoreW = 0f;
            float left = width - primary - fav - ActionGap;
            if (left < 0f)
            {
                float scale = Mathf.Max(0f, width - ActionGap) / (primary + fav);
                primary *= scale; fav *= scale;
                return;
            }
            if (restore && left - ActionGap >= RestoreMin) restoreW = Mathf.Min(RestoreMax, left - ActionGap);
        }

        internal static bool Fixable(ServerCardDto card, CompatState state)
        {
            if (state == CompatState.ModsAction) return true;
            if (state != CompatState.RwtUnsupported) return false;
            // A version mismatch masks later blockers (full, no address); Prepare re-checks those before joining.
            CompatState mods = KmhCompatibility.ModStateOf(card);
            return mods != CompatState.ModsNeedExpansion;
        }

        private static void OfferRestore()
        {
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                KmhText.Get("KMH_Browser_Restore_Confirm", "Put back the mod list and RimWorld Together copy you had before Colony Nexus changed them? RimWorld has to restart to load them."),
                () =>
                {
                    if (!Features.Mods.KmhJoinPreparation.RestoreModList(out string why, out var outcome))
                    {
                        Messages.Message(KmhText.Get("KMH_Prep_RestoreFailed", "Your mod list could not be restored: {0}", KmhDisplayText.Inert(why, 160)), MessageTypeDefOf.RejectInput, false);
                        return;
                    }
                    ModProfileClient.Invalidate();
                    AfterRestore(outcome, () => { try { GenCommandLine.Restart(); } catch (Exception ex) { RestartFailed(ex); } });
                },
                destructive: false));
        }

        // Shared by the browser and the preparation window: "restored" is only ever said when something was.
        internal static void AfterRestore(Features.Enforcement.ModsConfigJournal.RestoreOutcome outcome, Action restart)
        {
            string note = Features.Mods.KmhJoinPreparation.RestoreOutcomeNote(outcome);
            if (outcome == Features.Enforcement.ModsConfigJournal.RestoreOutcome.SavedCopyWasAServerList)
            {
                Find.WindowStack.Add(new Features.Mods.Dialog_KMHRecoverModList(note));
                return;
            }
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                (note.Length > 0 ? note : KmhText.Get("KMH_Prep_Restored", "Your own mod list and RimWorld Together copy are back on disk. RimWorld loads them when it restarts - and until then, opening the Mods page would undo this.")) + "\n\n"
              + KmhText.Get("KMH_Browser_ModPlan_RestartNow", "Restart now?"),
                restart, destructive: false));
        }

        private static void RestartFailed(Exception ex)
        {
            Diagnostics.KmhLog.Warn($"Browser: restarting after a mod list change failed: {ex.Message}");
            Messages.Message(KmhText.Get("KMH_Browser_ModPlan_RestartFailed",
                "Colony Nexus could not restart RimWorld for you. Close and reopen it yourself before opening the Mods page."),
                MessageTypeDefOf.RejectInput, false);
        }

        private void Join(ServerCardDto card)
        {
            // Re-checked on click: a refresh may have changed the row since it drew an enabled Join.
            Features.Discord.JoinDecision decision = Features.Discord.KmhJoinGate.MayJoin(
                card, directoryFresh: true, source: Features.Discord.JoinSource.Browser);
            if (!decision.Allowed)
            {
                Messages.Message(decision.Explain(card), MessageTypeDefOf.RejectInput, false);
                return;
            }

            if (!card.TrySplitEndpoint(out string host, out int port))
            {
                Messages.Message(KmhText.Get("KMH_Browser_Join_NoAddress",
                    "This server has not published a connection address."), MessageTypeDefOf.RejectInput, false);
                return;
            }

            // RWT's own Join asks for an account first; connecting without one sends an empty name.
            if (RwtCompat.LoginLooksValid() == false)
            {
                if (RwtCompat.PromptLogin())
                    Messages.Message(KmhText.Get("KMH_Browser_Join_NeedsAccount",
                        "RimWorld Together needs a player name and password first. Save them in its Account Setup window, then press Join again."),
                        MessageTypeDefOf.CautionInput, false);
                else
                    Messages.Message(KmhText.Get("KMH_Prep_NoAccountPrompt", "RimWorld Together has no saved account and Colony Nexus could not open its account window. Use RimWorld Together's own Join once to create one."),
                        MessageTypeDefOf.RejectInput, false);
                return;
            }

            // Recorded before connecting, so a server that refuses at the door is still in Recent to try again.
            Features.Servers.SeenServersStore.Record(card.ServerId, card.Name, card.JoinEndpoint,
                                                     card.KmhVersion, card.RwtVersion);
            if (!RwtCompat.ConnectTo(host, port, card.ServerId))
            {
                Messages.Message(KmhText.Get("KMH_Browser_Join_Failed",
                    "RimWorld Together would not start the connection."), MessageTypeDefOf.RejectInput, false);
                return;
            }
            Close();
        }

        // Built by this client from the directory's own address, so it is the one link here no owner typed.
        internal static string WebPageUrl(ServerCardDto card)
            => Account.KmhGlobalCapabilities.PublicSite && card != null && card.ServerId != null && card.ServerId.Length == 32
                ? GlobalHttp.BaseUrl.TrimEnd('/') + "/servers/" + card.ServerId : "";

        private static void LinkRow(Listing_Standard list, string label, string url)
        {
            Rect r = list.GetRect(24f);
            Widgets.Label(r.LeftPart(0.3f), label.Truncate(r.width * 0.3f - 4f));
            Rect copy = new Rect(r.xMax - 70f, r.y, 70f, 22f);
            Rect text = new Rect(r.x + r.width * 0.3f, r.y, r.width * 0.7f - 76f, 22f);
            GUI.color = new Color(0.72f, 0.82f, 0.95f);
            Widgets.Label(text, url.Truncate(text.width));
            GUI.color = Color.white;
            if (Widgets.ButtonText(copy, KmhText.Get("KMH_Browser_Copy", "Copy")))
            {
                GUIUtility.systemCopyBuffer = url;
                Notifications.KmhNotifications.Positive(
                    KmhText.Get("KMH_Browser_Copied", "Copied to clipboard."));
            }
            TooltipHandler.TipRegion(text, url);
        }

        private static void Row(Listing_Standard list, string label, string value)
        {
            Rect r = list.GetRect(24f);
            string shown = string.IsNullOrEmpty(value) ? "—" : value;
            Rect left = r.LeftPart(0.45f), right = r.RightPart(0.55f);
            Widgets.Label(left, label.Truncate(left.width - 4f));
            Text.Anchor = TextAnchor.UpperRight;
            Widgets.Label(right, shown.Truncate(right.width));
            Text.Anchor = TextAnchor.UpperLeft;
            if (Text.CalcSize(shown).x > right.width) TooltipHandler.TipRegion(right, shown);
        }

        // By id, not from the ranked page, so a friend's or favourite's server past page one still opens.
        private ServerCardDto Selected() => string.IsNullOrEmpty(_selectedServerId) ? null : KmhDirectoryClient.CardById(_selectedServerId);
    }
}
