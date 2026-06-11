using System;
using System.Collections.Generic;
using GameClient.Misc;
using KMHPatch.Features.Guilds;
using KMHPatch.Features.LinkedAccounts;
using KMHPatch.Features.Marketplace;
using KMHPatch.Features.Marketplace.Dto;
using KMHPatch.Features.Quests;
using KMHPatch.Features.Quests.Dto;
using KMHPatch.Features.Treasury;
using UnityEngine;
using Verse;

namespace KMHPatch.UI
{
    // Live "at a glance" dashboard rendered on the main KMH tab. Pulls stats from the per-feature caches and lays
    // them out as a compact two-column status grid so the player sees what's happening on the server without having
    // to open every feature dialog
    //
    // Stats covered (each line skips when its source cache is empty):
    //   - Treasury silver + owner label
    //   - Quests claimable on the board / your in-flight claims /
    //     your posted quests still active
    //   - Your marketplace listings (count + total escrowed value)
    //   - Guild membership (name + rank, or "none")
    //   - Discord link status (yes + handle, or "not linked")
    //
    // Pure read-only - never triggers network traffic. The caches refresh themselves on their own cadence; the
    // dashboard just observes them
    internal static class KMHDashboard
    {
        // Two columns side-by-side: left = label, right = value. Drawn as a single block so the labels and values
        // align without each line having to compute its own widths
        public static void Draw(Listing_Standard listing, Rect parentRect)
        {
            string me = SessionHandler.Username ?? "";

            // Gather every line in one pass, then render in one block - skipping empty/unknowns keeps the dashboard
            // tight on first-connect when caches haven't filled yet
            List<(string Label, string Value, Color color)> lines = new List<(string, string, Color)>();

            // Treasury
            if (TreasuryCache.HasSnapshot)
            {
                var t = TreasuryCache.Snapshot;
                string ownerLabel = t.IsGuildOwned ? $"guild · {t.OwnerKey}" : "personal vault";
                lines.Add(("Treasury",
                           $"<b>{t.SilverBalance:N0}s</b>  <color=grey>({ownerLabel})</color>",
                           Color.white));
            }
            else
            {
                lines.Add(("Treasury", "<color=grey>(loading…)</color>", Color.white));
            }

            // Guild
            if (GuildCache.HasSnapshot && GuildCache.Guild != null && !string.IsNullOrEmpty(GuildCache.Guild.Name))
            {
                string rank = ResolveMyRank(me);
                lines.Add(("Guild",
                           string.IsNullOrEmpty(rank)
                               ? $"<b>{GuildCache.Guild.Name}</b>"
                               : $"<b>{GuildCache.Guild.Name}</b>  <color=grey>({rank})</color>",
                           Color.white));
            }
            else
            {
                lines.Add(("Guild",
                           "<color=grey>none - /kmh guild create &lt;name&gt; or join one</color>",
                           Color.white));
            }

            // quests - board total + my claims + my posts; always rendered so the dashboard height stays constant
            // while caches fill
            {
                string text;
                if (!QuestCache.HasSnapshot)
                {
                    text = "<color=grey>(loading…)</color>";
                }
                else
                {
                    int openOnBoard   = 0;
                    int myInFlight    = 0;   // I claimed it, not yet submitted/completed
                    int myPostedAlive = 0;   // I posted it, still in play
                    if (QuestCache.Snapshot?.Quests != null)
                    {
                        foreach (QuestEntry q in QuestCache.Snapshot.Quests)
                        {
                            if (q == null) continue;
                            if (q.State == QuestEntry.StateOpen) openOnBoard++;
                            if (!string.IsNullOrEmpty(me))
                            {
                                if (string.Equals(q.ClaimedByUsername, me, StringComparison.OrdinalIgnoreCase)
                                    && (q.State == QuestEntry.StateClaimed
                                     || q.State == QuestEntry.StateSubmitted))
                                    myInFlight++;
                                if (string.Equals(q.PosterUsername, me, StringComparison.OrdinalIgnoreCase)
                                    && (q.State == QuestEntry.StateOpen
                                     || q.State == QuestEntry.StateClaimed
                                     || q.State == QuestEntry.StateSubmitted))
                                    myPostedAlive++;
                            }
                        }
                    }
                    text = $"<b>{openOnBoard}</b> open · <b>{myInFlight}</b> mine in flight · <b>{myPostedAlive}</b> posted by me";
                }
                lines.Add(("Quests", text, Color.white));
            }

            // Marketplace - my listings count + total value (always rendered).
            {
                string text;
                if (!MarketplaceCache.HasSnapshot)
                {
                    text = "<color=grey>(loading…)</color>";
                }
                else
                {
                    int  myCount = 0;
                    long myTotal = 0;
                    if (MarketplaceCache.Snapshot?.Listings != null && !string.IsNullOrEmpty(me))
                    {
                        foreach (MarketplaceListing l in MarketplaceCache.Snapshot.Listings)
                        {
                            if (l == null) continue;
                            if (string.Equals(l.SellerUsername, me, StringComparison.OrdinalIgnoreCase))
                            {
                                myCount += 1;
                                myTotal += (long)l.RemainingQty * l.UnitPriceSilver;
                            }
                        }
                    }
                    text = myCount == 0
                        ? "<color=grey>0 listings - post one from a selected caravan</color>"
                        : $"<b>{myCount}</b> active · escrow value <b>{myTotal:N0}s</b>";
                }
                lines.Add(("Marketplace", text, Color.white));
            }

            // Discord link (always rendered).
            {
                string text;
                if (!LinkedAccountsCache.HasSnapshot || string.IsNullOrEmpty(me))
                    text = "<color=grey>(loading…)</color>";
                else if (LinkedAccountsCache.IsLinked(me))
                    text = $"linked to <b>{LinkedAccountsCache.DiscordNameFor(me) ?? "(unknown)"}</b>";
                else
                    text = "<color=grey>not linked - /kmh link to get a code</color>";
                lines.Add(("Discord", text, Color.white));
            }

            // Render. Two-column layout with fixed left column width so every label lines up regardless of value
            // width
            const float labelW    = 120f;
            const float rowH      = 22f;
            const float rowGap    = 2f;
            const float panelPad  = 8f;
            float       panelH    = panelPad * 2f + lines.Count * (rowH + rowGap);
            Rect        panelRect = listing.GetRect(panelH);

            Widgets.DrawMenuSection(panelRect);
            float ly = panelRect.y + panelPad;
            foreach (var (label, value, color) in lines)
            {
                Rect labelRect = new Rect(panelRect.x + panelPad, ly, labelW, rowH);
                Rect valueRect = new Rect(panelRect.x + panelPad + labelW + 4f, ly,
                                          panelRect.width - panelPad * 2f - labelW - 4f, rowH);
                Color prev = GUI.color;
                GUI.color = color;
                DialogLayout.LabelTrunc(labelRect, $"<color=#9FB1C9>{label}</color>");
                DialogLayout.LabelTrunc(valueRect, value);
                GUI.color = prev;
                ly += rowH + rowGap;
            }
        }

        // Walk the guild member list looking for the caller's rank label. Returns empty when the guild snapshot
        // doesn't include the caller (shouldn't happen in v1 - server scopes the snapshot to the caller's guild -
        // but guards against future cross-guild snapshots)
        private static string ResolveMyRank(string me)
        {
            if (string.IsNullOrEmpty(me)) return "";
            var g = GuildCache.Guild;
            if (g?.Members == null) return "";
            foreach (var m in g.Members)
            {
                if (m != null && string.Equals(m.Username, me, StringComparison.OrdinalIgnoreCase))
                {
                    return m.Rank ?? "";
                }
            }
            return "";
        }
    }
}
