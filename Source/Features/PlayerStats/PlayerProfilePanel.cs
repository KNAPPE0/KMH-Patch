using System;
using System.Collections.Generic;
using KMHPatch.Features.Chat;
using KMHPatch.Features.Leaderboards;
using KMHPatch.Features.Leaderboards.Dto;
using KMHPatch.Features.LinkedAccounts;
using KMHPatch.Features.PlayerStats.Dto;
using KMHPatch.Features.Reputation;
using KMHPatch.Features.Standings;
using KMHPatch.UI;
using UnityEngine;
using Verse;

namespace KMHPatch.Features.PlayerStats
{
    internal sealed class PlayerProfilePanel
    {
        private enum Tab { Overview, Colony, Colonists, Combat, Contracts, Trade, GuildWork, History }

        private static readonly Tab[] Tabs =
            { Tab.Overview, Tab.Colony, Tab.Colonists, Tab.Combat, Tab.Contracts, Tab.Trade, Tab.GuildWork, Tab.History };

        private static string TabLabel(Tab t)
        {
            switch (t)
            {
                case Tab.Colony:    return KmhText.Get("KMH_PP_TabColony", "Colony");
                case Tab.Colonists: return KmhText.Get("KMH_PP_TabColonists", "Colonists");
                case Tab.Combat:    return KmhText.Get("KMH_PP_TabCombat", "Combat");
                case Tab.Contracts: return KmhText.Get("KMH_PP_TabContracts", "Contracts");
                case Tab.Trade:     return KmhText.Get("KMH_PP_TabTrade", "Trade");
                case Tab.GuildWork: return KmhText.Get("KMH_PP_TabGuildWork", "Guild Work");
                case Tab.History:   return KmhText.Get("KMH_PP_TabHistory", "History");
                default:            return KmhText.Get("KMH_PP_TabOverview", "Overview");
            }
        }

        // Not cached: a language picked in Options must show without a restart.
        private static string[] TabLabels
        {
            get
            {
                string[] a = new string[Tabs.Length];
                for (int i = 0; i < Tabs.Length; i++) a[i] = TabLabel(Tabs[i]);
                return a;
            }
        }

        private const string Dash = "<color=#A9AFB9>—</color>";
        private const float StaleSeconds = 120f;

        public string Username { get; private set; } = "";
        private Tab _tab = Tab.Overview;
        private Vector2 _scroll, _colScroll;
        private string _focusColonist;
        private bool _scrollToFocus;
        private Dialog_KMHColonistProfile _childProfile;

        private List<(string title, int rank, int total, bool reported)> _ranks; private string _ranksKey = "";

        public PlayerProfilePanel(string username, string focusColonist = null) => Focus(username, focusColonist);

        // Retargets in place, so clicking down a board never stacks windows.
        public void Focus(string username, string focusColonist = null)
        {
            bool same = KmhSession.Same(Username, username ?? "");
            Username = username ?? "";
            _focusColonist = string.IsNullOrEmpty(focusColonist) ? null : focusColonist;
            if (_focusColonist != null) { _tab = Tab.Colonists; _scrollToFocus = true; }
            else if (!same) _tab = Tab.Overview;
            if (!same) { _scroll = Vector2.zero; _colScroll = Vector2.zero; CloseChild(); }
            // Asked only when old: a click down the board must not send a request per row.
            if ((DateTime.UtcNow - PlayerStatsCache.LastUpdatedUtc).TotalSeconds > 15) PlayerStatsHandler.RequestSnapshot();
            if ((DateTime.UtcNow - ColonistRosterCache.LastUpdatedUtc).TotalSeconds > 15) PlayerStatsHandler.RequestColonistRoster();
        }

        public void Closed() => CloseChild();

        private void CloseChild()
        {
            if (_childProfile != null && Find.WindowStack.IsOpen(_childProfile)) _childProfile.Close(false);
            _childProfile = null;
        }

        public bool Draw(Rect rect, bool closable)
        {
            bool close = false;
            GUI.BeginGroup(rect);
            try { close = DrawInner(new Rect(0f, 0f, rect.width, rect.height), closable); }
            finally
            {
                GUI.EndGroup();
                Text.Font = GameFont.Small; Text.Anchor = TextAnchor.UpperLeft; GUI.color = Color.white;
            }
            return close;
        }

        private bool DrawInner(Rect r, bool closable)
        {
            PlayerLeaderboardEntry e = PlayerStatsCache.Entries?.Find(x => string.Equals(x.Username, Username, StringComparison.OrdinalIgnoreCase));

            bool close = false;
            float y = DrawHeader(r, e, closable, ref close);
            y = DrawRanks(r, y);

            Text.Font = GameFont.Tiny;
            float footH = DialogLayout.TextRowH + 4f;
            Text.Font = GameFont.Small;

            if (e == null)
            {
                Rect msg = new Rect(0f, y + 4f, r.width, 40f);
                if (PlayerStatsCache.LastUpdatedUtc == DateTime.MinValue) DialogLayout.DrawWaiting(msg, KmhText.Get("KMH_PP_Asking", "Asking the server for player stats…"));
                else DialogLayout.LabelWrapped(0f, y + 4f, r.width, "<color=#A9AFB9>" + KmhText.Get("KMH_PP_NoStats", "No stats for this player on this server yet.") + "</color>");
                return close;
            }

            int cur = 0;
            for (int i = 0; i < Tabs.Length; i++) if (Tabs[i] == _tab) { cur = i; break; }
            y = DialogLayout.TabRow(y, r.width, TabLabels, cur, i => { _tab = Tabs[i]; _scroll = Vector2.zero; });

            Rect body = new Rect(0f, y, r.width, Mathf.Max(DialogLayout.MinBodyHeight, r.height - y - footH));
            Widgets.DrawMenuSection(body);
            if (_tab == Tab.Colonists) DrawColonists(body.ContractedBy(6f), e);
            else if (_tab == Tab.Overview) DrawLines(body, BuildLines(e), Tiles(e));
            else DrawLines(body, BuildLines(e), null);

            DrawFooter(new Rect(0f, body.yMax + 2f, r.width, footH - 2f));
            return close;
        }

