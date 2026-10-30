using System;
using System.Collections.Generic;
using KMHPatch.Features.Chat;
using KMHPatch.Features.Guilds;
using KMHPatch.Features.LinkedAccounts;
using KMHPatch.Features.PlayerStats;
using KMHPatch.Features.PlayerStats.Dto;
using KMHPatch.Features.Reputation;
using KMHPatch.Features.Reputation.Dto;
using KMHPatch.Features.Seasons;
using KMHPatch.Features.Seasons.Dto;
using KMHPatch.UI;
using UnityEngine;
using Verse;

namespace KMHPatch.Features.Standings
{
    public class Dialog_KMHStandings : Window_KMHBase
    {
        public override Vector2 InitialSize => KMHPatch.UI.DialogLayout.FitToScreen(Mathf.Clamp(Verse.UI.screenWidth * 0.82f, 1280f, 1700f),
                                                                                  Mathf.Clamp(Verse.UI.screenHeight * 0.86f, 680f, 1000f));

        private enum Section { Leaderboards, Player, Guild, Members, Colony, Colonist, Trade, Contract, Battle, Site, Activity, Reputation, Season }

        private static readonly Section[] Nav =
        {
            Section.Leaderboards, Section.Player, Section.Guild, Section.Members, Section.Colony, Section.Colonist, Section.Trade,
            Section.Contract, Section.Battle, Section.Site, Section.Activity, Section.Reputation, Section.Season,
        };

        private static string NavLabel(Section s)
        {
            switch (s)
            {
                case Section.Player:     return KmhText.Get("KMH_St_NavPlayer", "Player Standings");
                case Section.Guild:      return KmhText.Get("KMH_St_NavGuild", "Guild Standings");
                case Section.Members:    return KmhText.Get("KMH_St_NavMembers", "Member Contributions");
                case Section.Colony:     return KmhText.Get("KMH_St_NavColony", "Colony Records");
                case Section.Colonist:   return KmhText.Get("KMH_St_NavColonist", "Colonist Records");
                case Section.Trade:      return KmhText.Get("KMH_St_NavTrade", "Trade Records");
                case Section.Contract:   return KmhText.Get("KMH_St_NavContract", "Contract Records");
                case Section.Battle:     return KmhText.Get("KMH_St_NavBattle", "Battle Records");
                case Section.Site:       return KmhText.Get("KMH_St_NavSite", "Site Records");
                case Section.Activity:   return KmhText.Get("KMH_St_NavActivity", "Activity Records");
                case Section.Reputation: return KmhText.Get("KMH_St_NavReputation", "Reputation Records");
                case Section.Season:     return KmhText.Get("KMH_St_NavSeason", "Season Archive");
                default:                 return KmhText.Get("KMH_St_NavLeaderboards", "Leaderboards");
            }
        }

        private Section _section = Section.Leaderboards;
        private readonly LeaderboardsView _boards = new LeaderboardsView();
        private readonly int[] _tab = new int[Enum.GetValues(typeof(Section)).Length];
        private Vector2 _scroll;
        private float   _refresh = RefreshSeconds;

        // Rebuilt only when the section/tab or snapshot changes - sorting a big roster every frame is not free.
        private List<PlayerLeaderboardEntry> _pView; private string _pKey = ""; private int[] _pRanks;
        private List<GuildAgg>               _gView; private string _gKey = ""; private int[] _gRanks;
        private List<ColonistEntry>          _cView; private string _cKey = ""; private int[] _cRanks;

        // The profile sits beside the board when both fit; on a small screen it is a docked window instead.
        private PlayerProfilePanel _drawer;
        private Dialog_KMHPlayerProfile _popup;
        private float _contentW;

        public Dialog_KMHStandings()
        {
            doCloseX = true; absorbInputAroundWindow = true; forcePause = false; draggable = true; resizeable = true;
            _boards.OpenProfile = n => ShowProfile(n);
            RequestAll();
            _boards.Asked();
        }

        private static float DrawerWidth(float contentW) => ProfileDrawer.Width(contentW);
        private static bool DrawerFits(float contentW) => ProfileDrawer.Fits(contentW);

        private void ShowProfile(string username, string colonist = null)
        {
            if (string.IsNullOrEmpty(username)) return;
            _shownColonist = colonist;
            if (DrawerFits(_contentW))
            {
                if (_drawer == null) _drawer = new PlayerProfilePanel(username, colonist);
                else _drawer.Focus(username, colonist);
                return;
            }
            // Retargeted in place; KeepDockedProfileOnTop lifts it back over the board the row click raised.
            if (_popup != null && Find.WindowStack.IsOpen(_popup)) { _popup.Show(username, colonist); _raiseProfile = true; return; }
            _popup = new Dialog_KMHPlayerProfile(username, colonist, beside: true);
            Find.WindowStack.Add(_popup);
        }

        private string Shown => _drawer?.Username ?? (_popup != null && Find.WindowStack.IsOpen(_popup) ? _popup.Username : null);

        private bool IsShown(string username) { string s = Shown; return s != null && KmhSession.Same(s, username); }

        private bool IsShown(string owner, string colonist) => IsShown(owner) && !string.IsNullOrEmpty(colonist) && colonist == _shownColonist;
        private string _shownColonist;

        public override void OnCancelKeyPressed()
        {
            if (_drawer == null) { base.OnCancelKeyPressed(); return; }
            _drawer.Closed(); _drawer = null; _shownColonist = null;
            Event.current?.Use();
        }

        public override void PostClose()
        {
            base.PostClose();
            _drawer?.Closed();
            _drawer = null;
            if (_popup != null && Find.WindowStack.IsOpen(_popup)) _popup.Close(false);
            _popup = null;
        }

        private void RequestAll()
        {
            for (int i = 0; i < _sectionAsked.Length; i++) _sectionAsked[i] = Time.realtimeSinceStartup;
            // This player's colony report first, or their own row stays blank until the timer-driven report fires.
            GameComponent_KMHColonyReporter.SendNow();
            PlayerStatsHandler.RequestSnapshot();
            PlayerStatsHandler.RequestColonistRoster();
            GuildHandler.RequestLeaderboard();
            ReputationCache.RequestSnapshot();
            SeasonHandler.RequestSnapshot();
            Leaderboards.LeaderboardHandler.RequestSnapshot();
            ChatRosterCache.RequestIfStale();
        }

        // Everything once on open; after that only what the shown section draws, once a minute or on switching to it.
        private const float RefreshSeconds = 60f, SwitchCooldown = 15f;
        private readonly float[] _sectionAsked = new float[Enum.GetValues(typeof(Section)).Length];

