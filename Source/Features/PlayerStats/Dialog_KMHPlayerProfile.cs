using System;
using System.Collections.Generic;
using KMHPatch.Features.LinkedAccounts;
using KMHPatch.Features.PlayerStats.Dto;
using KMHPatch.Features.Reputation;
using KMHPatch.UI;
using UnityEngine;
using Verse;

namespace KMHPatch.Features.PlayerStats
{
    // Player Profile shows cached standings across all tabs, using "—" for fields not tracked yet.
    public class Dialog_KMHPlayerProfile : Window_KMHBase
    {
        private readonly string _username;
        private Tab     _tab = Tab.Overview;
        private Vector2 _scroll;

        private enum Tab { Overview, Colony, Colonists, Combat, Contracts, Trade, GuildWork, History }

        private static readonly (Tab tab, string label)[] Tabs =
        {
            (Tab.Overview,    "Overview"),
            (Tab.Colony,      "Colony"),
            (Tab.Colonists,   "Colonists"),
            (Tab.Combat,      "Combat"),
            (Tab.Contracts,   "Contracts"),
            (Tab.Trade,       "Trade"),
            (Tab.GuildWork,   "Guild Work"),
            (Tab.History,     "History"),
        };

        private static readonly string[] TabLabels = BuildTabLabels();
        private static string[] BuildTabLabels()
        {
            string[] a = new string[Tabs.Length];
            for (int i = 0; i < Tabs.Length; i++) a[i] = Tabs[i].label;
            return a;
        }

        public Dialog_KMHPlayerProfile(string username)
        {
            _username = username ?? "";
            doCloseX = true; forcePause = false; absorbInputAroundWindow = true; draggable = true; resizeable = true;
            PlayerStatsHandler.RequestSnapshot();
            PlayerStatsHandler.RequestColonistRoster();   // the Colonists tab reads this; ask once on open
        }

        // A child colonist profile belongs to this window - close it alongside rather than leaving it orphaned.
        public override void PostClose()
        {
            base.PostClose();
            if (_childProfile != null && Find.WindowStack.IsOpen(_childProfile)) _childProfile.Close(false);
            _childProfile = null;
        }

        // Wide enough that the full 8-tab row (Overview … History) fits without the last tab clipping the edge.
        public override Vector2 InitialSize => KMHPatch.UI.DialogLayout.FitToScreen(700f, 620f);

        private const string Dash = "<color=grey>—</color>";

        protected override void DrawContents(Rect rect)
        {
            List<PlayerLeaderboardEntry> all = PlayerStatsCache.Entries;
            PlayerLeaderboardEntry e = all?.Find(x => string.Equals(x.Username, _username, StringComparison.OrdinalIgnoreCase));

            float y = DialogLayout.DrawTitle(rect, LinkedAccountsCache.Format(_username));
            DialogLayout.DrawSectionDivider(rect, ref y);

            if (e == null)
            {
                DialogLayout.LabelTrunc(new Rect(0f, y, rect.width, 24f), "<color=grey>No stats for this player yet.</color>");
                if (DialogLayout.DrawCloseButton(rect)) Close();
                return;
            }

            int overall = 1;
            if (all != null) foreach (PlayerLeaderboardEntry x in all) if (OverallScore(x) > OverallScore(e)) overall++;

            string guild  = string.IsNullOrEmpty(e.GuildName) ? "<color=grey>no guild</color>" : e.GuildName;
            string colony = string.IsNullOrEmpty(e.ColonyName) ? "<color=grey>unknown colony</color>" : $"<b>{e.ColonyName}</b>";
            DialogLayout.LabelTrunc(new Rect(0f, y, rect.width, 22f),
                $"<b>#{overall} overall</b>  <color=grey>·</color>  {colony}  <color=grey>·</color>  {guild}  <color=grey>·</color>  {ReputationCache.TierFor(_username)} ({ReputationCache.ScoreFor(_username)} rep)");
            y += 26f;

            // Wrapping tab strip - eight tabs do not fit one line on a narrow window.
            int cur = 0;
            for (int i = 0; i < Tabs.Length; i++) if (Tabs[i].tab == _tab) { cur = i; break; }
            y = DialogLayout.TabRow(y, rect.width, TabLabels, cur, i => _tab = Tabs[i].tab);

            Rect body = new Rect(0f, y, rect.width, DialogLayout.BodyHeight(rect, y));
            Widgets.DrawMenuSection(body);

            if (_tab == Tab.Colonists) DrawColonists(body.ContractedBy(6f), e);
            else                       DrawLines(body, BuildLines(e));

            if (DialogLayout.DrawCloseButton(rect)) Close();
        }