        private float DrawHeader(Rect r, PlayerLeaderboardEntry e, bool closable, ref bool close)
        {
            float d = r.width < 380f ? 40f : 46f;
            bool online = ChatRosterCache.Known && ChatRosterCache.IsOnline(Username);
            LeaderboardsView.Monogram(new Rect(0f, 2f, d, d), Username, online ? KmhBrand.Ok : KmhBrand.LineStrong, GameFont.Small);
            Text.Font = GameFont.Small;

            string actions = KmhText.Get("KMH_PP_Actions", "Actions ▾");
            bool mine = KmhSession.Same(Username, KmhSession.Me);
            Text.Font = GameFont.Small;
            float actW = mine ? 0f : Mathf.Max(84f, Mathf.Ceil(Text.CalcSize(actions).x) + 20f);
            float x = d + 10f, w = Mathf.Max(0f, r.width - x - actW - 6f - (closable ? 30f : 0f));
            Text.Font = GameFont.Medium;
            float nameH = Mathf.Max(DialogLayout.TextRowH, 28f);
            DialogLayout.LabelTrunc(new Rect(x, 0f, w, nameH), LinkedAccountsCache.Format(Username), TextAnchor.MiddleLeft);
            Text.Font = GameFont.Small;
            float rowH = DialogLayout.TextRowH;

            string colony = e == null || string.IsNullOrEmpty(e.ColonyName)
                ? "<color=#A9AFB9>" + KmhText.Get("KMH_PP_NoColony", "no colony reported yet") + "</color>"
                : $"<b>{KmhDisplayText.Inert(e.ColonyName, 60)}</b>";
            string guild = e == null || string.IsNullOrEmpty(e.GuildName)
                ? "<color=#A9AFB9>" + KmhText.Get("KMH_PP_NoGuild", "no guild") + "</color>" : KmhDisplayText.GuildName(e.GuildName);
            Rect act = new Rect(r.width - actW - (closable ? 30f : 0f), 2f, actW, 24f);
            // Hidden on your own profile, where the menu's only entry would be "This is you".
            if (!mine && KmhBrand.Button(act, actions, false))
            {
                var t = new KmhPlayerMenu.Target { LocalName = Username, Shown = Username, NameIsPlayer = true };
                List<FloatMenuOption> options = KmhPlayerMenu.Options(t, withProfile: false);
                if (options.Count > 0) Find.WindowStack.Add(new FloatMenu(options, KmhPlayerMenu.Title(t)));
            }

            Rect line2 = new Rect(x, nameH, r.width - x, rowH);
            DialogLayout.LabelTrunc(line2, $"{colony}  <color=#A9AFB9>·</color>  {guild}");
            if (e != null && string.IsNullOrEmpty(e.ColonyName))
                TooltipHandler.TipRegion(line2, KmhText.Get("KMH_PP_NoColonyTip", "A colony's name arrives from the player's own game the first time it plays here with Colony Nexus."));

            string seen = online ? "<color=#4cc38a>● " + KmhText.Get("KMH_PP_OnlineNow", "Online now") + "</color>"
                        : e != null && e.LastSeenUtcTicks > 0 ? "<color=#A9AFB9>" + KmhText.Get("KMH_PP_LastSeen", "Last seen {0}", KmhAgo.Since(e.LastSeenUtcTicks)) + "</color>"
                        : "<color=#A9AFB9>" + KmhText.Get("KMH_PP_NotSeen", "Not seen yet") + "</color>";
            DialogLayout.LabelTrunc(new Rect(x, nameH + rowH, r.width - x, rowH),
                $"{seen}  <color=#A9AFB9>·</color>  {ReputationLine(Username)}");

            if (closable)
            {
                Rect xr = new Rect(r.width - 26f, 2f, 24f, 24f);
                if (KmhBrand.Button(xr, "×", false, KmhText.Get("KMH_PP_CloseProfile", "Close the profile"))) close = true;
            }
            return Mathf.Max(d + 4f, nameH + rowH * 2f) + 8f;
        }