        private void RequestSection(Section s)
        {
            _sectionAsked[(int)s] = Time.realtimeSinceStartup;
            switch (s)
            {
                case Section.Leaderboards: Leaderboards.LeaderboardHandler.RequestSnapshot(); break;
                case Section.Colonist:     PlayerStatsHandler.RequestColonistRoster(); break;
                case Section.Season:       SeasonHandler.RequestSnapshot(); break;
                case Section.Guild:        GuildHandler.RequestLeaderboard(); PlayerStatsHandler.RequestSnapshot(); ReputationCache.RequestSnapshot(); break;
                case Section.Reputation:   ReputationCache.RequestSnapshot(); PlayerStatsHandler.RequestSnapshot(); break;
                default:                   PlayerStatsHandler.RequestSnapshot(); ReputationCache.RequestSnapshot(); break;
            }
            // The profile beside the board reads the player list whatever the section.
            if (_drawer != null && (s == Section.Leaderboards || s == Section.Colonist || s == Section.Season)) PlayerStatsHandler.RequestSnapshot();
            ChatRosterCache.RequestIfStale();
        }

        private void SwitchTo(Section s)
        {
            _section = s; _scroll = Vector2.zero;
            if (Time.realtimeSinceStartup - _sectionAsked[(int)s] > SwitchCooldown) RequestSection(s);
        }

        public override void WindowUpdate()
        {
            base.WindowUpdate();
            _refresh -= Time.unscaledDeltaTime;
            if (_refresh <= 0f) { _refresh = RefreshSeconds; RequestSection(_section); }
        }

        protected override void DrawContents(Rect rect)
        {
            float y = DialogLayout.DrawTitle(rect, KmhText.Get("KMH_St_Title", "Server Standings"));
            DialogLayout.LabelTrunc(new Rect(0f, y, rect.width, DialogLayout.TextRowH),
                "<color=#A9AFB9>" + KmhText.Get("KMH_St_Subtitle", "Season {0} · totals are cumulative on this server; each season's leaders are kept in the Season Archive.",
                                                SeasonArchiveCache.Snapshot?.CurrentSeason ?? 1) + "</color>");
            y += DialogLayout.TextRowH;
            DialogLayout.DrawSectionDivider(rect, ref y);

            // Proportional, or on a clamped window the nav eats a quarter of the table it exists to navigate.
            float navW = Mathf.Clamp(rect.width * 0.22f, 130f, 196f);
            Rect nav = new Rect(0f, y, navW, DialogLayout.BodyHeight(rect, y));
            Widgets.DrawMenuSection(nav);
            DrawNav(nav);

            Rect content = new Rect(navW + 10f, y, rect.width - navW - 10f, DialogLayout.BodyHeight(rect, y));
            _contentW = content.width;
            // Shrunk below the drawer's room: the profile moves to its docked window rather than crushing the board.
            if (_drawer != null && !DrawerFits(content.width))
            {
                string user = _drawer.Username, colonist = _shownColonist;
                _drawer.Closed(); _drawer = null;
                ShowProfile(user, colonist);
            }
            if (_drawer != null)
            {
                float dw = DrawerWidth(content.width);
                Rect drawer = new Rect(content.xMax - dw, content.y, dw, content.height);
                content.width -= dw + 10f;
                _boards.Selected = _drawer.Username;
                DrawSection(content);
                Widgets.DrawBoxSolid(drawer, KmhBrand.WithAlpha(KmhBrand.Surface, 0.92f));
                Widgets.DrawBoxSolid(new Rect(drawer.x, drawer.y, 2f, drawer.height), KmhBrand.WithAlpha(KmhBrand.Accent, 0.6f));
                if (_drawer.Draw(drawer.ContractedBy(10f), closable: true)) { _drawer.Closed(); _drawer = null; _shownColonist = null; }
            }
            else
            {
                _boards.Selected = Shown;
                DrawSection(content);
            }

            if (DialogLayout.DrawCloseButton(rect)) Close();
            KeepDockedProfileOnTop();
        }

        // A board click buries the docked profile; lift it back unless a field is being typed in and no row asked for it.
        private void KeepDockedProfileOnTop()
        {
            bool docked = _popup != null && Find.WindowStack.IsOpen(_popup);
            // The stack never raises a window from under one that absorbs input around itself.
            absorbInputAroundWindow = !docked;
            if (!docked) { _raiseProfile = false; return; }
            if (Event.current.type != EventType.Repaint || (GUIUtility.keyboardControl != 0 && !_raiseProfile)) return;
            var stack = Find.WindowStack.Windows;
            if (stack.IndexOf(_popup) < stack.IndexOf(this) && Find.WindowStack.GetsInput(_popup))
            {
                if (_raiseProfile) GUIUtility.keyboardControl = 0;
                Find.WindowStack.Notify_ClickedInsideWindow(_popup);
            }
            _raiseProfile = false;
        }

        private bool _raiseProfile;

        private Vector2 _navScroll;

        // Scrolls: on a short window the last sections would otherwise draw below the panel and be unclickable.
        private void DrawNav(Rect box)
        {
            Rect inner = box.ContractedBy(6f);
            const float h = 30f, pitch = 32f;
            float contentH = Nav.Length * pitch;
            Rect view = new Rect(0f, 0f,
                                 Mathf.Max(1f, inner.width - (contentH > inner.height ? DialogLayout.ScrollbarReserveWidth : 0f)),
                                 Mathf.Max(contentH, inner.height));

            Widgets.BeginScrollView(inner, ref _navScroll, view);
            for (int i = 0; i < Nav.Length; i++)
            {
                Rect r = new Rect(0f, i * pitch, view.width, h);
                bool active = _section == Nav[i];
                if (active)
                {
                    Widgets.DrawBoxSolid(r, KmhBrand.WithAlpha(KmhBrand.Accent, 0.16f));
                    Widgets.DrawBoxSolid(new Rect(r.x, r.y, 3f, r.height), KmhBrand.Accent);
                }
                Widgets.DrawHighlightIfMouseover(r);
                Color old = GUI.color; if (active) GUI.color = KmhBrand.AccentStrong;
                DialogLayout.LabelTrunc(new Rect(r.x + 8f, r.y, r.width - 12f, h), NavLabel(Nav[i]));
                GUI.color = old;
                if (Widgets.ButtonInvisible(r) && _section != Nav[i]) SwitchTo(Nav[i]);
            }
            Widgets.EndScrollView();
        }

