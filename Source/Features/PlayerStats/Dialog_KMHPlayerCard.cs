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
    // Single-player stat card: every metric with its rank against the whole roster. Opened by clicking a row on
    // the player leaderboard
    public class Dialog_KMHPlayerCard : Window_KMHBase
    {
        private readonly string _username;

        public Dialog_KMHPlayerCard(string username)
        {
            _username = username ?? "";
            doCloseX = true;
            forcePause = false;
            absorbInputAroundWindow = false;
            closeOnClickedOutside = true;
        }

        public override Vector2 InitialSize => new Vector2(440f, 460f);

        protected override void DrawContents(Rect rect)
        {
            List<PlayerLeaderboardEntry> all = PlayerStatsCache.Entries;
            PlayerLeaderboardEntry me = all?.Find(e => string.Equals(e.Username, _username, StringComparison.OrdinalIgnoreCase));

            float y = DialogLayout.DrawTitle(rect, LinkedAccountsCache.Format(_username));
            DialogLayout.DrawSectionDivider(rect, ref y);

            if (me == null)
            {
                DialogLayout.LabelTrunc(new Rect(0f, y, rect.width, 24f), "<color=grey>No stats for this player yet.</color>");
                if (DialogLayout.DrawCloseButton(rect)) Close();
                return;
            }

            int RankOf(Func<PlayerLeaderboardEntry, long> metric)
            {
                long mine = metric(me);
                int better = 0;
                foreach (PlayerLeaderboardEntry e in all) if (metric(e) > mine) better++;
                return better + 1;
            }

            string tier  = ReputationCache.TierFor(me.Username);
            int    rep   = ReputationCache.ScoreFor(me.Username);
            string guild = string.IsNullOrEmpty(me.GuildName) ? "<color=grey>no guild</color>" : me.GuildName;

            DialogLayout.LabelTrunc(new Rect(0f, y, rect.width, 24f),
                $"<b>#{RankOf(e => e.EconomyScore)} overall</b>  ·  {guild}  ·  {tier} ({rep} rep)");
            y += 28f;
            DialogLayout.LabelTrunc(new Rect(0f, y, rect.width, 22f),
                me.IsLinkedToDiscord ? "<color=#7289DA>Linked to Discord</color>" : "<color=grey>Not linked to Discord</color>");
            y += 26f;
            DialogLayout.DrawSectionDivider(rect, ref y);

            void Row(string label, string value, int rank)
            {
                DialogLayout.LabelTrunc(new Rect(8f, y, rect.width * 0.42f, 24f), label);
                DialogLayout.LabelTrunc(new Rect(rect.width * 0.44f, y, rect.width * 0.34f, 24f), $"<b>{value}</b>");
                DialogLayout.LabelTrunc(new Rect(rect.width * 0.80f, y, rect.width * 0.20f, 24f),
                    rank <= 3 ? $"<color=yellow>#{rank}</color>" : $"<color=grey>#{rank}</color>");
                y += 26f;
            }

            Row("Economy score",   me.EconomyScore.ToString("N0"),        RankOf(e => e.EconomyScore));
            Row("Silver donated",  SilverFmt.Format(me.SilverDonated),    RankOf(e => e.SilverDonated));
            Row("Sales earned",    SilverFmt.Format(me.SalesEarned),      RankOf(e => e.SalesEarned));
            Row("Silver spent",    SilverFmt.Format(me.PurchasesSpent),   RankOf(e => e.PurchasesSpent));
            Row("Quests completed", me.QuestsCompleted.ToString("N0"),    RankOf(e => e.QuestsCompleted));
            Row("Quests posted",   me.QuestsPosted.ToString("N0"),        RankOf(e => e.QuestsPosted));
            Row("Sites built",     me.SitesBuilt.ToString("N0"),          RankOf(e => e.SitesBuilt));
            Row("Worker XP",       me.WorkerXp.ToString("N0"),            RankOf(e => e.WorkerXp));

            y += 4f;
            DialogLayout.LabelTrunc(new Rect(8f, y, rect.width, 22f),
                $"<color=grey>Ranked against {all.Count} player{(all.Count == 1 ? "" : "s")}</color>");

            if (DialogLayout.DrawCloseButton(rect)) Close();
        }
    }
}