        // The player's places on the server's own boards: the ranks the website and Discord show, never a local score.
        private float DrawRanks(Rect r, float y)
        {
            List<(string title, int rank, int total, bool reported)> ranks = Ranks();
            const float h = 22f, gap = 6f;
            if (!LeaderboardCache.HasSnapshot) return y;
            if (ranks.Count == 0)
            {
                DialogLayout.LabelTrunc(new Rect(0f, y, r.width, h), "<color=#A9AFB9>" + KmhText.Get("KMH_PP_NoRanks", "Not on any leaderboard yet.") + "</color>");
                return y + h + 6f;
            }

            Text.Font = GameFont.Tiny;
            float x = 0f;
            int shown = 0;
            for (int i = 0; i < ranks.Count; i++)
            {
                (string title, int rank, int total, bool reported) k = ranks[i];
                string label = $"#{k.rank} {k.title}";
                float w = Mathf.Ceil(Text.CalcSize(label).x) + 16f;
                int left = ranks.Count - i - 1;
                float moreW = left > 0 ? Mathf.Ceil(Text.CalcSize(KmhText.Get("KMH_PP_More", "+{0} more", left)).x) + 16f + gap : 0f;
                if (x + w + moreW > r.width && shown > 0) break;
                Rect chip = new Rect(x, y, Mathf.Min(w, r.width - x), h);
                Color tone = k.rank == 1 ? KmhBrand.Warn : k.rank <= 3 ? KmhBrand.Accent : KmhBrand.Ring;
                Widgets.DrawBoxSolid(chip, KmhBrand.WithAlpha(tone, 0.14f));
                GUI.color = KmhBrand.WithAlpha(tone, 0.55f);
                Widgets.DrawBox(chip, 1);
                GUI.color = Color.white;
                DialogLayout.LabelTrunc(chip, $"<color={KmhTheme.Hex(tone)}>{label}</color>", TextAnchor.MiddleCenter);
                TooltipHandler.TipRegion(chip, RankTip(k));
                x += chip.width + gap;
                shown++;
            }
            if (shown < ranks.Count)
            {
                string more = KmhText.Get("KMH_PP_More", "+{0} more", ranks.Count - shown);
                Rect mr = new Rect(x, y, Mathf.Ceil(Text.CalcSize(more).x) + 16f, h);
                Widgets.DrawBoxSolid(mr, KmhBrand.WithAlpha(KmhBrand.Ring, 0.08f));
                DialogLayout.LabelTrunc(mr, $"<color=#A9AFB9>{more}</color>", TextAnchor.MiddleCenter);
                var all = new List<string>();
                foreach (var k in ranks)
                    all.Add(KmhText.Get("KMH_PP_RankOf", "#{0} of {1}  {2}", k.rank, k.total.ToString("N0"), k.title)
                            + (k.reported ? "  " + KmhText.Get("KMH_PP_ReportedByGame", "(reported by their game)") : ""));
                TooltipHandler.TipRegion(mr, string.Join("\n", all) + "\n\n" + KmhText.Get("KMH_PP_TotalsSinceReset", "Totals on this server since its standings were last reset."));
            }
            Text.Font = GameFont.Small;
            return y + h + 8f;
        }

        private static string RankTip((string title, int rank, int total, bool reported) k)
            => KmhText.Get("KMH_PP_RankTipPlace", "#{0} of {1} on {2}.", k.rank, k.total.ToString("N0"), k.title) + "\n"
               + KmhText.Get("KMH_PP_RankTipCounted", "Counted on this server since its standings were last reset; the Season Archive keeps each season's leaders.") + "\n"
               + (k.reported ? KmhText.Get("KMH_PP_RankTipReported", "Reported by the player's own game; the server only checks it is plausible.")
                             : KmhText.Get("KMH_PP_RankTipMeasured", "Measured by the server itself."));

        private List<(string title, int rank, int total, bool reported)> Ranks()
        {
            string key = Username + "\n" + LeaderboardCache.LastUpdatedUtc.Ticks;
            if (_ranks != null && _ranksKey == key) return _ranks;
            _ranks = RanksFor(LeaderboardCache.Snapshot, Username);
            _ranksKey = key;
            return _ranks;
        }

        // Best places first, then the server's board order; guild boards rank guilds, not this player.
        internal static List<(string title, int rank, int total, bool reported)> RanksFor(LeaderboardsSnapshot snap, string username)
        {
            var list = new List<(string title, int rank, int total, bool reported, int order)>();
            if (snap?.Boards != null && !string.IsNullOrEmpty(username))
                for (int i = 0; i < snap.Boards.Count; i++)
                {
                    LeaderboardBoard b = snap.Boards[i];
                    if (b == null || b.IsGuildBoard || b.Rows == null) continue;
                    LeaderboardRow row = b.Rows.Find(x => string.Equals(x?.Name, username, StringComparison.OrdinalIgnoreCase));
                    if (row != null && row.Rank > 0) list.Add((LeaderboardText.Title(b), row.Rank, Math.Max(b.Total, row.Rank), b.Reported, i));
                }
            list.Sort((a, b) => a.rank != b.rank ? a.rank.CompareTo(b.rank) : a.order.CompareTo(b.order));
            var result = new List<(string title, int rank, int total, bool reported)>(list.Count);
            foreach (var t in list) result.Add((t.title, t.rank, t.total, t.reported));
            return result;
        }

        private static void DrawFooter(Rect r)
        {
            Text.Font = GameFont.Tiny;
            DateTime at = PlayerStatsCache.LastUpdatedUtc;
            double age = at == DateTime.MinValue ? -1 : (DateTime.UtcNow - at).TotalSeconds;
            string when = age < 0 ? KmhText.Get("KMH_PP_FootWaiting", "waiting for the server")
                        : age < 60 ? KmhText.Get("KMH_PP_FootJustNow", "updated just now")
                        : KmhText.Get("KMH_PP_FootAgo", "updated {0} ago", KmhAgo.Span((long)age));
            string text = KmhText.Get("KMH_PP_Footer", "Stats {0}  ·  totals on this server", when);
            DialogLayout.LabelTrunc(r, age > StaleSeconds ? $"<color=#f0c04f>{text}</color>" : $"<color=#A9AFB9>{text}</color>");
            Text.Font = GameFont.Small;
        }

        internal static string ReputationLine(string username)
        {
            string tier = KmhDisplayText.Inert(ReputationCache.TierFor(username) ?? "", 40);
            int score = ReputationCache.ScoreFor(username);
            if (string.IsNullOrEmpty(tier))
                return score == 0 ? "<color=#A9AFB9>" + KmhText.Get("KMH_PP_NoRep", "no reputation yet") + "</color>" : KmhText.Get("KMH_PP_Rep", "{0} rep", score.ToString("N0"));
            return $"{tier} ({KmhText.Get("KMH_PP_Rep", "{0} rep", score.ToString("N0"))})";
        }