        private void DrawSection(Rect c)
        {
            switch (_section)
            {
                case Section.Leaderboards: _boards.Draw(c); break;
                case Section.Guild:      DrawGuildBoard(c); break;
                case Section.Colonist:   DrawColonistBoard(c); break;
                case Section.Reputation: DrawReputationBoard(c); break;
                case Section.Season:     DrawSeason(c); break;
                default:                 DrawPlayerSection(c); break;
            }
        }

        private int SectionTabs(Rect c, Section s, string[] labels)
        {
            int tab = _tab[(int)s];
            if (StandingsTable.Tabs(new Rect(c.x, c.y, c.width, StandingsTable.TabsH(labels, c.width)), labels, ref tab)) { _tab[(int)s] = tab; _scroll = Vector2.zero; }
            return Mathf.Clamp(tab, 0, labels.Length - 1);
        }

        private static Rect TableBelowTabs<T>(Rect c, string[] labels, Col<T>[] cols, int accent, bool hasInfo)
        {
            float hy = c.y + StandingsTable.TabsH(labels, c.width) + 6f;
            StandingsTable.Header(new Rect(c.x, hy, c.width, 22f), cols, accent, hasInfo);
            Rect list = new Rect(c.x, hy + 24f, c.width, c.height - (hy + 24f - c.y));
            Widgets.DrawMenuSection(list);
            return list;
        }

        // One recorded value per tab: the sort and the ranks both come from it, so no tab ranks by a made-up blend.
        private sealed class Tab
        {
            public string Label; public Func<PlayerLeaderboardEntry, long> Value; public int Accent;
            public Tab(string l, Func<PlayerLeaderboardEntry, long> value, int accent) { Label = l; Value = value; Accent = accent; }
            public int Sort(PlayerLeaderboardEntry a, PlayerLeaderboardEntry b) => Then(Value(b).CompareTo(Value(a)), a, b);
        }

        private void DrawPlayerSection(Rect c)
        {
            (Col<PlayerLeaderboardEntry>[] cols, Tab[] tabs) = PlayerBoardDef(_section);

            string[] labels = new string[tabs.Length];
            for (int i = 0; i < tabs.Length; i++) labels[i] = tabs[i].Label;
            int tab = SectionTabs(c, _section, labels);
            Rect list = TableBelowTabs(c, labels, cols, tabs[tab].Accent, hasInfo: true);

            string key = $"{(int)_section}:{tab}:{PlayerStatsCache.LastUpdatedUtc.Ticks}:{ReputationCache.Version}";
            if (_pView == null || _pKey != key)
            {
                _pView = new List<PlayerLeaderboardEntry>(StandingsData.Players());
                _pView.Sort(tabs[tab].Sort);
                _pRanks = StandingsTable.Ranks(_pView, tabs[tab].Value);
                _pKey = key;
            }
            List<PlayerLeaderboardEntry> rows = _pView;
            string me = KmhSession.Me;
            StandingsTable.Draw(list, rows, cols, ref _scroll,
                e => KmhSession.Same(e.Username, me),
                e => ShowProfile(e.Username), _pRanks, e => IsShown(e.Username));
        }