        private Vector2 _colScroll;

        // One child colonist profile per window: another click reuses it rather than stacking.
        private Dialog_KMHColonistProfile _childProfile;

        private void OpenColonistProfile()
        {
            if (_childProfile != null && Find.WindowStack.IsOpen(_childProfile)) _childProfile.Close(false);
            _childProfile = new Dialog_KMHColonistProfile(_username);
            Find.WindowStack.Add(_childProfile);
        }

        // Only the top colonist has a deep profile collected, so the rest show their roster record inline.
        private void DrawColonists(Rect box, PlayerLeaderboardEntry e)
        {
            string topName = (e.TopColonistName ?? "").Trim();

            List<ColonistEntry> mine = new List<ColonistEntry>();
            foreach (ColonistEntry c in ColonistRosterCache.Colonists)
                if (c != null && KmhSession.Same(c.Owner, _username)) mine.Add(c);

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

            Rect top = new Rect(box.x, box.y, box.width, topH);
            Widgets.DrawLightHighlight(top);
            DialogLayout.LabelTrunc(new Rect(top.x + 6f, top.y + 4f, top.width - 12f, lineH),
                UI.KmhTheme.Accented("★ Top Colonist"));
            ColonistEntry topEntry = mine.Find(c => !string.IsNullOrEmpty(topName)
                                                 && string.Equals(c.Name ?? "", topName, StringComparison.OrdinalIgnoreCase));

            // Text and button share the width that exists; fixed reservations go negative on a narrow card.
            float btnW  = Mathf.Clamp(top.width - 160f, 0f, 218f);
            bool  showBtn = btnW >= 110f;
            float textW = Mathf.Max(0f, top.width - 12f - (showBtn ? btnW + 8f : 0f));

            DialogLayout.LabelTrunc(new Rect(top.x + 6f, top.y + 4f + lineH, textW, lineH),
                string.IsNullOrEmpty(topName) ? "<color=grey>none reported</color>"
                                              : $"<b>{topName}</b>  <color=grey>{e.TopColonistTitle}</color>");
            DialogLayout.LabelTrunc(new Rect(top.x + 6f, top.y + 4f + lineH * 2f, textW, lineH),
                topEntry != null ? Detail(topEntry) : $"<color=grey>Kills:</color> {e.TopColonistKills}");

            // With no room for the button the whole card is clickable, so the deep profile is never unreachable.
            if (showBtn)
            {
                if (Widgets.ButtonText(new Rect(top.xMax - btnW - 6f, top.y + lineH, btnW, 26f), "View full colonist profile"))
                    OpenColonistProfile();
            }
            else if (Widgets.ButtonInvisible(top))
            {
                OpenColonistProfile();
            }

            List<ColonistEntry> rest = new List<ColonistEntry>();
            foreach (ColonistEntry c in mine)
                if (topEntry == null || !ReferenceEquals(c, topEntry)) rest.Add(c);   // never list the top one twice

            float listY = box.y + topH + 6f;
            DialogLayout.LabelTrunc(new Rect(box.x + 2f, listY, box.width - 4f, lineH),
                ColonistRosterCache.HasSnapshot
                    ? $"<b>All colonists</b>  <color=grey>({rest.Count} other{(rest.Count == 1 ? "" : "s")})</color>"
                    : "<color=grey>Loading colonists…</color>");
            listY += 24f;

            Rect listBox = new Rect(box.x, listY, box.width, Mathf.Max(0f, box.yMax - listY));
            if (listBox.height <= 0f) return;

            string empty = !ColonistRosterCache.HasSnapshot ? null
                         : (rest.Count == 0 ? "<color=grey>No other colonists reported for this player.</color>" : null);
            DialogLayout.ScrollList(listBox, ref _colScroll, rest.Count, rowH, (i, r) =>
            {
                ColonistEntry c = rest[i];
                DialogLayout.LabelTrunc(new Rect(r.x + 6f, r.y + 2f, r.width - 12f, lineH),
                    $"<b>{c.Name}</b>  <color=grey>{c.Title}</color>");
                Color prev = GUI.color; GUI.color = DialogLayout.MutedColor;
                DialogLayout.LabelTrunc(new Rect(r.x + 6f, r.y + 2f + lineH, r.width - 12f, lineH), Detail(c));
                GUI.color = prev;
            }, empty);
        }