        private static string S(long v) => SilverFmt.Format(v);
        private static string Reported(PlayerLeaderboardEntry e, string v) => e.LastReportUtcTicks > 0 ? v : KmhText.Get("KMH_PP_NotReported", "not reported");
        private static string Days(int d) => d <= 0 ? Dash : d == 1 ? KmhText.Get("KMH_PP_DayOne", "1 day") : KmhText.Get("KMH_PP_DayMany", "{0} days", d.ToString("N0"));
        private static string Hours(int h) => h > 0 ? $"{h:N0}h" : Dash;
        private static string Span(long s) => s > 0 ? KmhAgo.Span(s) : Dash;

        private static List<(string label, string value, string tip)> Tiles(PlayerLeaderboardEntry e)
        {
            int rep = ReputationCache.ScoreFor(e.Username);
            string tier = ReputationCache.TierFor(e.Username);
            return new List<(string, string, string)>
            {
                (KmhText.Get("KMH_PP_TileWealth", "Total wealth"), S(e.TotalWealth),
                    KmhText.Get("KMH_PP_TileWealthTip", "{0} in settlements (reported by their game) and {1} held in Colony Nexus (measured by the server).", S(e.Wealth), S(e.KmhWealth))),
                (KmhText.Get("KMH_PP_TileKills", "Kills"), Reported(e, e.Kills.ToString("N0")),
                    KmhText.Get("KMH_PP_TileKillsTip", "Reported by their game: everything the colony's people have killed.")),
                (KmhText.Get("KMH_PP_TabContracts", "Contracts"), e.QuestsCompleted.ToString("N0"),
                    KmhText.Get("KMH_PP_TileContractsTip", "Contracts completed on this server.")),
                (KmhText.Get("KMH_PP_TileRep", "Reputation"), string.IsNullOrEmpty(tier) && rep == 0 ? "—" : rep.ToString("N0"),
                    string.IsNullOrEmpty(tier) ? KmhText.Get("KMH_PP_TileRepNone", "No reputation recorded yet.") : tier),
                (KmhText.Get("KMH_PP_TileTrade", "Trade volume"), S(e.SalesEarned + e.PurchasesSpent),
                    KmhText.Get("KMH_PP_TileTradeTip", "{0} earned from sales, {1} spent on purchases.", S(e.SalesEarned), S(e.PurchasesSpent))),
                (KmhText.Get("KMH_PP_TileDonated", "Donated"), S(e.SilverDonated),
                    KmhText.Get("KMH_PP_TileDonatedTip", "Silver given to guild vaults and server projects.")),
                (KmhText.Get("KMH_PP_TileTime", "Time played"), Reported(e, e.TimePlayedHours > 0 ? $"{e.TimePlayedHours:N0}h" : "—"),
                    KmhText.Get("KMH_PP_TileTimeTip", "This colony's own play clock, reported by their game.")),
                (KmhText.Get("KMH_PP_TileAge", "Colony age"), Reported(e, e.ColonyAgeDays > 0 ? KmhText.Get("KMH_PP_DaysShort", "{0}d", e.ColonyAgeDays.ToString("N0")) : "—"),
                    KmhText.Get("KMH_PP_TileAgeTip", "In-game days since the colony was founded.")),
            };
        }