        private static (Col<PlayerLeaderboardEntry>[], Tab[]) PlayerBoardDef(Section s)
        {
            Col<PlayerLeaderboardEntry> Name   = new Col<PlayerLeaderboardEntry>(C_Player, 0.00f, e => Fmt(e.Username));
            Col<PlayerLeaderboardEntry> Colony = new Col<PlayerLeaderboardEntry>(C_Colony, 0.17f, e => e.LastReportUtcTicks > 0 ? Str(e.ColonyName) : NotReportedLong);
            Col<PlayerLeaderboardEntry> Guild  = new Col<PlayerLeaderboardEntry>(C_Guild,  0.30f, e => Str(KmhDisplayText.GuildName(e.GuildName)));
            string contracts = KmhText.Get("KMH_St_C_Contracts", "Contracts"), trade = KmhText.Get("KMH_St_C_Trade", "Trade");
            string siteWork = KmhText.Get("KMH_St_C_SiteWork", "Site Work"), workerXp = KmhText.Get("KMH_St_C_WorkerXp", "Worker XP");
            string kills = KmhText.Get("KMH_St_C_Kills", "Kills"), raids = KmhText.Get("KMH_St_C_Raids", "Raids");
            string age = KmhText.Get("KMH_St_C_Age", "Age"), connected = KmhText.Get("KMH_St_C_Connected", "Connected");
            string focus = KmhText.Get("KMH_St_C_Focus", "Focus"), volume = KmhText.Get("KMH_St_C_Volume", "Volume");
            string bounties = KmhText.Get("KMH_St_C_Bounties", "Bounties"), deliveries = KmhText.Get("KMH_St_C_Deliveries", "Deliveries");
            string hunts = KmhText.Get("KMH_St_C_Hunts", "Hunts"), wealth = KmhText.Get("KMH_St_C_Wealth", "Wealth");

            switch (s)
            {
                case Section.Members:
                    string contribution = KmhText.Get("KMH_St_C_Contribution", "Contribution");
                    return (new[]
                    {
                        Name, Guild,
                        Col(contribution, 0.42f, e => e.EconomyScore.ToString("N0")),
                        Col(KmhText.Get("KMH_St_C_Donated", "Donated"), 0.56f, e => Silver(e.SilverDonated)),
                        Col(contracts,    0.68f, e => e.QuestsCompleted.ToString()),
                        Col(siteWork,     0.78f, e => e.WorkerXp.ToString("N0")),
                        Col(trade,        0.88f, e => Silver(e.SalesEarned + e.PurchasesSpent)),
                    }, new[]
                    {
                        new Tab(contribution, e => e.EconomyScore, 2),
                        new Tab(KmhText.Get("KMH_St_T_Donations", "Donations"), e => e.SilverDonated, 3),
                        new Tab(contracts,    e => e.QuestsCompleted, 4),
                        new Tab(siteWork,     e => e.WorkerXp, 5),
                        new Tab(trade,        e => e.SalesEarned + e.PurchasesSpent, 6),
                    });

                case Section.Colony:
                    return (new[]
                    {
                        new Col<PlayerLeaderboardEntry>(C_Colony, 0.00f, e => e.LastReportUtcTicks > 0 ? Str(e.ColonyName) : NotReportedLong),
                        new Col<PlayerLeaderboardEntry>(C_Player, 0.18f, e => Fmt(e.Username)),
                        // Settlements only: this board's other columns are all on-map, and Player Standings ranks the total.
                        Col(KmhText.Get("KMH_St_C_Settlements", "Settlements"), 0.34f, e => R(e, Silver(e.Wealth))),
                        Col(age,     0.47f, e => R(e, Days(e.ColonyAgeDays))),
                        Col(KmhText.Get("KMH_St_C_Pop", "Pop"), 0.56f, e => R(e, e.Population.ToString("N0"))),
                        Col(KmhText.Get("KMH_St_C_Dev", "Dev"), 0.65f, e => R(e, e.DevelopmentScore.ToString("N0"))),
                        Col(KmhText.Get("KMH_St_C_Defense", "Defense"), 0.76f, e => R(e, e.DefenseScore.ToString("N0"))),
                        Col(raids,   0.88f, e => R(e, e.RaidsSurvived.ToString("N0"))),
                    }, new[]
                    {
                        new Tab(KmhText.Get("KMH_St_T_Richest", "Richest"),             e => e.Wealth, 2),
                        new Tab(KmhText.Get("KMH_St_T_Oldest", "Oldest"),               e => e.ColonyAgeDays, 3),
                        new Tab(KmhText.Get("KMH_St_T_Largest", "Largest"),             e => e.Population, 4),
                        new Tab(KmhText.Get("KMH_St_T_MostDeveloped", "Most Developed"), e => e.DevelopmentScore, 5),
                        new Tab(KmhText.Get("KMH_St_T_BestDefended", "Best Defended"),  e => e.DefenseScore, 6),
                    });

                case Section.Trade:
                    return (new[]
                    {
                        Name, Colony,
                        Col(KmhText.Get("KMH_St_C_Earned", "Earned"),   0.40f, e => Silver(e.SalesEarned)),
                        Col(KmhText.Get("KMH_St_C_Spent", "Spent"),     0.51f, e => Silver(e.PurchasesSpent)),
                        Col(KmhText.Get("KMH_St_C_Sold", "Sold"),       0.62f, e => e.ItemsSold.ToString("N0")),
                        Col(KmhText.Get("KMH_St_C_Bought", "Bought"),   0.71f, e => e.ItemsBought.ToString("N0")),
                        Col(KmhText.Get("KMH_St_C_Largest", "Largest"), 0.80f, e => Silver(e.LargestSale)),
                        Col(volume,  0.90f, e => Silver(e.SalesEarned + e.PurchasesSpent)),
                    }, new[]
                    {
                        new Tab(KmhText.Get("KMH_St_T_TopSellers", "Top Sellers"),   e => e.SalesEarned, 2),
                        new Tab(KmhText.Get("KMH_St_T_TopBuyers", "Top Buyers"),     e => e.PurchasesSpent, 3),
                        new Tab(KmhText.Get("KMH_St_T_MostSold", "Most Sold"),       e => e.ItemsSold, 4),
                        new Tab(KmhText.Get("KMH_St_T_LargestSale", "Largest Sale"), e => e.LargestSale, 6),
                        new Tab(volume,       e => e.SalesEarned + e.PurchasesSpent, 7),
                    });

                case Section.Contract:
                    return (new[]
                    {
                        Name, Colony,
                        Col(KmhText.Get("KMH_St_C_Completed", "Completed"), 0.40f, e => e.QuestsCompleted.ToString()),
                        Col(bounties,   0.51f, e => e.ContractsBounty.ToString()),
                        Col(deliveries, 0.60f, e => e.ContractsDeliver.ToString()),
                        Col(hunts,      0.71f, e => e.ContractsHunt.ToString()),
                        Col(KmhText.Get("KMH_St_C_Failed", "Failed"), 0.80f, e => e.ContractsFailed.ToString()),
                        Col(KmhText.Get("KMH_St_C_Streak", "Streak"), 0.89f, e => e.ContractStreak.ToString()),
                    }, new[]
                    {
                        new Tab(KmhText.Get("KMH_St_T_TopContractors", "Top Contractors"), e => e.QuestsCompleted, 2),
                        new Tab(bounties,        e => e.ContractsBounty, 3),
                        new Tab(deliveries,      e => e.ContractsDeliver, 4),
                        new Tab(hunts,           e => e.ContractsHunt, 5),
                        new Tab(KmhText.Get("KMH_St_T_Streaks", "Streaks"), e => e.ContractStreak, 7),
                    });

                case Section.Battle:
                    return (new[]
                    {
                        Name, Colony,
                        Col(kills,  0.38f, e => R(e, e.Kills.ToString("N0"))),
                        Col(KmhText.Get("KMH_St_C_Human", "Human"),   0.50f, e => R(e, e.KillsHumanlike.ToString("N0"))),
                        Col(KmhText.Get("KMH_St_C_Mech", "Mech"),     0.60f, e => R(e, e.KillsMechanoid.ToString("N0"))),
                        Col(KmhText.Get("KMH_St_C_Animal", "Animal"), 0.70f, e => R(e, e.KillsAnimal.ToString("N0"))),
                        Col(raids,  0.80f, e => R(e, e.RaidsSurvived.ToString("N0"))),
                        Col(KmhText.Get("KMH_St_C_Losses", "Losses"), 0.90f, e => R(e, e.PawnsLost.ToString("N0"))),
                    }, new[]
                    {
                        // No "fewest losses" tab: a player who never reported a colony has lost nobody and would lead it.
                        new Tab(KmhText.Get("KMH_St_T_Deadliest", "Deadliest"), e => e.Kills, 2),
                        new Tab(KmhText.Get("KMH_St_T_Mechanoid", "Mechanoid"), e => e.KillsMechanoid, 4),
                        new Tab(KmhText.Get("KMH_St_T_Humanlike", "Humanlike"), e => e.KillsHumanlike, 3),
                        new Tab(KmhText.Get("KMH_St_C_Animal", "Animal"),       e => e.KillsAnimal, 5),
                        new Tab(raids,     e => e.RaidsSurvived, 6),
                    });

                case Section.Activity:
                    return (new[]
                    {
                        Name, Colony, Guild,
                        Col(KmhText.Get("KMH_St_C_Status", "Status"), 0.42f, e => !ChatRosterCache.Known ? StandingsTable.Dash
                                                     : ChatRosterCache.IsOnline(e.Username) ? KmhText.Get("KMH_St_Online", "Online")
                                                     : "<color=#A9AFB9>" + KmhText.Get("KMH_St_Offline", "Offline") + "</color>"),
                        Col(KmhText.Get("KMH_St_C_Active", "Active"), 0.53f, e => Span(e.ActiveSeconds)),
                        Col(connected, 0.64f, e => Span(e.ConnectedSeconds)),
                        Col(focus,     0.76f, e => FocusCell(e)),
                        Col(KmhText.Get("KMH_St_C_LastSeen", "Last Seen"), 0.86f, e => Str(KmhAgo.Since(e.LastSeenUtcTicks))),
                    }, new[]
                    {
                        new Tab(KmhText.Get("KMH_St_T_MostActive", "Most Active"), e => e.ActiveSeconds, 4),
                        new Tab(connected,    e => e.ConnectedSeconds, 5),
                        new Tab(focus,        e => Focus(e), 6),
                        new Tab(KmhText.Get("KMH_St_T_RecentlySeen", "Recently Seen"), e => e.LastSeenUtcTicks, 7),
                    });

                case Section.Site:
                    return (new[]
                    {
                        Name, Colony,
                        Col(KmhText.Get("KMH_St_C_Sites", "Sites"),       0.40f, e => e.SitesOwned.ToString()),
                        Col(KmhText.Get("KMH_St_C_Produced", "Produced"), 0.52f, e => Silver(e.SiteSilverProduced)),
                        Col(workerXp,  0.66f, e => e.WorkerXp.ToString("N0")),
                        Col(KmhText.Get("KMH_St_C_Outposts", "Outposts"), 0.80f, e => e.OutpostsHeld > 0 || e.FrontierCaptures > 0
                                                       ? KmhText.Get("KMH_St_OutpostsCell", "{0} ({1} taken)", e.OutpostsHeld, e.FrontierCaptures) : StandingsTable.Dash),
                    }, new[]
                    {
                        new Tab(KmhText.Get("KMH_St_T_TopOwners", "Top Owners"),  e => e.SitesOwned, 2),
                        new Tab(KmhText.Get("KMH_St_T_Production", "Production"), e => e.SiteSilverProduced, 3),
                        new Tab(workerXp,         e => e.WorkerXp, 4),
                        new Tab(KmhText.Get("KMH_St_T_Frontier", "Frontier"),     e => e.FrontierCaptures, 5),
                    });

                default:
                    string time = KmhText.Get("KMH_St_C_Time", "Time");
                    return (new[]
                    {
                        Name, Colony, Guild,
                        Col(age,       0.40f, e => R(e, Days(e.ColonyAgeDays))),
                        Col(time,      0.48f, e => R(e, Hours(e.TimePlayedHours))),
                        Col(wealth,    0.56f, e => Silver(e.TotalWealth)),
                        Col(kills,     0.67f, e => R(e, e.Kills.ToString("N0"))),
                        Col(contracts, 0.75f, e => e.QuestsCompleted.ToString()),
                        Col(KmhText.Get("KMH_St_C_Rep", "Rep"), 0.83f, e => RepCell(e.Username)),
                        Col(KmhText.Get("KMH_St_C_Top", "Top"), 0.90f, e => R(e, Str(e.TopColonistName))),
                    }, new[]
                    {
                        // No "Overall" blend of unrelated numbers; the Leaderboards section has the server's economy score.
                        new Tab(wealth,     e => e.TotalWealth, 5),
                        new Tab(KmhText.Get("KMH_St_T_Combat", "Combat"), e => e.Kills, 6),
                        new Tab(contracts,  e => e.QuestsCompleted, 7),
                        new Tab(KmhText.Get("KMH_St_T_Reputation", "Reputation"), e => StandingsData.Rep(e.Username), 8),
                        new Tab(time,       e => e.TimePlayedHours, 4),
                        new Tab(KmhText.Get("KMH_St_T_ColonyAge", "Colony Age"), e => e.ColonyAgeDays, 3),
                        new Tab(KmhText.Get("KMH_St_T_TopColonist", "Top Colonist"), e => e.TopColonistKills, 9),
                    });
            }
        }

