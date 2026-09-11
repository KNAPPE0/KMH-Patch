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
    // A left section list + a right ranked board, every board a view over snapshots KMH already pushes.
    public class Dialog_KMHStandings : Window_KMHBase
    {
        public override Vector2 InitialSize => KMHPatch.UI.DialogLayout.FitToScreen(1280f, 680f);

        private enum Section { Player, Guild, Members, Colony, Colonist, Trade, Contract, Battle, Site, Activity, Reputation, Season }

        private static readonly (Section sec, string label)[] Nav =
        {
            (Section.Player,     "Player Standings"),
            (Section.Guild,      "Guild Standings"),
            (Section.Members,    "Member Contributions"),
            (Section.Colony,     "Colony Records"),
            (Section.Colonist,   "Colonist Records"),
            (Section.Trade,      "Trade Records"),
            (Section.Contract,   "Contract Records"),
            (Section.Battle,     "Battle Records"),
            (Section.Site,       "Site Records"),
            (Section.Activity,   "Activity Records"),
            (Section.Reputation, "Reputation Records"),
            (Section.Season,     "Season Archive"),
        };

        private Section _section = Section.Player;
        private readonly int[] _tab = new int[Enum.GetValues(typeof(Section)).Length];
        private Vector2 _scroll;
        private float   _refresh = DialogLayout.AutoRefreshSeconds;

        // Rebuilt only when the section/tab or snapshot changes - sorting a big roster every frame is not free.
        private List<PlayerLeaderboardEntry> _pView; private string _pKey = "";
        private List<GuildAgg>               _gView; private string _gKey = "";
        private List<ColonistEntry>          _cView; private string _cKey = "";

        public Dialog_KMHStandings()
        {
            doCloseX = true; absorbInputAroundWindow = true; forcePause = false; draggable = true; resizeable = true;
            RequestAll();
        }

        private static void RequestAll()
        {
            // Our own colony first, or the caller's row stays blank until the timer-driven report fires.
            GameComponent_KMHColonyReporter.SendNow();
            PlayerStatsHandler.RequestSnapshot();
            PlayerStatsHandler.RequestColonistRoster();
            GuildHandler.RequestLeaderboard();
            ReputationCache.RequestSnapshot();
            SeasonHandler.RequestSnapshot();
            ChatRosterCache.RequestIfStale();
        }

        public override void WindowUpdate()
        {
            base.WindowUpdate();
            _refresh -= Time.unscaledDeltaTime;
            if (_refresh <= 0f) { _refresh = DialogLayout.AutoRefreshSeconds; RequestAll(); }
        }

        protected override void DrawContents(Rect rect)
        {
            float y = DialogLayout.DrawTitle(rect, "Server Standings");
            // Said once here rather than on twenty column headers.
            DialogLayout.LabelTrunc(new Rect(0f, y, rect.width, DialogLayout.TextRowH),
                $"<color=grey>Season {SeasonArchiveCache.Snapshot?.CurrentSeason ?? 1} · totals are cumulative on this server; each season's leaders are kept in the Season Archive.</color>");
            y += DialogLayout.TextRowH;
            DialogLayout.DrawSectionDivider(rect, ref y);

            // Proportional, or on a clamped window the nav eats a quarter of the table it exists to navigate.
            float navW = Mathf.Clamp(rect.width * 0.22f, 130f, 196f);
            Rect nav = new Rect(0f, y, navW, DialogLayout.BodyHeight(rect, y));
            Widgets.DrawMenuSection(nav);
            DrawNav(nav);

            Rect content = new Rect(navW + 10f, y, rect.width - navW - 10f, DialogLayout.BodyHeight(rect, y));
            DrawSection(content);

            if (DialogLayout.DrawCloseButton(rect)) Close();
        }

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
                bool active = _section == Nav[i].sec;
                if (active) Widgets.DrawBoxSolid(r, new Color(0.30f, 0.45f, 0.65f, 0.55f));
                Widgets.DrawHighlightIfMouseover(r);
                Color old = GUI.color; if (active) GUI.color = new Color(0.8f, 0.9f, 1f);
                DialogLayout.LabelTrunc(new Rect(r.x + 8f, r.y, r.width - 12f, h), Nav[i].label);
                GUI.color = old;
                if (Widgets.ButtonInvisible(r) && _section != Nav[i].sec) { _section = Nav[i].sec; _scroll = Vector2.zero; }
            }
            Widgets.EndScrollView();
        }

        private void DrawSection(Rect c)
        {
            switch (_section)
            {
                case Section.Guild:      DrawGuildBoard(c); break;
                case Section.Colonist:   DrawColonistBoard(c); break;
                case Section.Reputation: DrawReputationBoard(c); break;
                case Section.Season:     DrawSeason(c); break;
                default:                 DrawPlayerSection(c); break;
            }
        }

        private sealed class Tab
        {
            public string Label; public Comparison<PlayerLeaderboardEntry> Sort; public int Accent;
            public Tab(string l, Comparison<PlayerLeaderboardEntry> s, int accent) { Label = l; Sort = s; Accent = accent; }
        }

        private void DrawPlayerSection(Rect c)
        {
            (Col<PlayerLeaderboardEntry>[] cols, Tab[] tabs) = PlayerBoardDef(_section);

            int tab = _tab[(int)_section];
            string[] labels = new string[tabs.Length];
            for (int i = 0; i < tabs.Length; i++) labels[i] = tabs[i].Label;
            Rect tabRow = new Rect(c.x, c.y, c.width, StandingsTable.TabsH(labels, c.width));
            if (StandingsTable.Tabs(tabRow, labels, ref tab)) { _tab[(int)_section] = tab; _scroll = Vector2.zero; }
            tab = Mathf.Clamp(tab, 0, tabs.Length - 1);

            float hy = c.y + StandingsTable.TabsH(labels, c.width) + 6f;
            StandingsTable.Header(new Rect(c.x, hy, c.width, 22f), cols, tabs[tab].Accent, hasInfo: true);

            Rect list = new Rect(c.x, hy + 24f, c.width, c.height - (hy + 24f - c.y));
            Widgets.DrawMenuSection(list);

            string key = $"{(int)_section}:{tab}:{PlayerStatsCache.LastUpdatedUtc.Ticks}:{ReputationCache.Version}";
            if (_pView == null || _pKey != key)
            {
                _pView = new List<PlayerLeaderboardEntry>(StandingsData.Players());
                _pView.Sort(tabs[tab].Sort);
                _pKey = key;
            }
            List<PlayerLeaderboardEntry> rows = _pView;
            string me = KmhSession.Me;
            StandingsTable.Draw(list, rows, cols, ref _scroll,
                e => KmhSession.Same(e.Username, me),
                e => Find.WindowStack.Add(new Dialog_KMHPlayerProfile(e.Username)));
        }

        // Column + tab definitions per player-row section. Columns are fixed per board; tabs re-sort + accent.
        private static (Col<PlayerLeaderboardEntry>[], Tab[]) PlayerBoardDef(Section s)
        {
            Col<PlayerLeaderboardEntry> Name   = new Col<PlayerLeaderboardEntry>("Player", 0.00f, e => Fmt(e.Username));
            Col<PlayerLeaderboardEntry> Colony = new Col<PlayerLeaderboardEntry>("Colony", 0.17f, e => Str(e.ColonyName));
            Col<PlayerLeaderboardEntry> Guild  = new Col<PlayerLeaderboardEntry>("Guild",  0.30f, e => Str(e.GuildName));

            switch (s)
            {
                case Section.Members:
                    return (new[]
                    {
                        Name, Guild,
                        Col("Contribution", 0.42f, e => e.EconomyScore.ToString("N0")),
                        Col("Donated",      0.56f, e => Silver(e.SilverDonated)),
                        Col("Contracts",    0.68f, e => e.QuestsCompleted.ToString()),
                        Col("Site Work",    0.78f, e => e.WorkerXp.ToString("N0")),
                        Col("Trade",        0.88f, e => Silver(e.SalesEarned + e.PurchasesSpent)),
                    }, new[]
                    {
                        new Tab("Overall",   (a, b) => b.EconomyScore.CompareTo(a.EconomyScore), 2),
                        new Tab("Donations", (a, b) => b.SilverDonated.CompareTo(a.SilverDonated), 3),
                        new Tab("Contracts", (a, b) => b.QuestsCompleted.CompareTo(a.QuestsCompleted), 4),
                        new Tab("Site Work", (a, b) => b.WorkerXp.CompareTo(a.WorkerXp), 5),
                        new Tab("Trade",     (a, b) => (b.SalesEarned + b.PurchasesSpent).CompareTo(a.SalesEarned + a.PurchasesSpent), 6),
                    });

                case Section.Colony:
                    return (new[]
                    {
                        new Col<PlayerLeaderboardEntry>("Colony", 0.00f, e => Str(e.ColonyName)),
                        new Col<PlayerLeaderboardEntry>("Player", 0.18f, e => Fmt(e.Username)),
                        // Settlements only: this board's other columns are all on-map, and Player Standings ranks the total.
                        Col("Settlements", 0.34f, e => Silver(e.Wealth)),
                        Col("Age",     0.47f, e => Days(e.ColonyAgeDays)),
                        Col("Pop",     0.56f, e => e.Population.ToString()),
                        Col("Dev",     0.65f, e => e.DevelopmentScore.ToString("N0")),
                        Col("Defense", 0.76f, e => e.DefenseScore.ToString("N0")),
                        Col("Raids",   0.88f, e => e.RaidsSurvived.ToString()),
                    }, new[]
                    {
                        new Tab("Richest",        (a, b) => b.Wealth.CompareTo(a.Wealth), 2),
                        new Tab("Oldest",         (a, b) => b.ColonyAgeDays.CompareTo(a.ColonyAgeDays), 3),
                        new Tab("Largest",        (a, b) => b.Population.CompareTo(a.Population), 4),
                        new Tab("Most Developed", (a, b) => b.DevelopmentScore.CompareTo(a.DevelopmentScore), 5),
                        new Tab("Best Defended",  (a, b) => b.DefenseScore.CompareTo(a.DefenseScore), 6),
                    });

                case Section.Trade:
                    return (new[]
                    {
                        Name, Colony,
                        Col("Earned",  0.40f, e => Silver(e.SalesEarned)),
                        Col("Spent",   0.51f, e => Silver(e.PurchasesSpent)),
                        Col("Sold",    0.62f, e => e.ItemsSold.ToString("N0")),
                        Col("Bought",  0.71f, e => e.ItemsBought.ToString("N0")),
                        Col("Largest", 0.80f, e => Silver(e.LargestSale)),
                        Col("Volume",  0.90f, e => Silver(e.SalesEarned + e.PurchasesSpent)),
                    }, new[]
                    {
                        new Tab("Top Sellers",  (a, b) => b.SalesEarned.CompareTo(a.SalesEarned), 2),
                        new Tab("Top Buyers",   (a, b) => b.PurchasesSpent.CompareTo(a.PurchasesSpent), 3),
                        new Tab("Most Sold",    (a, b) => b.ItemsSold.CompareTo(a.ItemsSold), 4),
                        new Tab("Largest Sale", (a, b) => b.LargestSale.CompareTo(a.LargestSale), 6),
                        new Tab("Volume",       (a, b) => (b.SalesEarned + b.PurchasesSpent).CompareTo(a.SalesEarned + a.PurchasesSpent), 7),
                    });

                case Section.Contract:
                    return (new[]
                    {
                        Name, Colony,
                        Col("Completed",  0.40f, e => e.QuestsCompleted.ToString()),
                        Col("Bounties",   0.51f, e => e.ContractsBounty.ToString()),
                        Col("Deliveries", 0.60f, e => e.ContractsDeliver.ToString()),
                        Col("Hunts",      0.71f, e => e.ContractsHunt.ToString()),
                        Col("Failed",     0.80f, e => e.ContractsFailed.ToString()),
                        Col("Streak",     0.89f, e => e.ContractStreak.ToString()),
                    }, new[]
                    {
                        new Tab("Top Contractors", (a, b) => b.QuestsCompleted.CompareTo(a.QuestsCompleted), 2),
                        new Tab("Bounties",        (a, b) => b.ContractsBounty.CompareTo(a.ContractsBounty), 3),
                        new Tab("Deliveries",      (a, b) => b.ContractsDeliver.CompareTo(a.ContractsDeliver), 4),
                        new Tab("Hunts",           (a, b) => b.ContractsHunt.CompareTo(a.ContractsHunt), 5),
                        new Tab("Streaks",         (a, b) => b.ContractStreak.CompareTo(a.ContractStreak), 7),
                    });

                case Section.Battle:
                    return (new[]
                    {
                        Name, Colony,
                        Col("Kills",  0.38f, e => e.Kills.ToString("N0")),
                        Col("Human",  0.50f, e => e.KillsHumanlike.ToString("N0")),
                        Col("Mech",   0.60f, e => e.KillsMechanoid.ToString("N0")),
                        Col("Animal", 0.70f, e => e.KillsAnimal.ToString("N0")),
                        Col("Raids",  0.80f, e => e.RaidsSurvived.ToString()),
                        Col("Losses", 0.90f, e => e.PawnsLost.ToString()),
                    }, new[]
                    {
                        new Tab("Deadliest", (a, b) => b.Kills.CompareTo(a.Kills), 2),
                        new Tab("Mechanoid", (a, b) => b.KillsMechanoid.CompareTo(a.KillsMechanoid), 4),
                        new Tab("Humanlike", (a, b) => b.KillsHumanlike.CompareTo(a.KillsHumanlike), 3),
                        new Tab("Animal",    (a, b) => b.KillsAnimal.CompareTo(a.KillsAnimal), 5),
                        new Tab("Raids",     (a, b) => b.RaidsSurvived.CompareTo(a.RaidsSurvived), 6),
                        new Tab("Survival",  (a, b) => a.PawnsLost.CompareTo(b.PawnsLost), 7),
                    });

                case Section.Activity:
                    return (new[]
                    {
                        Name, Colony, Guild,
                        Col("Status",    0.42f, e => !ChatRosterCache.Known ? StandingsTable.Dash
                                                     : ChatRosterCache.IsOnline(e.Username) ? "Online" : "<color=grey>Offline</color>"),
                        Col("Active",    0.53f, e => Span(e.ActiveSeconds)),
                        Col("Connected", 0.64f, e => Span(e.ConnectedSeconds)),
                        Col("Focus",     0.76f, e => FocusCell(e)),
                        Col("Last Seen", 0.86f, e => Str(KmhAgo.Since(e.LastSeenUtcTicks))),
                    }, new[]
                    {
                        // Named tiebreak throughout: List.Sort is unstable and most rows tie on zero, so it reshuffles.
                        new Tab("Most Active",  (a, b) => Then(b.ActiveSeconds.CompareTo(a.ActiveSeconds), a, b), 4),
                        new Tab("Connected",    (a, b) => Then(b.ConnectedSeconds.CompareTo(a.ConnectedSeconds), a, b), 5),
                        new Tab("Focus",        (a, b) => Then(Focus(b).CompareTo(Focus(a)), a, b), 6),
                        new Tab("Recently Seen",(a, b) => Then(b.LastSeenUtcTicks.CompareTo(a.LastSeenUtcTicks), a, b), 7),
                    });

                case Section.Site:
                    return (new[]
                    {
                        Name, Colony,
                        Col("Sites",      0.40f, e => e.SitesOwned.ToString()),
                        Col("Produced",   0.52f, e => Silver(e.SiteSilverProduced)),
                        Col("Worker XP",  0.66f, e => e.WorkerXp.ToString("N0")),
                        Col("Outposts",   0.80f, e => e.OutpostsHeld > 0 || e.FrontierCaptures > 0
                                                       ? $"{e.OutpostsHeld} ({e.FrontierCaptures} taken)" : StandingsTable.Dash),
                    }, new[]
                    {
                        new Tab("Top Owners",     (a, b) => b.SitesOwned.CompareTo(a.SitesOwned), 2),
                        new Tab("Production",     (a, b) => b.SiteSilverProduced.CompareTo(a.SiteSilverProduced), 3),
                        new Tab("Worker XP",      (a, b) => b.WorkerXp.CompareTo(a.WorkerXp), 4),
                        new Tab("Frontier",       (a, b) => b.FrontierCaptures.CompareTo(a.FrontierCaptures), 5),
                    });

                default: // Player Standings
                    return (new[]
                    {
                        Name, Colony, Guild,
                        Col("Age",       0.40f, e => Days(e.ColonyAgeDays)),
                        Col("Time",      0.48f, e => Hours(e.TimePlayedHours)),
                        Col("Wealth",    0.56f, e => Silver(e.TotalWealth)),
                        Col("Kills",     0.67f, e => e.Kills.ToString("N0")),
                        Col("Contracts", 0.75f, e => e.QuestsCompleted.ToString()),
                        Col("Rep",       0.83f, e => RepCell(e.Username)),
                        Col("Top",       0.90f, e => Str(e.TopColonistName)),
                    }, new[]
                    {
                        new Tab("Overall",    (a, b) => Overall(b).CompareTo(Overall(a)), -1),
                        new Tab("Wealth",     (a, b) => b.TotalWealth.CompareTo(a.TotalWealth), 5),
                        new Tab("Combat",     (a, b) => b.Kills.CompareTo(a.Kills), 6),
                        new Tab("Contracts",  (a, b) => b.QuestsCompleted.CompareTo(a.QuestsCompleted), 7),
                        new Tab("Reputation", (a, b) => StandingsData.Rep(b.Username).CompareTo(StandingsData.Rep(a.Username)), 8),
                        new Tab("Time",       (a, b) => b.TimePlayedHours.CompareTo(a.TimePlayedHours), 4),
                        new Tab("Colony Age", (a, b) => b.ColonyAgeDays.CompareTo(a.ColonyAgeDays), 3),
                        new Tab("Top Colonist",(a, b) => b.TopColonistKills.CompareTo(a.TopColonistKills), 9),
                    });
            }
        }

        // Ties break on the name, which is the only field on a row that is unique and never changes.
        internal static int Then(int primary, PlayerLeaderboardEntry a, PlayerLeaderboardEntry b)
            => primary != 0 ? primary : string.Compare(a?.Username ?? "", b?.Username ?? "", StringComparison.OrdinalIgnoreCase);

        private static int Focus(PlayerLeaderboardEntry e) => StandingsData.Focus(e.ActiveSeconds, e.ConnectedSeconds);

        private static string FocusCell(PlayerLeaderboardEntry e)
        {
            int f = Focus(e);
            return f < 0 ? StandingsTable.Dash : f + "%";
        }

        // Rough "overall" rank: a blend so the default Player Standings tab rewards all-round play.
        private static long Overall(PlayerLeaderboardEntry e)
            => e.TotalWealth / 1000 + e.Kills * 50 + (long)e.QuestsCompleted * 100 + StandingsData.Rep(e.Username) * 10 + e.TimePlayedHours;

        private void DrawGuildBoard(Rect c)
        {
            string[] labels = { "Overall", "Treasury", "Members", "Combat", "Contracts", "Sites", "Trade", "Reputation" };
            int tab = _tab[(int)Section.Guild];
            if (StandingsTable.Tabs(new Rect(c.x, c.y, c.width, StandingsTable.TabsH(labels, c.width)), labels, ref tab)) { _tab[(int)Section.Guild] = tab; _scroll = Vector2.zero; }
            tab = Mathf.Clamp(tab, 0, labels.Length - 1);

            Col<GuildAgg>[] cols =
            {
                new Col<GuildAgg>("Guild", 0.00f, g => g.Name),
                new Col<GuildAgg>("Members",   0.26f, g => g.Members.ToString()),
                new Col<GuildAgg>("Treasury",  0.38f, g => SilverFmt.Format(g.Treasury)),
                new Col<GuildAgg>("Wealth",    0.50f, g => SilverFmt.Format(g.Wealth)),
                new Col<GuildAgg>("Kills",     0.62f, g => g.Kills.ToString("N0")),
                new Col<GuildAgg>("Contracts", 0.72f, g => g.Contracts.ToString()),
                new Col<GuildAgg>("Sites",     0.81f, g => g.Sites.ToString()),
                new Col<GuildAgg>("Trade",     0.89f, g => SilverFmt.Format(g.TradeVolume)),
            };
            int accent = new[] { -1, 2, 1, 4, 5, 6, 7, -1 }[tab];

            float hy = c.y + StandingsTable.TabsH(labels, c.width) + 6f;
            StandingsTable.Header(new Rect(c.x, hy, c.width, 22f), cols, accent, hasInfo: false);
            Rect list = new Rect(c.x, hy + 24f, c.width, c.height - (hy + 24f - c.y));
            Widgets.DrawMenuSection(list);

            Comparison<GuildAgg> sort = tab switch
            {
                1 => (a, b) => b.Treasury.CompareTo(a.Treasury),
                2 => (a, b) => b.Members.CompareTo(a.Members),
                3 => (a, b) => b.Kills.CompareTo(a.Kills),
                4 => (a, b) => b.Contracts.CompareTo(a.Contracts),
                5 => (a, b) => b.Sites.CompareTo(a.Sites),
                6 => (a, b) => b.TradeVolume.CompareTo(a.TradeVolume),
                7 => (a, b) => b.RepAvg.CompareTo(a.RepAvg),
                _ => (a, b) => (b.Wealth + b.Treasury).CompareTo(a.Wealth + a.Treasury),
            };
            // Reputation feeds the per-guild average, so a rep update alone must still rebuild the sort.
            string gkey = $"G:{tab}:{PlayerStatsCache.LastUpdatedUtc.Ticks}:{GuildLeaderboardCache.LastUpdatedUtc.Ticks}:{ReputationCache.Version}";
            if (_gView == null || _gKey != gkey)
            {
                _gView = StandingsData.GuildAggs();
                _gView.Sort(sort);
                _gKey = gkey;
            }
            List<GuildAgg> rows = _gView;
            string myGuild = GuildCache.Guild?.Name ?? "";
            StandingsTable.Draw(list, rows, cols, ref _scroll,
                g => !string.IsNullOrEmpty(myGuild) && string.Equals(g.Name, myGuild, StringComparison.OrdinalIgnoreCase), null);
        }

        private List<ReputationEntryDto> _rView; private string _rKey = "";

        private void DrawReputationBoard(Rect c)
        {
            bool inGuild = GuildCache.InGuild && !string.IsNullOrEmpty(GuildCache.Guild?.Name);
            string[] labels = inGuild ? new[] { "Most Trusted", "Lowest", "My Guild" }
                                      : new[] { "Most Trusted", "Lowest" };
            int tab = _tab[(int)Section.Reputation];
            if (StandingsTable.Tabs(new Rect(c.x, c.y, c.width, StandingsTable.TabsH(labels, c.width)), labels, ref tab)) { _tab[(int)Section.Reputation] = tab; _scroll = Vector2.zero; }
            tab = Mathf.Clamp(tab, 0, labels.Length - 1);

            Col<ReputationEntryDto>[] cols =
            {
                new Col<ReputationEntryDto>("Player", 0.00f, e => Fmt(e.Username)),
                new Col<ReputationEntryDto>("Reputation", 0.45f, e => e.Score.ToString("N0")),
                new Col<ReputationEntryDto>("Status",     0.70f, e => TierLabel(e.Tier)),
            };
            float hy = c.y + StandingsTable.TabsH(labels, c.width) + 6f;
            StandingsTable.Header(new Rect(c.x, hy, c.width, 22f), cols, 1, hasInfo: false);
            Rect list = new Rect(c.x, hy + 24f, c.width, c.height - (hy + 24f - c.y));
            Widgets.DrawMenuSection(list);

            string key = $"{tab}:{ReputationCache.Version}:{PlayerStatsCache.LastUpdatedUtc.Ticks}";
            if (_rView == null || _rKey != key)
            {
                _rView = ReputationCache.Leaderboard();
                if (tab == 1) _rView.Reverse();
                else if (tab == 2) _rView = OnlyGuild(_rView, GuildCache.Guild.Name);
                _rKey = key;
            }

            string me = KmhSession.Me;
            StandingsTable.Draw(list, _rView, cols, ref _scroll,
                e => KmhSession.Same(e.Username, me), null);
        }

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

        // An all-rounder, so the default tab is not just the Deadliest one beside it; a kill is worth a skill point x20.
        private static long ColonistScore(ColonistEntry e)
            => e.Kills * 20L + e.SkShooting + e.SkMelee + e.SkMedicine + e.SkCrafting + e.SkConstruction;

        // Rows are ColonistEntry from the flattened roster, not one row per player.
        private void DrawColonistBoard(Rect c)
        {
            string[] labels = { "Top Colonists", "Deadliest", "Best Shooter", "Best Melee", "Best Doctor", "Best Crafter", "Best Builder", "Oldest", "Longest Serving" };
            int tab = _tab[(int)Section.Colonist];
            if (StandingsTable.Tabs(new Rect(c.x, c.y, c.width, StandingsTable.TabsH(labels, c.width)), labels, ref tab)) { _tab[(int)Section.Colonist] = tab; _scroll = Vector2.zero; }
            tab = Mathf.Clamp(tab, 0, labels.Length - 1);

            Col<ColonistEntry>[] cols =
            {
                new Col<ColonistEntry>("Colonist", 0.00f, e => Str(e.Name)),
                new Col<ColonistEntry>("Player",   0.18f, e => Fmt(e.Owner)),
                new Col<ColonistEntry>("Kills",    0.33f, e => e.Kills.ToString()),
                new Col<ColonistEntry>("Shoot",    0.43f, e => e.SkShooting.ToString()),
                new Col<ColonistEntry>("Melee",    0.52f, e => e.SkMelee.ToString()),
                new Col<ColonistEntry>("Doctor",   0.61f, e => e.SkMedicine.ToString()),
                new Col<ColonistEntry>("Craft",    0.70f, e => e.SkCrafting.ToString()),
                new Col<ColonistEntry>("Build",    0.79f, e => e.SkConstruction.ToString()),
                new Col<ColonistEntry>("Age/Days", 0.88f, e => $"{e.Age}y · {e.Days}d"),
            };
            int accent = new[] { -1, 2, 3, 4, 5, 6, 7, 8, 8 }[tab];

            float hy = c.y + StandingsTable.TabsH(labels, c.width) + 6f;
            StandingsTable.Header(new Rect(c.x, hy, c.width, 22f), cols, accent, hasInfo: true);
            Rect list = new Rect(c.x, hy + 24f, c.width, c.height - (hy + 24f - c.y));
            Widgets.DrawMenuSection(list);

            Comparison<ColonistEntry> sort = tab switch
            {
                1 => (a, b) => b.Kills.CompareTo(a.Kills),
                2 => (a, b) => b.SkShooting.CompareTo(a.SkShooting),
                3 => (a, b) => b.SkMelee.CompareTo(a.SkMelee),
                4 => (a, b) => b.SkMedicine.CompareTo(a.SkMedicine),
                5 => (a, b) => b.SkCrafting.CompareTo(a.SkCrafting),
                6 => (a, b) => b.SkConstruction.CompareTo(a.SkConstruction),
                7 => (a, b) => b.Age.CompareTo(a.Age),
                8 => (a, b) => b.Days.CompareTo(a.Days),
                _ => (a, b) => ColonistScore(b).CompareTo(ColonistScore(a)),
            };
            string ckey = $"C:{tab}:{ColonistRosterCache.LastUpdatedUtc.Ticks}";
            if (_cView == null || _cKey != ckey)
            {
                _cView = new List<ColonistEntry>(ColonistRosterCache.Colonists);
                _cView.Sort(sort);
                _cKey = ckey;
            }
            List<ColonistEntry> rows = _cView;
            string me = KmhSession.Me;
            StandingsTable.Draw(list, rows, cols, ref _scroll,
                e => KmhSession.Same(e.Owner, me),
                e => OpenColonistInfo(e, me));
        }

        // A local pawn opens RimWorld's own info card; anyone else's opens the KMH colonist profile.
        private static void OpenColonistInfo(ColonistEntry e, string me)
        {
            if (e == null) return;
            if (KmhSession.Same(e.Owner, me))
            {
                Pawn p = FindLocalColonist(e.Name);
                if (p != null) { Find.WindowStack.Add(new Dialog_InfoCard(p)); return; }
            }
            Find.WindowStack.Add(new Dialog_KMHColonistProfile(e.Owner));
        }

        private static Pawn FindLocalColonist(string shortName)
        {
            if (string.IsNullOrEmpty(shortName)) return null;
            try
            {
                foreach (Map map in Find.Maps)
                    foreach (Pawn p in map.mapPawns.FreeColonistsSpawned)
                        if (string.Equals(p.Name?.ToStringShort, shortName, StringComparison.OrdinalIgnoreCase)) return p;
                foreach (RimWorld.Planet.Caravan car in Find.WorldObjects.Caravans)
                    if (car.IsPlayerControlled)
                        foreach (Pawn p in car.PawnsListForReading)
                            if (p.IsColonist && string.Equals(p.Name?.ToStringShort, shortName, StringComparison.OrdinalIgnoreCase)) return p;
            }
            catch { }
            return null;
        }

        private void DrawSeason(Rect c)
        {
            string[] labels = { "Current Season", "Server Records", "Past Seasons" };
            int tab = _tab[(int)Section.Season];
            if (StandingsTable.Tabs(new Rect(c.x, c.y, c.width, StandingsTable.TabsH(labels, c.width)), labels, ref tab)) { _tab[(int)Section.Season] = tab; _scroll = Vector2.zero; }
            tab = Mathf.Clamp(tab, 0, labels.Length - 1);

            float bodyY = StandingsTable.TabsH(labels, c.width) + 8f;
            Rect body = new Rect(c.x, c.y + bodyY, c.width, Mathf.Max(0f, c.height - bodyY));
            Widgets.DrawMenuSection(body);

            SeasonArchiveSnapshot snap = SeasonArchiveCache.Snapshot;
            if (snap == null)
            {
                DialogLayout.LabelTrunc(new Rect(body.x + 8f, body.y + 8f, body.width - 16f, 22f), "<color=grey>Loading season archive…</color>");
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
                H($"Season {snap.CurrentSeason} — in progress");
                if (current.Count == 0) R("<color=grey>No leaders yet this season.</color>");
                else foreach (SeasonRecordDto r in current) R(RecordLine(r));
            }
            else if (tab == 1)
            {
                H("All-time server records");
                if (records.Count == 0) R("<color=grey>No records yet - an admin rolls the season with 'kmh season roll'.</color>");
                else foreach (SeasonRecordDto r in records) R(RecordLine(r) + (r.Season > 0 ? $"   <color=grey>(season {r.Season})</color>" : ""));
            }
            else
            {
                if (past.Count == 0) R("<color=grey>No past seasons yet. Each season is archived when an admin runs 'kmh season roll'.</color>");
                else foreach (SeasonArchiveDto s in past)
                {
                    H($"Season {s.Season} — ended {FormatDate(s.EndedUtcTicks)}");
                    foreach (SeasonRecordDto r in s.Records ?? new List<SeasonRecordDto>()) R(RecordLine(r));
                    lines.Add(("", false));
                }
            }
            DrawSeasonLines(body, lines);
        }

        private static string RecordLine(SeasonRecordDto r)
            => $"<b>{r.Category}</b>:  {LinkedAccountsCache.Format(r.Holder)}  <color=grey>· {r.Detail}</color>";

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
                if (header) DialogLayout.LabelTrunc(new Rect(0f, ly, viewRect.width, lineH), $"<color=#e0b94a><b>{text}</b></color>");
                else if (!string.IsNullOrEmpty(text)) DialogLayout.LabelTrunc(new Rect(10f, ly, viewRect.width - 10f, lineH), text);
                ly += lineH;
            }
            Widgets.EndScrollView();
        }

        private static Col<PlayerLeaderboardEntry> Col(string h, float f, Func<PlayerLeaderboardEntry, string> cell)
            => new Col<PlayerLeaderboardEntry>(h, f, cell);

        private static string Fmt(string user)   => LinkedAccountsCache.Format(user);
        private static string Str(string s)      => string.IsNullOrEmpty(s) ? StandingsTable.Dash : s;
        private static string Silver(long v)     => SilverFmt.Format(v);
        private static string Days(int d)        => d > 0 ? $"{d}d" : StandingsTable.Dash;
        private static string Hours(int h)       => h > 0 ? $"{h}h" : StandingsTable.Dash;
        private static string Span(long secs)    => secs > 0 ? KmhAgo.Span(secs) : StandingsTable.Dash;

        private static string RepCell(string user)
        {
            int score = StandingsData.Rep(user);
            switch (StandingsData.RepTier(user))
            {
                case "Trusted":    return $"<color=#80ff80>{score}</color>";
                case "Unreliable": return $"<color=#ff8080>{score}</color>";
                default:           return $"<color=grey>{score}</color>";
            }
        }

        private static string TierLabel(string tier)
        {
            switch (tier)
            {
                case "Trusted":    return "<color=#80ff80>Trusted</color>";
                case "Unreliable": return "<color=#ff8080>Unreliable</color>";
                default:           return "<color=grey>Neutral</color>";
            }
        }
    }
}