        private List<(string text, bool header)> BuildLines(PlayerLeaderboardEntry e)
        {
            List<(string, bool)> L = new List<(string, bool)>();
            void H(string s) => L.Add((s, true));
            void R(string s) => L.Add((s, false));

            if ((_tab == Tab.Colony || _tab == Tab.Combat) && e.LastReportUtcTicks <= 0)
            {
                H(_tab == Tab.Colony ? TabLabel(Tab.Colony) : TabLabel(Tab.Combat));
                R(Muted(KmhText.Get("KMH_PP_NoReport", "Their game has not sent a colony report to this server yet, so these figures are unknown.")));
                R(Muted(KmhText.Get("KMH_PP_NoReportCadence", "Reports arrive every two minutes while they play with Colony Nexus.")));
                if (_tab == Tab.Colony) R(KmhText.Get("KMH_PP_Holdings", "Colony Nexus holdings: {0}", S(e.KmhWealth)) + "  " + Muted(KmhText.Get("KMH_PP_ByServer", "(measured by the server)")));
                return L;
            }

            string sep = "  <color=#A9AFB9>·</color>  ";
            string topColonist = string.IsNullOrEmpty(e.TopColonistName) ? Dash : KmhDisplayText.Inert(e.TopColonistName);
            string topKills = Muted("(" + Kills(e.TopColonistKills) + ")");
            switch (_tab)
            {
                case Tab.Colony:
                    H(TabLabel(Tab.Colony));
                    R(KmhText.Get("KMH_PP_Name", "Name: {0}", string.IsNullOrEmpty(e.ColonyName) ? Dash : KmhDisplayText.Inert(e.ColonyName)));
                    R(KmhText.Get("KMH_PP_TotalWealth", "Total wealth: {0}", $"<b>{S(e.TotalWealth)}</b>"));
                    if (e.Settlements != null && e.Settlements.Count > 1)
                        foreach (SettlementReport s2 in e.Settlements)
                            R("   " + Muted($"{KmhDisplayText.Inert(s2.Name)}: {S(s2.Wealth)} · " + (s2.Population == 1
                                ? KmhText.Get("KMH_PP_ColonistOne", "1 colonist") : KmhText.Get("KMH_PP_ColonistMany", "{0} colonists", s2.Population))));
                    else
                        R("   " + Muted(KmhText.Get("KMH_PP_Settlements", "Settlements: {0}", S(e.Wealth))));
                    R("   " + Muted(KmhText.Get("KMH_PP_Holdings", "Colony Nexus holdings: {0}", S(e.KmhWealth))));
                    R(KmhText.Get("KMH_PP_Age", "Age: {0}", Days(e.ColonyAgeDays)));
                    R(KmhText.Get("KMH_PP_Population", "Population: {0}", e.Population.ToString("N0")));
                    R(KmhText.Get("KMH_PP_DevScore", "Development score: {0}", e.DevelopmentScore.ToString("N0")));
                    R(KmhText.Get("KMH_PP_DefScore", "Defense score: {0}", e.DefenseScore.ToString("N0")));
                    R(KmhText.Get("KMH_PP_Raids", "Raids survived: {0}", e.RaidsSurvived.ToString("N0")));
                    break;

                case Tab.Combat:
                    H(TabLabel(Tab.Combat));
                    R(KmhText.Get("KMH_PP_ColonyKills", "Colony kills: {0}", $"<b>{e.Kills:N0}</b>"));
                    R(KmhText.Get("KMH_PP_KillsHuman", "Humanlike {0}", e.KillsHumanlike.ToString("N0")) + sep
                      + KmhText.Get("KMH_PP_KillsMech", "mechanoid {0}", e.KillsMechanoid.ToString("N0")) + sep
                      + KmhText.Get("KMH_PP_KillsAnimal", "animal {0}", e.KillsAnimal.ToString("N0")));
                    R(KmhText.Get("KMH_PP_TopColonist", "Top colonist: {0}", topColonist) + "  " + topKills);
                    R(KmhText.Get("KMH_PP_Raids", "Raids survived: {0}", e.RaidsSurvived.ToString("N0")));
                    R(KmhText.Get("KMH_PP_Lost", "Colonists lost: {0}", e.PawnsLost.ToString("N0")));
                    break;

                case Tab.Contracts:
                    H(TabLabel(Tab.Contracts));
                    R(KmhText.Get("KMH_PP_Completed", "Completed: {0}", $"<b>{e.QuestsCompleted:N0}</b>"));
                    R(KmhText.Get("KMH_PP_Posted", "Posted: {0}", e.QuestsPosted.ToString("N0")));
                    R(KmhText.Get("KMH_PP_Bounties", "Bounties {0}", e.ContractsBounty.ToString("N0")) + sep
                      + KmhText.Get("KMH_PP_Deliveries", "deliveries {0}", e.ContractsDeliver.ToString("N0")) + sep
                      + KmhText.Get("KMH_PP_Hunts", "hunts {0}", e.ContractsHunt.ToString("N0")) + sep
                      + KmhText.Get("KMH_PP_Defenses", "defenses {0}", e.ContractsDefend.ToString("N0")));
                    R(KmhText.Get("KMH_PP_Failed", "Failed: {0}", e.ContractsFailed.ToString("N0")));
                    R(KmhText.Get("KMH_PP_Streak", "Current streak: {0}", $"<b>{e.ContractStreak:N0}</b>"));
                    break;

                case Tab.Trade:
                    H(TabLabel(Tab.Trade));
                    R(KmhText.Get("KMH_PP_Earned", "Silver earned from sales: {0}", $"<b>{S(e.SalesEarned)}</b>"));
                    R(KmhText.Get("KMH_PP_Spent", "Silver spent: {0}", S(e.PurchasesSpent)));
                    R(KmhText.Get("KMH_PP_Sales", "Sales {0}", e.MarketplaceSales.ToString("N0")) + sep
                      + KmhText.Get("KMH_PP_ItemsSold", "items sold {0}", e.ItemsSold.ToString("N0")) + sep
                      + KmhText.Get("KMH_PP_ItemsBought", "items bought {0}", e.ItemsBought.ToString("N0")));
                    R(KmhText.Get("KMH_PP_LargestSale", "Largest sale: {0}", S(e.LargestSale)));
                    R(KmhText.Get("KMH_PP_TradeVolume", "Trade volume: {0}", S(e.SalesEarned + e.PurchasesSpent)));
                    break;

                case Tab.GuildWork:
                    H(TabLabel(Tab.GuildWork));
                    R(KmhText.Get("KMH_PP_Guild", "Guild: {0}", string.IsNullOrEmpty(e.GuildName) ? Dash : KmhDisplayText.GuildName(e.GuildName)));
                    R(KmhText.Get("KMH_PP_Donated", "Silver donated: {0}", $"<b>{S(e.SilverDonated)}</b>"));
                    R(KmhText.Get("KMH_PP_SitesBuilt", "Sites built: {0}", e.SitesBuilt.ToString("N0")));
                    R(KmhText.Get("KMH_PP_SiteProduction", "Site production: {0}", S(e.SiteSilverProduced)));
                    R(KmhText.Get("KMH_PP_WorkerXp", "Worker XP: {0}", e.WorkerXp.ToString("N0")));
                    break;

                case Tab.History:
                {
                    H(KmhText.Get("KMH_PP_OnServer", "On this server"));
                    R(KmhText.Get("KMH_PP_FirstSeen", "First seen: {0}", e.FirstSeenUtcTicks > 0 ? KmhAgo.Since(e.FirstSeenUtcTicks) : Dash));
                    R(KmhText.Get("KMH_PP_LastSeenLine", "Last seen: {0}", e.LastSeenUtcTicks > 0 ? KmhAgo.Since(e.LastSeenUtcTicks) : Dash));
                    R(KmhText.Get("KMH_PP_Connected", "Connected: {0}", Span(e.ConnectedSeconds)) + "  "
                      + Muted("· " + KmhText.Get("KMH_PP_Active", "actively playing {0}", Span(e.ActiveSeconds))));
                    R(KmhText.Get("KMH_PP_FrontierWon", "Frontier claims won: {0}", e.FrontierCaptures.ToString("N0")) + sep
                      + KmhText.Get("KMH_PP_SitesBuiltLower", "sites built: {0}", e.SitesBuilt.ToString("N0")));
                    R(KmhText.Get("KMH_PP_BestStreak", "Best contract streak: {0}", e.ContractStreak.ToString("N0")));

                    Seasons.Dto.SeasonArchiveSnapshot arch = Seasons.SeasonArchiveCache.Snapshot;
                    if (arch == null)
                    {
                        L.Add(("", false));
                        R(Muted(KmhText.Get("KMH_PP_SeasonsLoading", "Season records are still loading…")));
                        break;
                    }

                    L.Add(("", false));
                    H(KmhText.Get("KMH_PP_RecordsHeld", "All-time server records held"));
                    int held = 0;
                    foreach (Seasons.Dto.SeasonRecordDto rec in arch.ServerRecords ?? new List<Seasons.Dto.SeasonRecordDto>())
                    {
                        if (rec == null || !KmhSession.Same(rec.Holder, e.Username)) continue;
                        R($"<b>{rec.Category}</b>  " + Muted("· " + KmhDisplayText.Inert(rec.Detail)
                          + (rec.Season > 0 ? " · " + KmhText.Get("KMH_PP_SeasonLower", "season {0}", rec.Season) : "")));
                        held++;
                    }
                    if (held == 0) R(Muted(KmhText.Get("KMH_PP_NoneYet", "None yet.")));

                    L.Add(("", false));
                    H(KmhText.Get("KMH_PP_Placements", "Season placements"));
                    int placed = 0;
                    foreach (Seasons.Dto.SeasonArchiveDto past in arch.Past ?? new List<Seasons.Dto.SeasonArchiveDto>())
                    {
                        if (past?.Records == null) continue;
                        foreach (Seasons.Dto.SeasonRecordDto rec in past.Records)
                        {
                            if (rec == null || !KmhSession.Same(rec.Holder, e.Username)) continue;
                            R(KmhText.Get("KMH_PP_Season", "Season {0}", past.Season) + sep + $"<b>{rec.Category}</b>  " + Muted("· " + KmhDisplayText.Inert(rec.Detail)));
                            placed++;
                        }
                    }
                    if (placed == 0)
                        R(Muted(arch.Past == null || arch.Past.Count == 0
                            ? KmhText.Get("KMH_PP_NoSeasonDone", "No season has finished on this server yet.")
                            : KmhText.Get("KMH_PP_NoCategoryLed", "No category led in a finished season.")));
                    break;
                }

                default:
                    H(KmhText.Get("KMH_PP_OnServer", "On this server"));
                    R(KmhText.Get("KMH_PP_ConnectedShort", "Connected {0}", Span(e.ConnectedSeconds)) + "  "
                      + Muted("· " + KmhText.Get("KMH_PP_Active", "actively playing {0}", Span(e.ActiveSeconds))));
                    R(KmhText.Get("KMH_PP_LastSeenLine", "Last seen: {0}", e.LastSeenUtcTicks > 0 ? KmhAgo.Since(e.LastSeenUtcTicks) : Dash));
                    L.Add(("", false));
                    H(TabLabel(Tab.Colony));
                    R(KmhText.Get("KMH_PP_InSettlements", "{0} in settlements", S(e.Wealth)) + sep
                      + KmhText.Get("KMH_PP_HeldInNexus", "{0} held in Colony Nexus", S(e.KmhWealth)));
                    R(KmhText.Get("KMH_PP_PopulationShort", "Population {0}", e.Population.ToString("N0")) + sep
                      + KmhText.Get("KMH_PP_RaidsLower", "raids survived {0}", e.RaidsSurvived.ToString("N0")));
                    R(KmhText.Get("KMH_PP_TopColonist", "Top colonist: {0}", topColonist) + "  " + topKills);
                    break;
            }
            return L;
        }