        private static string C_Player => KmhText.Get("KMH_St_C_Player", "Player");
        private static string C_Colony => KmhText.Get("KMH_St_C_Colony", "Colony");
        private static string C_Guild  => KmhText.Get("KMH_St_C_Guild", "Guild");

        // Ties break on the name, which is the only field on a row that is unique and never changes.
        internal static int Then(int primary, PlayerLeaderboardEntry a, PlayerLeaderboardEntry b)
            => primary != 0 ? primary : string.Compare(a?.Username ?? "", b?.Username ?? "", StringComparison.OrdinalIgnoreCase);

        private static int Focus(PlayerLeaderboardEntry e) => StandingsData.Focus(e.ActiveSeconds, e.ConnectedSeconds);

        private static string FocusCell(PlayerLeaderboardEntry e)
        {
            int f = Focus(e);
            return f < 0 ? StandingsTable.Dash : f + "%";
        }

        private void DrawGuildBoard(Rect c)
        {
            string members = KmhText.Get("KMH_St_C_Members", "Members"), treasury = KmhText.Get("KMH_St_C_Treasury", "Treasury");
            string contracts = KmhText.Get("KMH_St_C_Contracts", "Contracts"), sites = KmhText.Get("KMH_St_C_Sites", "Sites");
            string trade = KmhText.Get("KMH_St_C_Trade", "Trade");
            string[] labels =
            {
                KmhText.Get("KMH_St_T_TotalWealth", "Total Wealth"), treasury, members, KmhText.Get("KMH_St_T_Combat", "Combat"),
                contracts, sites, trade, KmhText.Get("KMH_St_T_Reputation", "Reputation"),
            };
            int tab = SectionTabs(c, Section.Guild, labels);

            Col<GuildAgg>[] cols =
            {
                new Col<GuildAgg>(C_Guild,   0.00f, g => KmhDisplayText.GuildName(g.Name)),
                new Col<GuildAgg>(members,   0.26f, g => g.Members.ToString()),
                new Col<GuildAgg>(treasury,  0.38f, g => SilverFmt.Format(g.Treasury)),
                new Col<GuildAgg>(KmhText.Get("KMH_St_C_Wealth", "Wealth"), 0.50f, g => SilverFmt.Format(g.Wealth)),
                new Col<GuildAgg>(KmhText.Get("KMH_St_C_Kills", "Kills"),   0.62f, g => g.Kills.ToString("N0")),
                new Col<GuildAgg>(contracts, 0.72f, g => g.Contracts.ToString()),
                new Col<GuildAgg>(sites,     0.81f, g => g.Sites.ToString()),
                new Col<GuildAgg>(trade,     0.89f, g => SilverFmt.Format(g.TradeVolume)),
            };
            int accent = new[] { -1, 2, 1, 4, 5, 6, 7, -1 }[tab];
            Rect list = TableBelowTabs(c, labels, cols, accent, hasInfo: false);

            // Total Wealth is the members' colonies plus the guild's vault: both shown in the row, nothing weighted.
            Func<GuildAgg, long> value = tab switch
            {
                1 => g => g.Treasury,
                2 => g => g.Members,
                3 => g => g.Kills,
                4 => g => g.Contracts,
                5 => g => g.Sites,
                6 => g => g.TradeVolume,
                7 => g => g.RepAvg,
                _ => g => g.Wealth + g.Treasury,
            };
            // Reputation feeds the per-guild average, so a rep update alone must still rebuild the sort.
            string gkey = $"G:{tab}:{PlayerStatsCache.LastUpdatedUtc.Ticks}:{GuildLeaderboardCache.LastUpdatedUtc.Ticks}:{ReputationCache.Version}";
            if (_gView == null || _gKey != gkey)
            {
                _gView = StandingsData.GuildAggs();
                _gView.Sort((a, b) =>
                {
                    int c2 = value(b).CompareTo(value(a));
                    return c2 != 0 ? c2 : string.Compare(a.Name ?? "", b.Name ?? "", StringComparison.OrdinalIgnoreCase);
                });
                _gRanks = StandingsTable.Ranks(_gView, value);
                _gKey = gkey;
            }
            List<GuildAgg> rows = _gView;
            string myGuild = GuildCache.Guild?.Name ?? "";
            StandingsTable.Draw(list, rows, cols, ref _scroll,
                g => !string.IsNullOrEmpty(myGuild) && string.Equals(g.Name, myGuild, StringComparison.OrdinalIgnoreCase), null, _gRanks);
        }