        // One compact line of roster facts, skipping anything the client never reported.
        private static string Detail(ColonistEntry c)
        {
            List<string> bits = new List<string>();
            if (c.Age   > 0) bits.Add($"age {c.Age}");
            if (c.Days  > 0) bits.Add($"{c.Days}d served");
            bits.Add($"{c.Kills} kill{(c.Kills == 1 ? "" : "s")}");
            string sk = TopSkills(c);
            if (sk.Length > 0) bits.Add(sk);
            return string.Join("  <color=grey>·</color>  ", bits);
        }

        // The two strongest reported skills, so a row says what the colonist is FOR at a glance.
        private static string TopSkills(ColonistEntry c)
        {
            (string n, int v)[] all =
            {
                ("Shooting", c.SkShooting), ("Melee", c.SkMelee), ("Medicine", c.SkMedicine),
                ("Crafting", c.SkCrafting), ("Construction", c.SkConstruction),
            };
            System.Array.Sort(all, (a, b) => b.v.CompareTo(a.v));
            List<string> best = new List<string>();
            foreach ((string n, int v) in all) { if (v <= 0 || best.Count == 2) continue; best.Add($"{n} {v}"); }
            return best.Count == 0 ? "" : string.Join(", ", best);
        }

        private static long OverallScore(PlayerLeaderboardEntry e)
            => e.TotalWealth / 1000 + e.Kills * 50 + (long)e.QuestsCompleted * 100 + ReputationCache.ScoreFor(e.Username) * 10 + e.TimePlayedHours;