        private static string Muted(string s) => "<color=#A9AFB9>" + s + "</color>";
        private static string Kills(int n) => n == 1 ? KmhText.Get("KMH_PP_KillOne", "1 kill") : KmhText.Get("KMH_PP_KillMany", "{0} kills", n.ToString("N0"));

        private void DrawLines(Rect body, List<(string text, bool header)> lines, List<(string label, string value, string tip)> tiles)
        {
            Rect inner = body.ContractedBy(8f);
            float lineH = Mathf.Max(24f, DialogLayout.TextRowH);
            float viewW = inner.width - DialogLayout.ScrollbarReserveWidth;

            int cols = 0; float tileH = 0f, tilesH = 0f;
            if (tiles != null && tiles.Count > 0)
            {
                cols = Mathf.Clamp((int)(viewW / 140f), 2, 4);
                Text.Font = GameFont.Tiny;   float tinyH = DialogLayout.TextRowH;
                Text.Font = GameFont.Medium; float medH = DialogLayout.TextRowH;
                Text.Font = GameFont.Small;
                tileH = tinyH + medH + 10f;
                tilesH = Mathf.Ceil(tiles.Count / (float)cols) * (tileH + 6f) + 4f;
            }

            // Wraps rather than losing a sentence's end; measured narrower since 1.7 draws bold wider than it measures.
            float textW = viewW - 8f, linesH = 0f;
            float[] heights = new float[lines.Count];
            for (int i = 0; i < lines.Count; i++)
            {
                heights[i] = lines[i].header || string.IsNullOrEmpty(lines[i].text) ? lineH : Mathf.Max(lineH, Text.CalcHeight(lines[i].text, textW - 24f) + 4f);
                linesH += heights[i];
            }

            float viewH = Mathf.Max(inner.height, tilesH + linesH + 6f);
            Rect viewRect = new Rect(0f, 0f, viewW, viewH);
            Widgets.BeginScrollView(inner, ref _scroll, viewRect);
            if (cols > 0)
            {
                float tw = (viewW - (cols - 1) * 6f) / cols;
                for (int i = 0; i < tiles.Count; i++)
                {
                    Rect t = new Rect((i % cols) * (tw + 6f), (i / cols) * (tileH + 6f), tw, tileH);
                    Widgets.DrawBoxSolid(t, KmhBrand.WithAlpha(KmhBrand.Surface2, 0.85f));
                    Widgets.DrawBoxSolid(new Rect(t.x, t.y, 2f, t.height), KmhBrand.WithAlpha(KmhBrand.Accent, 0.7f));
                    Text.Font = GameFont.Tiny;
                    DialogLayout.LabelTrunc(new Rect(t.x + 8f, t.y + 4f, t.width - 12f, DialogLayout.TextRowH), $"<color=#A9AFB9>{tiles[i].label.ToUpperInvariant()}</color>");
                    float ty = t.y + 4f + DialogLayout.TextRowH;
                    Text.Font = GameFont.Medium;
                    DialogLayout.LabelTrunc(new Rect(t.x + 8f, ty, t.width - 12f, DialogLayout.TextRowH), tiles[i].value);
                    Text.Font = GameFont.Small;
                    if (!string.IsNullOrEmpty(tiles[i].tip)) TooltipHandler.TipRegion(t, tiles[i].tip);
                }
            }
            float ly = tilesH;
            for (int i = 0; i < lines.Count; i++)
            {
                (string text, bool header) = lines[i];
                if (header) DialogLayout.LabelTrunc(new Rect(0f, ly, viewRect.width, lineH), $"<color=#f0c04f><b>{text}</b></color>");
                else if (heights[i] > lineH) Widgets.Label(new Rect(8f, ly + 2f, textW, heights[i] - 2f), text);
                else if (!string.IsNullOrEmpty(text)) DialogLayout.LabelTrunc(new Rect(8f, ly, textW, lineH), text);
                ly += heights[i];
            }
            Widgets.EndScrollView();
        }