        private List<ReputationEntryDto> _rView; private string _rKey = "";

        private void DrawReputationBoard(Rect c)
        {
            bool inGuild = GuildCache.InGuild && !string.IsNullOrEmpty(GuildCache.Guild?.Name);
            string trusted = KmhText.Get("KMH_St_T_MostTrusted", "Most Trusted"), lowest = KmhText.Get("KMH_St_T_Lowest", "Lowest");
            string[] labels = inGuild ? new[] { trusted, lowest, KmhText.Get("KMH_St_T_MyGuild", "My Guild") }
                                      : new[] { trusted, lowest };
            int tab = SectionTabs(c, Section.Reputation, labels);

            Col<ReputationEntryDto>[] cols =
            {
                new Col<ReputationEntryDto>(C_Player, 0.00f, e => Fmt(e.Username)),
                new Col<ReputationEntryDto>(KmhText.Get("KMH_St_T_Reputation", "Reputation"), 0.45f, e => e.Score.ToString("N0")),
                new Col<ReputationEntryDto>(KmhText.Get("KMH_St_C_Status", "Status"),         0.70f, e => TierLabel(e.Tier)),
            };
            Rect list = TableBelowTabs(c, labels, cols, 1, hasInfo: true);

            string key = $"{tab}:{ReputationCache.Version}:{PlayerStatsCache.LastUpdatedUtc.Ticks}";
            if (_rView == null || _rKey != key)
            {
                _rView = ReputationCache.Leaderboard();
                if (tab == 1) _rView.Reverse();
                else if (tab == 2) _rView = OnlyGuild(_rView, GuildCache.Guild.Name);
                // "Lowest" ranks only those below zero; the others rank only those above it.
                _rRanks = tab == 1 ? StandingsTable.Ranks(_rView, e => -(long)e.Score) : StandingsTable.Ranks(_rView, e => (long)e.Score);
                _rKey = key;
            }

            string me = KmhSession.Me;
            StandingsTable.Draw(list, _rView, cols, ref _scroll,
                e => KmhSession.Same(e.Username, me), e => ShowProfile(e.Username), _rRanks, e => IsShown(e.Username));
        }

        private int[] _rRanks;

        // Reputation rows carry no guild, so membership is joined from the player snapshot rather than guessed.
        private static List<ReputationEntryDto> OnlyGuild(List<ReputationEntryDto> rows, string guild)
        {
            var members = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (PlayerLeaderboardEntry p in StandingsData.Players())
                if (string.Equals(p.GuildName, guild, StringComparison.OrdinalIgnoreCase)) members.Add(p.Username);

            var kept = new List<ReputationEntryDto>();
            foreach (ReputationEntryDto e in rows) if (members.Contains(e.Username)) kept.Add(e);
            return kept;
        }

