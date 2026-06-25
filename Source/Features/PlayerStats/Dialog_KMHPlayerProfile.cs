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

        private enum Tab { Overview, Colony, TopColonist, Combat, Contracts, Trade, GuildWork, History }

        private static readonly (Tab tab, string label)[] Tabs =
        {
            (Tab.Overview,    "Overview"),
            (Tab.Colony,      "Colony"),
            (Tab.TopColonist, "Top Colonist"),
            (Tab.Combat,      "Combat"),
            (Tab.Contracts,   "Contracts"),
            (Tab.Trade,       "Trade"),
            (Tab.GuildWork,   "Guild Work"),
            (Tab.History,     "History"),
        };

        public Dialog_KMHPlayerProfile(string username)
        {
            _username = username ?? "";
            doCloseX = true; forcePause = false; absorbInputAroundWindow = true; draggable = true; resizeable = true;
            PlayerStatsHandler.RequestSnapshot();
        }

        // Wide enough that the full 8-tab row (Overview … History) fits without the last tab clipping the edge.
        public override Vector2 InitialSize => new Vector2(700f, 620f);

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

            // Tab row.
            float tx = 0f;
            foreach ((Tab tab, string label) in Tabs)
            {
                float w = Mathf.Max(66f, Text.CalcSize(label).x + 18f);
                Color old = GUI.color; if (_tab == tab) GUI.color = new Color(0.45f, 0.75f, 1f);
                if (Widgets.ButtonText(new Rect(tx, y, w, 26f), label)) _tab = tab;
                GUI.color = old;
                tx += w + 3f;
            }
            y += 32f;

            Rect body = new Rect(0f, y, rect.width, rect.height - y - DialogLayout.FooterReserve);
            Widgets.DrawMenuSection(body);

            // The Top Colonist tab gets a button into the full colonist profile.
            Rect lines = body;
            if (_tab == Tab.TopColonist)
            {
                if (Widgets.ButtonText(new Rect(body.x + 8f, body.y + 6f, 220f, 26f), "View full colonist profile"))
                    Find.WindowStack.Add(new Dialog_KMHColonistProfile(_username));
                lines = new Rect(body.x, body.y + 36f, body.width, body.height - 36f);
            }
            DrawLines(lines, BuildLines(e));

            if (DialogLayout.DrawCloseButton(rect)) Close();
        }

        private static long OverallScore(PlayerLeaderboardEntry e)
            => e.Wealth / 1000 + e.Kills * 50 + (long)e.QuestsCompleted * 100 + ReputationCache.ScoreFor(e.Username) * 10 + e.TimePlayedHours;

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
                    R($"Wealth: <b>{S(e.Wealth)}</b>");
                    R($"Age: {(e.ColonyAgeDays > 0 ? e.ColonyAgeDays + " days" : Dash)}");
                    R($"Time played: {(e.TimePlayedHours > 0 ? e.TimePlayedHours + "h" : Dash)}");
                    R($"Population: {e.Population}");
                    R($"Development score: {e.DevelopmentScore:N0}");
                    R($"Defense score: {e.DefenseScore:N0}");
                    R($"Raids survived: {e.RaidsSurvived}");
                    break;

                case Tab.TopColonist:
                    H("Top Colonist");
                    R($"Name: {(string.IsNullOrEmpty(e.TopColonistName) ? Dash : e.TopColonistName)}");
                    R($"Title: {(string.IsNullOrEmpty(e.TopColonistTitle) ? Dash : e.TopColonistTitle)}");
                    R($"Kills: {e.TopColonistKills}");
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
                    H("History");
                    R("<color=grey>Recent activity + season records arrive in a later update.</color>");
                    break;

                default: // Overview
                    H("Standing");
                    R($"Wealth: <b>{S(e.Wealth)}</b>");
                    R($"Kills: {e.Kills:N0}");
                    R($"Contracts completed: {e.QuestsCompleted}");
                    R($"Reputation: {ReputationCache.ScoreFor(e.Username)} ({ReputationCache.TierFor(e.Username)})");
                    R($"Time played: {(e.TimePlayedHours > 0 ? e.TimePlayedHours + "h" : Dash)}");
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