        private void OpenColonistProfile()
        {
            CloseChild();
            _childProfile = new Dialog_KMHColonistProfile(Username);
            Find.WindowStack.Add(_childProfile);
        }

        // Only the top colonist has a deep profile collected, so the rest show their roster record inline.
        private void DrawColonists(Rect box, PlayerLeaderboardEntry e)
        {
            string topName = (e.TopColonistName ?? "").Trim();

            List<ColonistEntry> mine = new List<ColonistEntry>();
            foreach (ColonistEntry c in ColonistRosterCache.Colonists)
                if (c != null && KmhSession.Same(c.Owner, Username)) mine.Add(c);

            // Deterministic: most kills first, then longest-serving, then name - never dictionary order.
            mine.Sort((a, b) =>
            {
                int k = b.Kills.CompareTo(a.Kills);
                if (k != 0) return k;
                int d = b.Days.CompareTo(a.Days);
                return d != 0 ? d : string.Compare(a.Name ?? "", b.Name ?? "", StringComparison.OrdinalIgnoreCase);
            });

            float lineH = DialogLayout.TextRowH;
            // Measured, not a constant: a fixed height clips the third line's descenders at larger fonts.
            float topH = lineH * 3f + 12f;
            float rowH = lineH * 2f + 6f;

            ColonistEntry topEntry = mine.Find(c => !string.IsNullOrEmpty(topName)
                                                 && string.Equals(c.Name ?? "", topName, StringComparison.OrdinalIgnoreCase));
            Rect top = new Rect(box.x, box.y, box.width, topH);
            Widgets.DrawLightHighlight(top);
            if (topEntry != null && IsFocus(topEntry)) DrawFocus(top);
            DialogLayout.LabelTrunc(new Rect(top.x + 6f, top.y + 4f, top.width - 12f, lineH), KmhTheme.Accented("★ " + KmhText.Get("KMH_PP_TopColonistHead", "Top colonist")));

            // Text and button share the width that exists; fixed reservations go negative on a narrow card.
            float btnW  = Mathf.Clamp(top.width - 160f, 0f, 190f);
            bool  showBtn = btnW >= 110f;
            float textW = Mathf.Max(0f, top.width - 12f - (showBtn ? btnW + 8f : 0f));

            DialogLayout.LabelTrunc(new Rect(top.x + 6f, top.y + 4f + lineH, textW, lineH),
                string.IsNullOrEmpty(topName) ? Muted(KmhText.Get("KMH_PP_NoneReported", "none reported"))
                                              : $"<b>{KmhDisplayText.Inert(topName)}</b>  <color=#A9AFB9>{KmhDisplayText.Inert(e.TopColonistTitle)}</color>");
            DialogLayout.LabelTrunc(new Rect(top.x + 6f, top.y + 4f + lineH * 2f, textW, lineH),
                topEntry != null ? Detail(topEntry) : Kills(e.TopColonistKills));

            // With no room for the button the whole card is clickable, so the deep profile is never unreachable.
            if (!string.IsNullOrEmpty(topName))
            {
                if (showBtn)
                {
                    if (KmhBrand.Button(new Rect(top.xMax - btnW - 6f, top.y + lineH, btnW, 26f), KmhText.Get("KMH_PP_FullProfile", "Full colonist profile"), false)) OpenColonistProfile();
                }
                else if (Widgets.ButtonInvisible(top)) OpenColonistProfile();
            }

            List<ColonistEntry> rest = new List<ColonistEntry>();
            foreach (ColonistEntry c in mine)
                if (topEntry == null || !ReferenceEquals(c, topEntry)) rest.Add(c);

            List<FallenColonistEntry> fallen = new List<FallenColonistEntry>();
            foreach (FallenColonistEntry f in ColonistRosterCache.Snapshot?.Fallen ?? new List<FallenColonistEntry>())
                if (f?.Colonist != null && KmhSession.Same(f.Colonist.Owner, Username)) fallen.Add(f);
            fallen.Sort((a, b) => b.DiedUtcTicks.CompareTo(a.DiedUtcTicks));

            float listY = box.y + topH + 6f;
            DialogLayout.LabelTrunc(new Rect(box.x + 2f, listY, box.width - 4f, lineH),
                ColonistRosterCache.HasSnapshot
                    ? "<b>" + KmhText.Get("KMH_PP_AllColonists", "All colonists") + "</b>  " + Muted("("
                      + (rest.Count == 1 ? KmhText.Get("KMH_PP_OtherOne", "1 other") : KmhText.Get("KMH_PP_OtherMany", "{0} others", rest.Count))
                      + (fallen.Count > 0 ? " · " + KmhText.Get("KMH_PP_InMemory", "{0} in memory", fallen.Count) : "") + ")")
                    : Muted(KmhText.Get("KMH_PP_LoadingColonists", "Loading colonists…")));
            listY += lineH + 4f;

            Rect listBox = new Rect(box.x, listY, box.width, Mathf.Max(0f, box.yMax - listY));
            if (listBox.height <= 0f) return;

            if (_scrollToFocus && ColonistRosterCache.HasSnapshot)
            {
                int at = rest.FindIndex(IsFocus);
                if (at < 0) { at = fallen.FindIndex(f => IsFocus(f.Colonist)); if (at >= 0) at += rest.Count; }
                if (at >= 0) _colScroll.y = Mathf.Max(0f, at * rowH - listBox.height / 2f + rowH);
                _scrollToFocus = false;
            }

            string empty = !ColonistRosterCache.HasSnapshot ? null
                         : (rest.Count + fallen.Count == 0 ? Muted(KmhText.Get("KMH_PP_NoOtherColonists", "No other colonists reported for this player.")) : null);
            DialogLayout.ScrollList(listBox, ref _colScroll, rest.Count + fallen.Count, rowH, (i, r) =>
            {
                bool gone = i >= rest.Count;
                FallenColonistEntry f = gone ? fallen[i - rest.Count] : null;
                ColonistEntry c = gone ? f.Colonist : rest[i];
                if (IsFocus(c)) DrawFocus(r);
                DialogLayout.LabelTrunc(new Rect(r.x + 6f, r.y + 2f, r.width - 12f, lineH), gone
                    ? $"<color=#A9AFB9>†</color> <b>{KmhDisplayText.Inert(c.Name)}</b>  " + Muted(KmhDisplayText.Inert(c.Title) + " · "
                      + (f.ColonyDay > 0 ? KmhText.Get("KMH_PP_FellOnDay", "fell on day {0}", f.ColonyDay.ToString("N0")) : KmhText.Get("KMH_PP_Fell", "fell"))
                      + (f.DiedUtcTicks > 0 ? ", " + KmhAgo.Since(f.DiedUtcTicks) : ""))
                    : $"<b>{KmhDisplayText.Inert(c.Name)}</b>  <color=#A9AFB9>{KmhDisplayText.Inert(c.Title)}</color>");
                Color prev = GUI.color; GUI.color = DialogLayout.MutedColor;
                DialogLayout.LabelTrunc(new Rect(r.x + 6f, r.y + 2f + lineH, r.width - 12f, lineH), Detail(c));
                GUI.color = prev;
            }, empty);
        }