        // One row per colonist, not per player; each tab is one recorded number, never a weighted blend.
        private void DrawColonistBoard(Rect c)
        {
            string[] labels =
            {
                KmhText.Get("KMH_St_T_Deadliest", "Deadliest"), KmhText.Get("KMH_St_T_BestShooter", "Best Shooter"),
                KmhText.Get("KMH_St_T_BestMelee", "Best Melee"), KmhText.Get("KMH_St_T_BestDoctor", "Best Doctor"),
                KmhText.Get("KMH_St_T_BestCrafter", "Best Crafter"), KmhText.Get("KMH_St_T_BestBuilder", "Best Builder"),
                KmhText.Get("KMH_St_T_Oldest", "Oldest"), KmhText.Get("KMH_St_T_LongestServing", "Longest Serving"),
                KmhText.Get("KMH_St_T_InMemory", "In Memory"),
            };
            int tab = SectionTabs(c, Section.Colonist, labels);

            Col<ColonistEntry>[] cols =
            {
                new Col<ColonistEntry>(KmhText.Get("KMH_St_C_Colonist", "Colonist"), 0.00f, e => Str(e.Name)),
                new Col<ColonistEntry>(C_Player,   0.18f, e => Fmt(e.Owner)),
                new Col<ColonistEntry>(KmhText.Get("KMH_St_C_Kills", "Kills"),   0.33f, e => e.Kills.ToString()),
                new Col<ColonistEntry>(KmhText.Get("KMH_St_C_Shoot", "Shoot"),   0.43f, e => e.SkShooting.ToString()),
                new Col<ColonistEntry>(KmhText.Get("KMH_St_C_Melee", "Melee"),   0.52f, e => e.SkMelee.ToString()),
                new Col<ColonistEntry>(KmhText.Get("KMH_St_C_Doctor", "Doctor"), 0.61f, e => e.SkMedicine.ToString()),
                new Col<ColonistEntry>(KmhText.Get("KMH_St_C_Craft", "Craft"),   0.70f, e => e.SkCrafting.ToString()),
                new Col<ColonistEntry>(KmhText.Get("KMH_St_C_Build", "Build"),   0.79f, e => e.SkConstruction.ToString()),
                tab == 8 ? new Col<ColonistEntry>(KmhText.Get("KMH_St_C_Fell", "Fell"), 0.88f, e => DiedCell(e))
                         : new Col<ColonistEntry>(KmhText.Get("KMH_St_C_AgeDays", "Age/Days"), 0.88f,
                                                  e => KmhText.Get("KMH_St_AgeDaysCell", "{0}y · {1}d", e.Age, e.Days)),
            };
            int accent = new[] { 2, 3, 4, 5, 6, 7, 8, 8, 8 }[tab];
            Rect list = TableBelowTabs(c, labels, cols, accent, hasInfo: true);

            Func<ColonistEntry, long> value = tab switch
            {
                1 => e => e.SkShooting,
                2 => e => e.SkMelee,
                3 => e => e.SkMedicine,
                4 => e => e.SkCrafting,
                5 => e => e.SkConstruction,
                6 => e => e.Age,
                7 => e => e.Days,
                _ => e => e.Kills,
            };
            string ckey = $"C:{tab}:{ColonistRosterCache.LastUpdatedUtc.Ticks}";
            if (tab == 8 && (_cView == null || _cKey != ckey))
            {
                // The server's memorial, newest loss first; remembrance is not a contest, so nobody is ranked.
                List<FallenColonistEntry> fallen = new List<FallenColonistEntry>(ColonistRosterCache.Snapshot?.Fallen ?? new List<FallenColonistEntry>());
                fallen.RemoveAll(f => f?.Colonist == null);
                fallen.Sort((a, b) => b.DiedUtcTicks.CompareTo(a.DiedUtcTicks));
                _cView = new List<ColonistEntry>(fallen.Count);
                _died.Clear();
                foreach (FallenColonistEntry f in fallen) { _cView.Add(f.Colonist); _died[f.Colonist] = f; }
                _cRanks = new int[_cView.Count];
                _cKey = ckey;
            }
            else if (_cView == null || _cKey != ckey)
            {
                _cView = new List<ColonistEntry>(ColonistRosterCache.Colonists);
                _cView.Sort((a, b) =>
                {
                    int c2 = value(b).CompareTo(value(a));
                    if (c2 == 0) c2 = string.Compare(a.Name ?? "", b.Name ?? "", StringComparison.OrdinalIgnoreCase);
                    return c2 != 0 ? c2 : string.Compare(a.Owner ?? "", b.Owner ?? "", StringComparison.OrdinalIgnoreCase);
                });
                _cRanks = StandingsTable.Ranks(_cView, value);
                _cKey = ckey;
            }
            List<ColonistEntry> rows = _cView;
            string me = KmhSession.Me;
            StandingsTable.Draw(list, rows, cols, ref _scroll,
                e => KmhSession.Same(e.Owner, me),
                e => OpenColonistInfo(e, me), _cRanks, e => IsShown(e.Owner, e.Name),
                tab == 8 ? KmhText.Get("KMH_Standings_NoFallen", "No colonist has fallen on this server yet. Colonists the server sees die are remembered here.") : null);
        }

        private readonly Dictionary<ColonistEntry, FallenColonistEntry> _died = new Dictionary<ColonistEntry, FallenColonistEntry>();

        private string DiedCell(ColonistEntry e)
        {
            if (e == null || !_died.TryGetValue(e, out FallenColonistEntry f)) return StandingsTable.Dash;
            string when = f.DiedUtcTicks > 0 ? KmhAgo.Since(f.DiedUtcTicks) : "";
            return f.ColonyDay > 0 ? KmhText.Get("KMH_St_DayN", "day {0}", f.ColonyDay.ToString("N0")) + (when.Length > 0 ? $" <color=#A9AFB9>· {when}</color>" : "")
                                   : (when.Length > 0 ? when : StandingsTable.Dash);
        }

        // A local pawn opens RimWorld's own info card; anyone else's opens their profile at that colonist.
        private void OpenColonistInfo(ColonistEntry e, string me)
        {
            if (e == null) return;
            // A fallen colonist has no pawn to open, and a living namesake would be the wrong one.
            if (KmhSession.Same(e.Owner, me) && !_died.ContainsKey(e))
            {
                Pawn p = FindLocalColonist(e.Id, e.Name);
                if (p != null) { Find.WindowStack.Add(new Dialog_InfoCard(p)); return; }
            }
            ShowProfile(e.Owner, e.Name);
        }

        // By ThingID when sent, so colonists sharing a nickname open the right card; by name for old servers.
        private static Pawn FindLocalColonist(string thingId, string shortName)
        {
            bool byId = !string.IsNullOrEmpty(thingId);
            if (!byId && string.IsNullOrEmpty(shortName)) return null;
            bool Is(Pawn p) => byId ? string.Equals(p.ThingID, thingId, StringComparison.Ordinal)
                                    : string.Equals(p.Name?.ToStringShort, shortName, StringComparison.OrdinalIgnoreCase);
            try
            {
                foreach (Map map in Find.Maps)
                    foreach (Pawn p in map.mapPawns.FreeColonistsSpawned)
                        if (Is(p)) return p;
                foreach (RimWorld.Planet.Caravan car in Find.WorldObjects.Caravans)
                    if (car.IsPlayerControlled)
                        foreach (Pawn p in car.PawnsListForReading)
                            if (p.IsColonist && Is(p)) return p;
            }
            catch { }
            return null;
        }

