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
    // Read-only KMH dashboard for the main tab. Shows cached server/player stats at a glance without making network calls.
    internal static class KMHDashboard
    {
        // Draw labels and values as one two-column block so everything lines up cleanly.
        public static void Draw(Listing_Standard listing, Rect parentRect)
        {
            string me = SessionHandler.Username ?? "";

            // Build the dashboard lines first, skipping empty cache data so first-connect stays clean.
            List<(string Label, string Value, Color color)> lines = new List<(string, string, Color)>();

            // Only when the player opted into the API - default chat players don't need a "fallback" label.
            if (KMHPatchMod.Settings?.UseKmhApiTransport == true)
            {
                string c;
                switch (SubProtocol.KmhTransport.Status)
                {
                    case SubProtocol.KmhTransportStatus.ApiConnected:    c = "#7CD37C"; break;
                    case SubProtocol.KmhTransportStatus.ApiConnecting:   c = "#E2C16B"; break;
                    case SubProtocol.KmhTransportStatus.ChatFallback:    c = "#E2C16B"; break;
                    case SubProtocol.KmhTransportStatus.VersionMismatch:
                    case SubProtocol.KmhTransportStatus.AuthFailed:      c = "#D37C7C"; break;
                    default:                                             c = "grey";    break;
                }
                lines.Add(("Transport", $"<color={c}>{SubProtocol.KmhTransport.StatusLabel}</color>", Color.white));
            }

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

            // Quest counts stay visible so the dashboard height doesn't jump while caches load.
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

            // Marketplace counts stay visible: your listings and their total value.
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

            // Discord link status, always shown.
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

            // Show world events only while one is active, keeping the dashboard quiet otherwise.
            if (Features.World.WorldCache.HasEvents)
            {
                var ev = Features.World.WorldCache.Snapshot.Events;
                string text = ev.Count == 1
                    ? $"<b><color=#7CD37C>{ev[0].Title}</color></b>  <color=grey>{ev[0].Description}</color>"
                    : $"<b><color=#7CD37C>{ev.Count} active</color></b>  <color=grey>{string.Join(", ", ev.ConvertAll(e => e.Title))}</color>";
                lines.Add(("Events", text, Color.white));
            }

            // Show active global quests with live progress and reward.
            if (Features.World.WorldCache.HasServerQuests)
            {
                var qs = Features.World.WorldCache.ActiveServerQuests();
                string text;
                if (qs.Count == 1)
                {
                    var q = qs[0];
                    string reward = q.RewardPool > 0 ? $" · <b>{q.RewardPool:N0}s</b>" : "";
                    text = $"<b><color=#E2C16B>{q.Title}</color></b>  <color=grey>{q.ProgressQty}/{q.GoalQty} {q.TargetDefName}{reward}</color>";
                }
                else
                {
                    text = $"<b><color=#E2C16B>{qs.Count} active</color></b>  <color=grey>{string.Join(", ", qs.ConvertAll(q => q.Title))}</color>";
                }
                lines.Add(("Global Quests", text, Color.white));
            }

            // Show live auctions only, with your own bids/listings called out.
            if (Features.Auctions.AuctionCache.HasSnapshot
                && Features.Auctions.AuctionCache.Snapshot.Auctions != null
                && Features.Auctions.AuctionCache.Snapshot.Auctions.Count > 0)
            {
                var aucs = Features.Auctions.AuctionCache.Snapshot.Auctions;
                int myLead = 0, myListed = 0;
                if (!string.IsNullOrEmpty(me))
                    foreach (var a in aucs)
                    {
                        if (a == null) continue;
                        if (string.Equals(a.SellerUsername, me, System.StringComparison.OrdinalIgnoreCase)) myListed++;
                        else if (string.Equals(a.HighBidder, me, System.StringComparison.OrdinalIgnoreCase)) myLead++;
                    }
                string mine = (myLead > 0 || myListed > 0)
                    ? $"  <color=grey>· you: {(myLead > 0 ? $"leading {myLead}" : "")}{(myLead > 0 && myListed > 0 ? ", " : "")}{(myListed > 0 ? $"{myListed} listed" : "")}</color>"
                    : "";
                lines.Add(("Auctions", $"<b>{aucs.Count}</b> live{mine}", Color.white));
            }

            // Render as two columns with a fixed label width so everything lines up cleanly.
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

        // Find the caller's guild rank, or empty if this snapshot doesn't include them.
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