        private List<(string text, bool header)> BuildLines(PlayerLeaderboardEntry e)
        {
            List<(string, bool)> L = new List<(string, bool)>();
            void H(string s) => L.Add((s, true));
            void R(string s) => L.Add((s, false));
            string S(long v) => SilverFmt.Format(v);

            switch (_tab)
            {
                case Tab.Colony:
                    H("Colony");
                    R($"Name: {(string.IsNullOrEmpty(e.ColonyName) ? Dash : e.ColonyName)}");
                    R($"Total wealth: <b>{S(e.TotalWealth)}</b>");
                    if (e.Settlements != null && e.Settlements.Count > 1)
                        foreach (Dto.SettlementReport s2 in e.Settlements)
                            R($"   <color=grey>{s2.Name}: {S(s2.Wealth)} · {s2.Population} colonist(s)</color>");
                    else
                        R($"   <color=grey>Settlements: {S(e.Wealth)}</color>");
                    R($"   <color=grey>KMH holdings: {S(e.KmhWealth)}</color>");
                    R($"Age: {(e.ColonyAgeDays > 0 ? e.ColonyAgeDays + " days" : Dash)}");
                    R($"Time played: {(e.TimePlayedHours > 0 ? e.TimePlayedHours + "h" : Dash)}");
                    // This colony's own clock above; time spent HERE below, which is the server's own count.
                    R($"Connected on this server: {(e.ConnectedSeconds > 0 ? Chat.KmhAgo.Span(e.ConnectedSeconds) : Dash)}");
                    R($"Of that, actively playing: {(e.ActiveSeconds > 0 ? Chat.KmhAgo.Span(e.ActiveSeconds) : Dash)}");
                    R($"Last seen: {(e.LastSeenUtcTicks > 0 ? Chat.KmhAgo.Since(e.LastSeenUtcTicks) : Dash)}");
                    R($"Population: {e.Population}");
                    R($"Development score: {e.DevelopmentScore:N0}");
                    R($"Defense score: {e.DefenseScore:N0}");
                    R($"Raids survived: {e.RaidsSurvived}");
                    break;


                case Tab.Combat:
                    H("Combat");
                    R($"Colony kills: <b>{e.Kills:N0}</b>");
                    R($"By type — humanlike {e.KillsHumanlike:N0} · mech {e.KillsMechanoid:N0} · animal {e.KillsAnimal:N0}");
                    R($"Top colonist kills: {e.TopColonistKills}");
                    R($"Raids survived: {e.RaidsSurvived}");
                    R($"Pawns lost: {e.PawnsLost}");
                    break;

                case Tab.Contracts:
                    H("Contracts");
                    R($"Completed: <b>{e.QuestsCompleted}</b>");
                    R($"Posted: {e.QuestsPosted}");
                    R($"Bounties: {e.ContractsBounty}   Deliveries: {e.ContractsDeliver}   Hunts: {e.ContractsHunt}   Defenses: {e.ContractsDefend}");
                    R($"Failed: {e.ContractsFailed}");
                    R($"Current streak: <b>{e.ContractStreak}</b>");
                    break;

                case Tab.Trade:
                    H("Trade");
                    R($"Silver earned (sales): <b>{S(e.SalesEarned)}</b>");
                    R($"Silver spent: {S(e.PurchasesSpent)}");
                    R($"Sales: {e.MarketplaceSales}   ·   Items sold: {e.ItemsSold:N0}   ·   Items bought: {e.ItemsBought:N0}");
                    R($"Largest sale: {S(e.LargestSale)}");
                    R($"Trade volume: {S(e.SalesEarned + e.PurchasesSpent)}");
                    break;

                case Tab.GuildWork:
                    H("Guild Work");
                    R($"Guild: {(string.IsNullOrEmpty(e.GuildName) ? Dash : e.GuildName)}");
                    R($"Silver donated: <b>{S(e.SilverDonated)}</b>");
                    R($"Sites built: {e.SitesBuilt}");
                    R($"Site production: {S(e.SiteSilverProduced)}");
                    R($"Worker XP: {e.WorkerXp:N0}");
                    break;

                case Tab.History:
                {
                    H("On this server");
                    R($"First seen: {(e.FirstSeenUtcTicks > 0 ? Chat.KmhAgo.Since(e.FirstSeenUtcTicks) : Dash)}");
                    R($"Last seen: {(e.LastSeenUtcTicks > 0 ? Chat.KmhAgo.Since(e.LastSeenUtcTicks) : Dash)}");
                    R($"Connected: {(e.ConnectedSeconds > 0 ? Chat.KmhAgo.Span(e.ConnectedSeconds) : Dash)}"
                      + $"  <color=grey>· actively playing {(e.ActiveSeconds > 0 ? Chat.KmhAgo.Span(e.ActiveSeconds) : Dash)}</color>");
                    R($"Frontier claims won: {e.FrontierCaptures}   <color=grey>·</color>   Sites built: {e.SitesBuilt}");
                    R($"Best contract streak: {e.ContractStreak}");

                    Seasons.Dto.SeasonArchiveSnapshot arch = Seasons.SeasonArchiveCache.Snapshot;
                    if (arch == null)
                    {
                        L.Add(("", false));
                        R("<color=grey>Season records are still loading…</color>");
                        break;
                    }

                    L.Add(("", false));
                    H("All-time server records held");
                    int held = 0;
                    foreach (Seasons.Dto.SeasonRecordDto rec in arch.ServerRecords ?? new List<Seasons.Dto.SeasonRecordDto>())
                    {
                        if (rec == null || !KmhSession.Same(rec.Holder, e.Username)) continue;
                        R($"<b>{rec.Category}</b>  <color=grey>· {rec.Detail}" + (rec.Season > 0 ? $" · season {rec.Season}" : "") + "</color>");
                        held++;
                    }
                    if (held == 0) R("<color=grey>None yet.</color>");

                    L.Add(("", false));
                    H("Season placements");
                    int placed = 0;
                    foreach (Seasons.Dto.SeasonArchiveDto past in arch.Past ?? new List<Seasons.Dto.SeasonArchiveDto>())
                    {
                        if (past?.Records == null) continue;
                        foreach (Seasons.Dto.SeasonRecordDto rec in past.Records)
                        {
                            if (rec == null || !KmhSession.Same(rec.Holder, e.Username)) continue;
                            R($"Season {past.Season}  <color=grey>·</color>  <b>{rec.Category}</b>  <color=grey>· {rec.Detail}</color>");
                            placed++;
                        }
                    }
                    if (placed == 0)
                        R(arch.Past == null || arch.Past.Count == 0
                            ? "<color=grey>No season has finished on this server yet.</color>"
                            : "<color=grey>No category led in a finished season.</color>");
                    break;
                }

                default: // Overview
                    H("Standing");
                    R($"Total wealth: <b>{S(e.TotalWealth)}</b>");
                    R($"   <color=grey>{S(e.Wealth)} in settlements · {S(e.KmhWealth)} held in KMH</color>");
                    R($"Kills: {e.Kills:N0}");
                    R($"Contracts completed: {e.QuestsCompleted}");
                    R($"Reputation: {ReputationCache.ScoreFor(e.Username)} ({ReputationCache.TierFor(e.Username)})");
                    R($"Time played: {(e.TimePlayedHours > 0 ? e.TimePlayedHours + "h" : Dash)}");
                    // This colony's own clock above; time spent HERE below, which is the server's own count.
                    R($"Connected on this server: {(e.ConnectedSeconds > 0 ? Chat.KmhAgo.Span(e.ConnectedSeconds) : Dash)}");
                    R($"Of that, actively playing: {(e.ActiveSeconds > 0 ? Chat.KmhAgo.Span(e.ActiveSeconds) : Dash)}");
                    R($"Last seen: {(e.LastSeenUtcTicks > 0 ? Chat.KmhAgo.Since(e.LastSeenUtcTicks) : Dash)}");
                    R($"Colony age: {(e.ColonyAgeDays > 0 ? e.ColonyAgeDays + " days" : Dash)}");
                    L.Add(("", false));
                    H("Economy");
                    R($"Donated {S(e.SilverDonated)} · Sales {S(e.SalesEarned)} · Spent {S(e.PurchasesSpent)}");
                    R($"Top colonist: {(string.IsNullOrEmpty(e.TopColonistName) ? Dash : e.TopColonistName)} ({e.TopColonistKills} kills)");
                    break;
            }
            return L;
        }

        private void DrawLines(Rect body, List<(string text, bool header)> lines)
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
                else if (!string.IsNullOrEmpty(text)) DialogLayout.LabelTrunc(new Rect(8f, ly, viewRect.width - 8f, lineH), text);
                ly += lineH;
            }
            Widgets.EndScrollView();
        }
    }
}