        private void DrawSeason(Rect c)
        {
            string[] labels =
            {
                KmhText.Get("KMH_St_T_CurrentSeason", "Current Season"), KmhText.Get("KMH_St_T_ServerRecords", "Server Records"),
                KmhText.Get("KMH_St_T_PastSeasons", "Past Seasons"),
            };
            int tab = SectionTabs(c, Section.Season, labels);

            float bodyY = StandingsTable.TabsH(labels, c.width) + 8f;
            Rect body = new Rect(c.x, c.y + bodyY, c.width, Mathf.Max(0f, c.height - bodyY));
            Widgets.DrawMenuSection(body);

            SeasonArchiveSnapshot snap = SeasonArchiveCache.Snapshot;
            if (snap == null)
            {
                DialogLayout.LabelTrunc(new Rect(body.x + 8f, body.y + 8f, body.width - 16f, 22f), "<color=#A9AFB9>" + KmhText.Get("KMH_St_SeasonLoading", "Loading season archive…") + "</color>");
                return;
            }

            List<(string text, bool header)> lines = new List<(string, bool)>();
            void H(string s) => lines.Add((s, true));
            void R(string s) => lines.Add((s, false));

            // Lists come off the wire; default them so a malformed/partial snapshot can't NRE the board.
            List<SeasonRecordDto>  current = snap.Current       ?? new List<SeasonRecordDto>();
            List<SeasonRecordDto>  records = snap.ServerRecords  ?? new List<SeasonRecordDto>();
            List<SeasonArchiveDto> past    = snap.Past           ?? new List<SeasonArchiveDto>();

            if (tab == 0)
            {
                H(KmhText.Get("KMH_St_SeasonInProgress", "Season {0} — in progress", snap.CurrentSeason));
                if (current.Count == 0) R("<color=#A9AFB9>" + KmhText.Get("KMH_St_NoLeaders", "No leaders yet this season.") + "</color>");
                else foreach (SeasonRecordDto r in current) R(RecordLine(r));
            }
            else if (tab == 1)
            {
                H(KmhText.Get("KMH_St_AllTimeRecords", "All-time server records"));
                if (records.Count == 0) R("<color=#A9AFB9>" + KmhText.Get("KMH_St_NoRecords", "No records yet - an admin rolls the season with 'kmh season roll'.") + "</color>");
                else foreach (SeasonRecordDto r in records)
                    R(RecordLine(r) + (r.Season > 0 ? "   <color=#A9AFB9>(" + KmhText.Get("KMH_PP_SeasonLower", "season {0}", r.Season) + ")</color>" : ""));
            }
            else
            {
                if (past.Count == 0) R("<color=#A9AFB9>" + KmhText.Get("KMH_St_NoPastSeasons", "No past seasons yet. Each season is archived when an admin runs 'kmh season roll'.") + "</color>");
                else foreach (SeasonArchiveDto s in past)
                {
                    H(KmhText.Get("KMH_St_SeasonEnded", "Season {0} — ended {1}", s.Season, FormatDate(s.EndedUtcTicks)));
                    foreach (SeasonRecordDto r in s.Records ?? new List<SeasonRecordDto>()) R(RecordLine(r));
                    lines.Add(("", false));
                }
            }
            DrawSeasonLines(body, lines);
        }

        private static string RecordLine(SeasonRecordDto r)
            => $"<b>{KmhDisplayText.Inert(r.Category ?? "", 60)}</b>:  {LinkedAccountsCache.Format(r.Holder)}  <color=#A9AFB9>· {KmhDisplayText.Inert(r.Detail ?? "", 200)}</color>";

        private static string FormatDate(long utcTicks)
        {
            if (utcTicks <= 0) return "?";
            try { return new DateTime(utcTicks, DateTimeKind.Utc).ToLocalTime().ToString("yyyy-MM-dd"); }
            catch { return "?"; }
        }

        private void DrawSeasonLines(Rect body, List<(string text, bool header)> lines)
        {
            Rect inner = body.ContractedBy(8f);
            const float lineH = 24f;
            float viewH = Mathf.Max(inner.height, lines.Count * lineH + 6f);
            Rect viewRect = new Rect(0f, 0f, inner.width - DialogLayout.ScrollbarReserveWidth, viewH);
            Widgets.BeginScrollView(inner, ref _scroll, viewRect);
            float ly = 0f;
            foreach ((string text, bool header) in lines)
            {
                if (header) DialogLayout.LabelTrunc(new Rect(0f, ly, viewRect.width, lineH), $"<color=#f0c04f><b>{text}</b></color>");
                else if (!string.IsNullOrEmpty(text)) DialogLayout.LabelTrunc(new Rect(10f, ly, viewRect.width - 10f, lineH), text);
                ly += lineH;
            }
            Widgets.EndScrollView();
        }

        private static Col<PlayerLeaderboardEntry> Col(string h, float f, Func<PlayerLeaderboardEntry, string> cell)
            => new Col<PlayerLeaderboardEntry>(h, f, cell);

        private static string Fmt(string user)   => LinkedAccountsCache.Format(user);
        // Colony and colonist names are typed by players, so a cell never obeys markup in them.
        private static string Str(string s)      => string.IsNullOrEmpty(s) ? StandingsTable.Dash : KmhDisplayText.Inert(s);
        private static string Silver(long v)     => SilverFmt.Format(v);
        private static string Days(int d)        => d > 0 ? KmhText.Get("KMH_PP_DaysShort", "{0}d", d) : StandingsTable.Dash;
        private static string Hours(int h)       => h > 0 ? $"{h}h" : StandingsTable.Dash;
        private static string Span(long secs)    => secs > 0 ? KmhAgo.Span(secs) : StandingsTable.Dash;
        // A colony figure from a game that has not reported yet is unknown, not zero.
        internal static string R(PlayerLeaderboardEntry e, string cell) => e != null && e.LastReportUtcTicks > 0 ? cell : NotReported;
        // Said once per row, in the colony cell; the other colony cells stay a dim dash rather than a zero.
        internal const string NotReported = "<color=#5b6270>—</color>";
        internal static string NotReportedLong => "<color=#6f7785>" + KmhText.Get("KMH_St_NotReportedYet", "not reported yet") + "</color>";

        private static string RepCell(string user)
        {
            int score = StandingsData.Rep(user);
            switch (StandingsData.RepTier(user))
            {
                case "Trusted":    return $"<color=#4cc38a>{score}</color>";
                case "Unreliable": return $"<color=#f07167>{score}</color>";
                default:           return $"<color=#A9AFB9>{score}</color>";
            }
        }

        private static string TierLabel(string tier)
        {
            switch (tier)
            {
                case "Trusted":    return "<color=#4cc38a>" + KmhText.Get("KMH_St_TierTrusted", "Trusted") + "</color>";
                case "Unreliable": return "<color=#f07167>" + KmhText.Get("KMH_St_TierUnreliable", "Unreliable") + "</color>";
                default:           return "<color=#A9AFB9>" + KmhText.Get("KMH_St_TierNeutral", "Neutral") + "</color>";
            }
        }
    }

    // Pure sizing, apart from the window, so the rule can be checked without a game.
    internal static class ProfileDrawer
    {
        internal static float Width(float contentW) => Math.Min(460f, Math.Max(360f, contentW * 0.32f));

        // The board keeps at least 720px beside the drawer, or its columns crush; 1280x720 gets the docked window.
        internal static bool Fits(float contentW) => contentW - Width(contentW) - 10f >= 720f;
    }
}