        private bool IsFocus(ColonistEntry c)
            => _focusColonist != null && c != null && string.Equals(c.Name ?? "", _focusColonist, StringComparison.OrdinalIgnoreCase);

        private static void DrawFocus(Rect r)
        {
            Widgets.DrawBoxSolid(r, KmhBrand.WithAlpha(KmhBrand.Accent, 0.12f));
            Widgets.DrawBoxSolid(new Rect(r.x, r.y, 3f, r.height), KmhBrand.Accent);
        }

        private static string Detail(ColonistEntry c)
        {
            List<string> bits = new List<string>();
            if (c.Age  > 0) bits.Add(KmhText.Get("KMH_PP_AgeShort", "age {0}", c.Age));
            if (c.Days > 0) bits.Add(KmhText.Get("KMH_PP_DaysServed", "{0}d served", c.Days.ToString("N0")));
            bits.Add(Kills(c.Kills));
            string sk = TopSkills(c);
            if (sk.Length > 0) bits.Add(sk);
            return string.Join("  <color=#A9AFB9>·</color>  ", bits);
        }

        // The game's own skill names, so they read in the player's language.
        private static string SkillName(RimWorld.SkillDef def, string fallback)
        {
            try { string s = def?.LabelCap; return string.IsNullOrEmpty(s) ? fallback : s; }
            catch { return fallback; }
        }

        private static string TopSkills(ColonistEntry c)
        {
            (string n, int v)[] all =
            {
                (SkillName(RimWorld.SkillDefOf.Shooting, "Shooting"), c.SkShooting), (SkillName(RimWorld.SkillDefOf.Melee, "Melee"), c.SkMelee),
                (SkillName(RimWorld.SkillDefOf.Medicine, "Medicine"), c.SkMedicine), (SkillName(RimWorld.SkillDefOf.Crafting, "Crafting"), c.SkCrafting),
                (SkillName(RimWorld.SkillDefOf.Construction, "Construction"), c.SkConstruction),
            };
            Array.Sort(all, (a, b) => b.v.CompareTo(a.v));
            List<string> best = new List<string>();
            foreach ((string n, int v) in all) { if (v <= 0 || best.Count == 2) continue; best.Add($"{n} {v}"); }
            return best.Count == 0 ? "" : string.Join(", ", best);
        }
    }
}
