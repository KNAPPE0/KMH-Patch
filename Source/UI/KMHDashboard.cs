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
    // Registry-driven KMH dashboard. Every enabled feature has a ROW that is ALWAYS rendered (loading/empty/data/
    // disabled/stale) - the row exists first, snapshots only update its text. A feature can never go missing because
    // its snapshot arrived late or not at all (the old snapshot-driven build dropped rows with no cached data).
    internal static class KMHDashboard
    {
        private static bool _buildErrorLogged;   // one-shot so a recurring section failure can't flood the log

        private const string DisabledText = "<color=grey>disabled by server</color>";
        private const string LoadingText  = "<color=grey>loading…</color>";
        private static bool On(string feature) => Features.KmhFeatures.IsEnabled(feature);
        private static string Me() => SessionHandler.Username ?? "";

        // A dashboard row. Summary returns the current text (it decides loading/empty/data/stale). The server-disabled
        // state is handled centrally from Feature. Visible is an extra gate (e.g. Transport needs the local setting).
        private readonly struct Row
        {
            public readonly string Label;
            public readonly string Feature;        // "" = always-on (no server toggle exists for it)
            public readonly Func<string> Summary;
            public readonly Func<bool> Visible;    // null = always visible
            public Row(string label, string feature, Func<string> summary, Func<bool> visible = null)
            { Label = label; Feature = feature; Summary = summary; Visible = visible; }
        }

        private static List<Row> _rows;

        // Built once (the set of features is fixed for the mod). Order = display order.
        private static List<Row> Rows()
        {
            if (_rows != null) return _rows;
            _rows = new List<Row>
            {
                new Row("Transport",     "",            TransportSummary, () => KMHPatchMod.Settings?.UseKmhApiTransport == true),
                new Row("Treasury",      "treasury",    TreasurySummary),
                new Row("Guild",         "guilds",      GuildSummary),
                new Row("Marketplace",   "marketplace", MarketplaceSummary),
                new Row("Auctions",      "auctions",    AuctionsSummary),
                new Row("Want Board",    "wantboard",   WantSummary),
                new Row("Quests",        "quests",      QuestsSummary),
                new Row("Sites",         "",            SitesSummary),
                new Row("World Events",  "world",       WorldEventsSummary),
                new Row("Global Quests", "world",       GlobalQuestsSummary),
                new Row("Standings",     "standings",   StandingsSummary),
                new Row("Enforcement",   "",            EnforcementSummary),
                new Row("Discord",       "",            DiscordSummary),
            };
            if (Diagnostics.KmhLog.DebugEnabled)
            {
                Diagnostics.KmhLog.Debug($"KMH dashboard registry initialized with {_rows.Count} rows.");
                foreach (Row r in _rows) Diagnostics.KmhLog.Debug($"  dashboard row: {r.Label}");
            }
            return _rows;
        }

        public static void Draw(Listing_Standard listing, Rect parentRect)
        {
            List<(string Label, string Value, Color color)> lines = new List<(string, string, Color)>();

            // Persistent version-mismatch notice (kept above the feature rows).
            try
            {
                if (SubProtocol.KmhDispatcher.IsKmhServer)
                {
                    string sb = SubProtocol.KmhDispatcher.ServerBuild;
                    if (string.IsNullOrEmpty(sb))
                        lines.Add(("KMH version", "<color=#E2C16B>server is pre-1.1.0 - newer features hidden until it updates</color>", Color.white));
                    else if (sb != SubProtocol.KmhProtocol.BuildVersion)
                        lines.Add(("KMH version", $"<color=#E2C16B>server {sb} vs your mod {SubProtocol.KmhProtocol.BuildVersion} - update so both match</color>", Color.white));
                }
            }
            catch (Exception ex) { LogSectionOnce("version", ex); }

            // Every row is rendered - a per-row failure yields an "error" cell, never a missing row.
            foreach (Row r in Rows())
            {
                try
                {
                    if (r.Visible != null && !r.Visible()) continue;
                    string text = !string.IsNullOrEmpty(r.Feature) && !On(r.Feature) ? DisabledText : (r.Summary() ?? LoadingText);
                    lines.Add((r.Label, text, Color.white));
                }
                catch (Exception ex) { LogSectionOnce(r.Label, ex); lines.Add((r.Label, "<color=#D37C7C>error - refresh</color>", Color.white)); }
            }

            // Render as two aligned columns. The loop restores GUI state so one bad row can't corrupt the panel/buttons.
            // rowH must be >= Text.LineHeight (22) - LabelTrunc grows shorter rows to a full line, so 20/1 made
            // neighboring rows overlap ("text bleeding"); the tab body scrolls now, so no need to squeeze.
            const float labelW = 120f, rowH = 23f, rowGap = 2f, panelPad = 6f;
            float panelH = panelPad * 2f + Math.Max(0, lines.Count) * (rowH + rowGap);
            Rect panelRect = listing.GetRect(panelH);

            GameFont prevFont = Text.Font; TextAnchor prevAnchor = Text.Anchor; Color prevColor = GUI.color;
            try
            {
                Widgets.DrawMenuSection(panelRect);
                float ly = panelRect.y + panelPad;
                foreach (var (label, value, color) in lines)
                {
                    try
                    {
                        Rect labelRect = new Rect(panelRect.x + panelPad, ly, labelW, rowH);
                        Rect valueRect = new Rect(panelRect.x + panelPad + labelW + 4f, ly, panelRect.width - panelPad * 2f - labelW - 4f, rowH);
                        GUI.color = color;
                        DialogLayout.LabelTrunc(labelRect, $"<color=#9FB1C9>{label ?? ""}</color>");
                        DialogLayout.LabelTrunc(valueRect, value ?? "");
                    }
                    catch (Exception ex) { LogSectionOnce("row-render", ex); }
                    ly += rowH + rowGap;
                }
            }
            catch (Exception ex) { LogSectionOnce("panel-render", ex); }
            finally { Text.Font = prevFont; Text.Anchor = prevAnchor; GUI.color = prevColor; }
        }

        // --- per-feature summary providers (each handles loading / empty / data; "" -> caller shows loading) ---

        private static string TransportSummary()
        {
            string c;
            switch (SubProtocol.KmhTransport.Status)
            {
                case SubProtocol.KmhTransportStatus.ApiConnected:  c = "#7CD37C"; break;
                case SubProtocol.KmhTransportStatus.ApiConnecting: c = "#E2C16B"; break;
                case SubProtocol.KmhTransportStatus.ChatFallback:  c = "#E2C16B"; break;
                case SubProtocol.KmhTransportStatus.VersionMismatch:
                case SubProtocol.KmhTransportStatus.AuthFailed:    c = "#D37C7C"; break;
                default:                                           c = "grey";    break;
            }
            return $"<color={c}>{SubProtocol.KmhTransport.StatusLabel}</color>";
        }

        private static string TreasurySummary()
        {
            if (!TreasuryCache.HasSnapshot) return LoadingText;
            var t = TreasuryCache.Snapshot;
            string owner = t.IsGuildOwned ? $"guild · {t.OwnerKey}" : "personal vault";
            return $"<b>{SilverFmt.Format(t.SilverBalance)}</b>  <color=grey>({owner})</color>";
        }

        private static string GuildSummary()
        {
            if (!GuildCache.HasSnapshot) return LoadingText;
            if (GuildCache.Guild == null || string.IsNullOrEmpty(GuildCache.Guild.Name))
                return "<color=grey>none - create or join one in the Guild Hall</color>";
            string rank = ResolveMyRank(Me());
            return string.IsNullOrEmpty(rank) ? $"<b>{GuildCache.Guild.Name}</b>" : $"<b>{GuildCache.Guild.Name}</b>  <color=grey>({rank})</color>";
        }

        private static string MarketplaceSummary()
        {
            if (!MarketplaceCache.HasSnapshot) return LoadingText;
            string me = Me(); int mine = 0; long total = 0;
            if (MarketplaceCache.Snapshot?.Listings != null && !string.IsNullOrEmpty(me))
                foreach (MarketplaceListing l in MarketplaceCache.Snapshot.Listings)
                    if (l != null && string.Equals(l.SellerUsername, me, StringComparison.OrdinalIgnoreCase)) { mine++; total += (long)l.RemainingQty * l.UnitPriceSilver; }
            int open = MarketplaceCache.Snapshot?.Listings?.Count ?? 0;
            return mine == 0 ? $"<color=grey>{open} listing(s) · you have 0</color>" : $"<b>{mine}</b> yours · escrow <b>{SilverFmt.Format(total)}</b> · {open} total";
        }

        private static string AuctionsSummary()
        {
            if (!Features.Auctions.AuctionCache.HasSnapshot) return LoadingText;
            var aucs = Features.Auctions.AuctionCache.Snapshot?.Auctions;
            int count = aucs?.Count ?? 0;
            if (count == 0) return "<color=grey>0 open</color>";
            string me = Me(); int lead = 0, listed = 0;
            if (!string.IsNullOrEmpty(me))
                foreach (var a in aucs)
                {
                    if (a == null) continue;
                    if (string.Equals(a.SellerUsername, me, StringComparison.OrdinalIgnoreCase)) listed++;
                    else if (string.Equals(a.HighBidder, me, StringComparison.OrdinalIgnoreCase)) lead++;
                }
            string you = (lead > 0 || listed > 0) ? $"  <color=grey>· you: {(lead > 0 ? $"leading {lead}" : "")}{(lead > 0 && listed > 0 ? ", " : "")}{(listed > 0 ? $"{listed} listed" : "")}</color>" : "";
            return $"<b>{count}</b> live{you}";
        }

        private static string WantSummary()
        {
            if (!Features.WantBoard.WantCache.HasSnapshot) return LoadingText;
            var wants = Features.WantBoard.WantCache.Snapshot?.Wants;
            int count = wants?.Count ?? 0;
            if (count == 0) return "<color=grey>0 open</color>";
            string me = Me(); int mine = 0;
            if (!string.IsNullOrEmpty(me)) foreach (var w in wants) if (w != null && string.Equals(w.BuyerUsername, me, StringComparison.OrdinalIgnoreCase)) mine++;
            return mine == 0 ? $"<b>{count}</b> open" : $"<b>{count}</b> open  <color=grey>· {mine} yours</color>";
        }

        private static string QuestsSummary()
        {
            if (!QuestCache.HasSnapshot) return LoadingText;
            string me = Me(); int open = 0, inFlight = 0, posted = 0;
            if (QuestCache.Snapshot?.Quests != null)
                foreach (QuestEntry q in QuestCache.Snapshot.Quests)
                {
                    if (q == null) continue;
                    if (q.State == QuestEntry.StateOpen) open++;
                    if (!string.IsNullOrEmpty(me))
                    {
                        if (string.Equals(q.ClaimedByUsername, me, StringComparison.OrdinalIgnoreCase) && (q.State == QuestEntry.StateClaimed || q.State == QuestEntry.StateSubmitted)) inFlight++;
                        if (string.Equals(q.PosterUsername, me, StringComparison.OrdinalIgnoreCase) && (q.State == QuestEntry.StateOpen || q.State == QuestEntry.StateClaimed || q.State == QuestEntry.StateSubmitted)) posted++;
                    }
                }
            return $"<b>{open}</b> open · <b>{inFlight}</b> mine in flight · <b>{posted}</b> posted by me";
        }

        private static string SitesSummary()
        {
            if (!Features.Sites.SiteCache.HasSnapshot) return LoadingText;
            var sites = Features.Sites.SiteCache.Snapshot?.Sites;
            string me = Me(); int owned = 0, worked = 0;
            if (sites != null)
                foreach (var s in sites)
                {
                    if (s == null) continue;
                    if (string.Equals(s.OwnerUsername, me, StringComparison.OrdinalIgnoreCase)) owned++;
                    if (s.Workers != null && !string.IsNullOrEmpty(me) && s.Workers.Exists(w => string.Equals(w, me, StringComparison.OrdinalIgnoreCase))) worked++;
                }
            return $"<b>{owned}</b> owned · <b>{worked}</b> worked";
        }

        private static string WorldEventsSummary()
        {
            if (!Features.World.WorldCache.HasSnapshot) return LoadingText;
            if (!Features.World.WorldCache.HasEvents) return "<color=grey>none active - waiting for next roll</color>";
            var ev = Features.World.WorldCache.Snapshot.Events;
            return ev.Count == 1
                ? $"<b><color=#7CD37C>{ev[0].Title}</color></b>  <color=grey>{ev[0].Description}</color>"
                : $"<b><color=#7CD37C>{ev.Count} active</color></b>  <color=grey>{string.Join(", ", ev.ConvertAll(e => e.Title))}</color>";
        }

        private static string GlobalQuestsSummary()
        {
            if (!Features.World.WorldCache.HasSnapshot) return LoadingText;
            var qs = Features.World.WorldCache.ActiveServerQuests();
            if (qs.Count == 0) return "<color=grey>0 active</color>";
            if (qs.Count == 1)
            {
                var q = qs[0]; string me = Me();
                string pool = q.RewardPool > 0 ? $" · Pool <b>{SilverFmt.Format(q.RewardPool)}</b>" : "";
                string time = q.EndsUtcTicks > 0 ? $" · {DialogLayout.TimeLeft(q.EndsUtcTicks, DateTime.UtcNow.Ticks)}" : "";
                int mine = (q.Contributors != null && !string.IsNullOrEmpty(me) && q.Contributors.TryGetValue(me, out int c)) ? c : 0;
                string you = mine > 0 ? $"  <color=#79b8ff>you: {mine}</color>" : "";
                return $"<b><color=#E2C16B>{q.Title}</color></b>  <color=grey>{q.ProgressQty}/{q.GoalQty} {q.TargetDefName}{pool}{time}</color>{you}";
            }
            return $"<b><color=#E2C16B>{qs.Count} active</color></b>  <color=grey>{string.Join(", ", qs.ConvertAll(q => q.Title))}</color>";
        }

        private static string StandingsSummary()
        {
            var e = Features.PlayerStats.PlayerStatsCache.Entries;
            if (e == null) return LoadingText;
            return e.Count == 0 ? "<color=grey>ready - no players ranked yet</color>" : $"<b>{e.Count}</b> player(s) ranked · open Server Standings";
        }

        private static string EnforcementSummary()
        {
            if (!Features.Enforcement.EnforcementCache.HasProfile && !Features.Enforcement.EnforcementCache.Enabled)
                return "<color=grey>disabled by server</color>";
            if (!Features.Enforcement.EnforcementCache.Enabled) return "<color=grey>off (profile present)</color>";
            return Features.Enforcement.EnforcementCache.IsLockActive()
                ? "<color=#E2C16B>active</color>"
                : "<color=#7CD37C>unlocked (admin)</color>";
        }

        private static string DiscordSummary()
        {
            string me = Me();
            if (!LinkedAccountsCache.HasSnapshot || string.IsNullOrEmpty(me)) return LoadingText;
            return LinkedAccountsCache.IsLinked(me)
                ? $"linked to <b>{LinkedAccountsCache.DiscordNameFor(me) ?? "(unknown)"}</b>"
                : "<color=grey>not linked - use the Link Discord button</color>";
        }

        private static void LogSectionOnce(string section, Exception ex)
        {
            if (_buildErrorLogged) return;
            _buildErrorLogged = true;
            Diagnostics.KmhLog.Warn($"KMH dashboard '{section}' row failed (rest still renders): {ex.Message}");
        }

        private static string ResolveMyRank(string me)
        {
            if (string.IsNullOrEmpty(me)) return "";
            var g = GuildCache.Guild;
            if (g?.Members == null) return "";
            foreach (var m in g.Members)
                if (m != null && string.Equals(m.Username, me, StringComparison.OrdinalIgnoreCase)) return m.Rank ?? "";
            return "";
        }
    }
}
