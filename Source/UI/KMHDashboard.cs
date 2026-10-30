using System;
using System.Collections.Generic;
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
    // The row exists first and snapshots only update its text, so a late snapshot can never drop a feature.
    internal static class KMHDashboard
    {
        private static bool _buildErrorLogged;   // one-shot so a recurring section failure can't flood the log

        private const string DisabledText = "<color=#A9AFB9>disabled by server</color>";
        private const string LoadingText  = "<color=#A9AFB9>loading…</color>";
        private static bool On(string feature) => Features.KmhFeatures.IsEnabled(feature);
        private static string Me() => KmhSession.Me;

        // Status only, never navigation: nothing may be reachable from here alone.
        private readonly struct Row
        {
            public readonly string Label;
            public readonly string Feature;        // "" = always-on (no server toggle exists for it)
            public readonly Func<string> Summary;
            public readonly Func<bool> Visible;    // null = always visible
            public readonly Func<string> Tooltip;  // null = no hover detail
            public readonly Func<string> Title;    // null = Label as written
            public Row(string label, string feature, Func<string> summary, Func<bool> visible = null,
                       Func<string> tooltip = null, Func<string> title = null)
            { Label = label; Feature = feature; Summary = summary; Visible = visible; Tooltip = tooltip; Title = title; }
        }

        private static List<Row> _rows;

        // Cached so the row count, and therefore the panel height, stays stable within a frame.
        private static readonly TimeSpan RebuildInterval = TimeSpan.FromMilliseconds(250);
        private static List<(string Label, string Value, Color color, Func<string> Tip)> _cachedLines;
        private static DateTime _lastBuildUtc = DateTime.MinValue;

        // Declaration order is display order.
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
                new Row("Mail",          "mail",        MailSummary),
                new Row("Chat",          "chat",        ChatSummary),
                new Row("Federation",    "federation",  FederationSummary,
                        () => SubProtocol.KmhCapabilities.Has(SubProtocol.KmhCapabilities.Federation),
                        title: () => KmhText.Get("KMH_Fed_DashLabel", "Federation")),
                new Row("Connected Treasury", "connected", ConnectedSummary,
                        () => Features.Federation.FederationGate.TreasuryOffered,
                        title: () => KmhText.Get("KMH_Ct_ConnectedTreasury", "Connected Treasury")),
                new Row("Quests",        "quests",      QuestsSummary),
                new Row("Sites",         "",            SitesSummary),
                new Row("World Events",  "world",       WorldEventsSummary, null, WorldEventsTooltip),
                new Row("Global Quests", "world",       GlobalQuestsSummary),
                new Row("Standings",     "standings",   StandingsSummary),
                new Row("Enforcement",   "",            EnforcementSummary),
                new Row("Discord",       "",            DiscordSummary),
                new Row("Discord join",  "",            DiscordJoinSummary),
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
            var lines = BuildLines();

            // rowH must be at least Text.LineHeight: LabelTrunc grows a shorter row and it overlaps its neighbour.
            const float rowH = 23f, rowGap = 2f, panelPad = 6f;
            float panelH = panelPad * 2f + Math.Max(0, lines.Count) * (rowH + rowGap);
            Rect panelRect = listing.GetRect(panelH);
            float labelW = Mathf.Min(LabelColumnW(lines), Mathf.Max(120f, panelRect.width * 0.4f));

            GameFont prevFont = Text.Font; TextAnchor prevAnchor = Text.Anchor; Color prevColor = GUI.color;
            try
            {
                Widgets.DrawMenuSection(panelRect);
                float ly = panelRect.y + panelPad;
                List<string> shown = ShownLabels(lines);
                for (int i = 0; i < lines.Count; i++)
                {
                    var (label, value, color, tip) = lines[i];
                    try
                    {
                        Rect labelRect = new Rect(panelRect.x + panelPad, ly, labelW, rowH);
                        Rect valueRect = new Rect(panelRect.x + panelPad + labelW + 4f, ly, panelRect.width - panelPad * 2f - labelW - 4f, rowH);
                        GUI.color = color;
                        DialogLayout.LabelTrunc(labelRect, shown[i]);
                        DialogLayout.LabelTrunc(valueRect, value ?? "");

                        // A per-row uniqueId, because the string overload hashes the text and a countdown re-registers.
                        if (tip != null)
                        {
                            Rect rowRect = new Rect(labelRect.x, ly, valueRect.xMax - labelRect.x, rowH);
                            if (Mouse.IsOver(rowRect))
                                TooltipHandler.TipRegion(rowRect, new TipSignal(tip, (label ?? "kmhrow").GetHashCode()));
                        }
                    }
                    catch (Exception ex) { LogSectionOnce("row-render", ex); }
                    ly += rowH + rowGap;
                }
            }
            catch (Exception ex) { LogSectionOnce("panel-render", ex); }
            finally { Text.Font = prevFont; Text.Anchor = prevAnchor; GUI.color = prevColor; }
        }

        // Widest key, measured once per rebuilt line set: a fixed 120px cut "Connected Treasury" and longer translations.
        private static float LabelColumnW(List<(string Label, string Value, Color color, Func<string> Tip)> lines)
        {
            if (ReferenceEquals(lines, _labelWFor)) return _labelW;
            float w = 120f;
            foreach (var line in lines) w = Mathf.Max(w, Mathf.Ceil(Text.CalcSize(line.Label ?? "").x) + 8f);
            _labelWFor = lines;
            _labelW = w;
            return w;
        }

        private static object _labelWFor;
        private static float  _labelW = 120f;

        // Wrapped once per rebuilt line set, like the column width, rather than per row on every GUI event.
        internal static List<string> ShownLabels(List<(string Label, string Value, Color color, Func<string> Tip)> lines)
        {
            if (ReferenceEquals(lines, _shownFor)) return _shown;
            var shown = new List<string>(lines.Count);
            foreach (var line in lines) shown.Add($"<color=#9FB1C9>{line.Label ?? ""}</color>");
            _shownFor = lines;
            _shown = shown;
            return shown;
        }

        private static object       _shownFor;
        private static List<string> _shown;

        private static List<(string Label, string Value, Color color, Func<string> Tip)> BuildLines()
        {
            DateTime now = DateTime.UtcNow;
            if (_cachedLines != null && now - _lastBuildUtc < RebuildInterval) return _cachedLines;

            var lines = new List<(string Label, string Value, Color color, Func<string> Tip)>();

            // Compatibility is capability-based, so a build difference is information, not an update alarm.
            try
            {
                if (SubProtocol.KmhDispatcher.IsKmhServer)
                {
                    string sb  = SubProtocol.KmhDispatcher.ServerBuild;
                    string srv = string.IsNullOrEmpty(sb) ? "pre-1.1.0" : sb;
                    bool   caps = SubProtocol.KmhCapabilities.ManifestSeen;
                    string note = caps
                        ? "<color=#4cc38a>compatible</color>"
                        : "<color=#9FB1C9>legacy server - newer features hidden</color>";
                    // "Version": the panel is Colony Nexus's own, and the longer label was cut off in its key column.
                    lines.Add(("Version",
                        $"client {SubProtocol.KmhProtocol.DisplayVersion} · server {srv} · protocol v{SubProtocol.KmhProtocol.CurrentVersion} · {note}",
                        Color.white, null));
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
                    lines.Add((r.Title?.Invoke() ?? r.Label, text, Color.white, r.Tooltip));
                }
                catch (Exception ex) { LogSectionOnce(r.Label, ex); lines.Add((r.Label, "<color=#D37C7C>error - refresh</color>", Color.white, null)); }
            }

            _cachedLines = lines;
            _lastBuildUtc = now;
            return lines;
        }


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
            string owner = t.IsGuildOwned ? $"guild · {KmhDisplayText.GuildName(t.OwnerKey)}" : "personal vault";
            return $"<b>{SilverFmt.Format(t.SilverBalance)}</b>  <color=#A9AFB9>({owner})</color>";
        }

        private static string GuildSummary()
        {
            if (!GuildCache.HasSnapshot) return LoadingText;
            if (GuildCache.Guild == null || string.IsNullOrEmpty(GuildCache.Guild.Name))
                return "<color=#A9AFB9>none - create or join one in the Guild Hall</color>";
            string rank = ResolveMyRank(Me());
            string name = KmhDisplayText.GuildName(GuildCache.Guild.Name);
            return string.IsNullOrEmpty(rank) ? $"<b>{name}</b>" : $"<b>{name}</b>  <color=#A9AFB9>({rank})</color>";
        }

        private static string MarketplaceSummary()
        {
            if (!MarketplaceCache.HasSnapshot) return LoadingText;
            string me = Me(); int mine = 0; long total = 0;
            if (MarketplaceCache.Snapshot?.Listings != null && !string.IsNullOrEmpty(me))
                foreach (MarketplaceListing l in MarketplaceCache.Snapshot.Listings)
                    if (l != null && string.Equals(l.SellerUsername, me, StringComparison.OrdinalIgnoreCase)) { mine++; total += l.TotalAskingSilver(l.RemainingQty); }
            int open = MarketplaceCache.Snapshot?.Listings?.Count ?? 0;
            return mine == 0 ? $"<color=#A9AFB9>{open} listing(s) · you have 0</color>" : $"<b>{mine}</b> yours · escrow <b>{SilverFmt.Format(total)}</b> · {open} total";
        }

        private static string AuctionsSummary()
        {
            if (!Features.Auctions.AuctionCache.HasSnapshot) return LoadingText;
            var aucs = Features.Auctions.AuctionCache.Snapshot?.Auctions;
            int count = aucs?.Count ?? 0;
            if (count == 0) return "<color=#A9AFB9>0 open</color>";
            string me = Me(); int lead = 0, listed = 0;
            if (!string.IsNullOrEmpty(me))
                foreach (var a in aucs)
                {
                    if (a == null) continue;
                    if (string.Equals(a.SellerUsername, me, StringComparison.OrdinalIgnoreCase)) listed++;
                    else if (string.Equals(a.HighBidder, me, StringComparison.OrdinalIgnoreCase)) lead++;
                }
            string you = (lead > 0 || listed > 0) ? $"  <color=#A9AFB9>· you: {(lead > 0 ? $"leading {lead}" : "")}{(lead > 0 && listed > 0 ? ", " : "")}{(listed > 0 ? $"{listed} listed" : "")}</color>" : "";
            return $"<b>{count}</b> live{you}";
        }

        private static string WantSummary()
        {
            if (!Features.WantBoard.WantCache.HasSnapshot) return LoadingText;
            var wants = Features.WantBoard.WantCache.Snapshot?.Wants;
            int count = wants?.Count ?? 0;
            if (count == 0) return "<color=#A9AFB9>0 open</color>";
            string me = Me(); int mine = 0;
            if (!string.IsNullOrEmpty(me)) foreach (var w in wants) if (w != null && string.Equals(w.BuyerUsername, me, StringComparison.OrdinalIgnoreCase)) mine++;
            return mine == 0 ? $"<b>{count}</b> open" : $"<b>{count}</b> open  <color=#A9AFB9>· {mine} yours</color>";
        }

        private static string MailSummary()
        {
            if (!Features.Mail.MailCache.HasSnapshot) return LoadingText;
            int total  = Features.Mail.MailCache.Snapshot?.Messages?.Count ?? 0;
            int unread = Features.Mail.MailCache.Unread;
            if (total == 0) return "<color=#A9AFB9>empty</color>";
            return unread > 0
                ? $"<b><color={KmhTheme.Hex(KmhTheme.Accent)}>{unread} unread</color></b>  <color=#A9AFB9>· {total} total</color>"
                : $"<color=#A9AFB9>{total} message(s)</color>";
        }

        // Whether the row for a feature would be drawn at all right now, read off the same list the panel draws.
        internal static bool RowShownFor(string feature)
        {
            foreach (Row r in Rows())
                if (r.Feature == feature) return r.Visible == null || r.Visible();
            return false;
        }

        // Membership only: the treasury has a row of its own, and that one is hidden while "connected" is off.
        private static string FederationSummary()
        {
            if (!On("guilds")) return DisabledText;
            var s = Features.Federation.FederationCache.Snapshot;
            if (s == null) return LoadingText;
            if (!s.InGuild) return "<color=#A9AFB9>" + KmhText.Get("KMH_Fed_DashNoGuild", "join a guild first") + "</color>";
            if (!Features.Federation.FederationCache.IsLiveLink(s))
                return "<color=#A9AFB9>" + KmhText.Get("KMH_Fed_DashNotLinked", "your guild is not in a federation") + "</color>";

            var f = s.Federation;
            string tag = Features.Federation.FederationCache.TagMarkup(f);
            string head = "<b>" + (tag.Length > 0 ? tag + " " : "") + f.Name + "</b>";
            string tail = KmhText.Get("KMH_Fed_DashLevel", "level {0} · {1} member server(s)", f.Level, f.ActiveMembers);
            if (s.Stale || !s.Available) tail += " · " + KmhText.Get("KMH_Fed_DashStale", "last known");
            return head + "  <color=#A9AFB9>" + tail + "</color>";
        }

        // Pending work first: a player who deposited wants to know it has not landed yet, before a total.
        private static string ConnectedSummary()
        {
            var c = Features.ConnectedTreasury.ConnectedTreasuryCache.Snapshot;
            if (c == null) return "<color=#A9AFB9>not loaded yet</color>";
            if (!c.Linked) return "<color=#A9AFB9>no Colony Nexus account linked</color>";
            if (!c.Available) return "<color=#A9AFB9>Colony Nexus Global unreachable</color>";

            long pin = c.PendingIn, pout = c.PendingOut;
            if (pin > 0 || pout > 0)
                return $"<b><color={KmhTheme.Hex(KmhTheme.Accent)}>{pin + pout:N0} pending</color></b>  <color=#A9AFB9>· federated silver</color>";

            var b = Features.ConnectedTreasury.ConnectedTreasuryCache.Balance;
            return b == null
                ? "<color=#A9AFB9>balance not read yet</color>"
                : $"<color=#A9AFB9>{b.Available:N0} available · federated silver</color>";
        }

        private static string ChatSummary()
        {
            int unread = Features.Chat.ChatCache.TotalUnread();
            if (unread > 0) return $"<b><color={KmhTheme.Hex(KmhTheme.Accent)}>{unread} unread</color></b>  <color=#A9AFB9>· server channel</color>";
            int have = Features.Chat.ChatCache.Recent(Features.Chat.ChatCache.ServerChannel).Count;
            return have > 0 ? $"<color=#A9AFB9>{have} recent · server channel</color>" : "<color=#A9AFB9>quiet · open to say hello</color>";
        }

        private static string QuestsSummary()
        {
            if (!QuestCache.HasSnapshot) return LoadingText;
            var (open, inFlight, posted, review) = QuestCounts(QuestCache.Snapshot?.Quests, Me());
            return QuestsLine(open, inFlight, posted, review);
        }

        // What needs the player leads the line, because a narrow panel truncates its end.
        internal static string QuestsLine(int open, int inFlight, int posted, int review)
        {
            string counts = $"<b>{open}</b> open · <b>{inFlight}</b> mine in flight · <b>{posted}</b> posted by me";
            return review > 0
                ? $"<b><color={KmhTheme.Hex(KmhTheme.Accent)}>{review} awaiting your review</color></b> · {counts}"
                : counts;
        }

        // A proof waiting on the poster is still that poster's live quest, and the one thing on the board they must act on.
        internal static (int Open, int InFlight, int Posted, int AwaitingReview) QuestCounts(IEnumerable<QuestEntry> quests, string me)
        {
            int open = 0, inFlight = 0, posted = 0, review = 0;
            if (quests == null) return (0, 0, 0, 0);
            foreach (QuestEntry q in quests)
            {
                if (q == null) continue;
                if (q.State == QuestEntry.StateOpen) open++;
                if (string.IsNullOrEmpty(me)) continue;
                bool live = q.State == QuestEntry.StateClaimed || q.State == QuestEntry.StateSubmitted || q.State == QuestEntry.StatePendingReview;
                if (string.Equals(q.ClaimedByUsername, me, StringComparison.OrdinalIgnoreCase) && live) inFlight++;
                if (!string.Equals(q.PosterUsername, me, StringComparison.OrdinalIgnoreCase)) continue;
                if (q.State == QuestEntry.StateOpen || live) posted++;
                if (q.State == QuestEntry.StatePendingReview || (q.State == QuestEntry.StateSubmitted && q.Kind == QuestEntry.KindBounty)) review++;
            }
            return (open, inFlight, posted, review);
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
            // ActiveEvents, not Snapshot.Events: a stale snapshot still lists events whose end time has passed.
            var ev = Features.World.WorldCache.ActiveEvents();
            if (ev.Count == 0) return "<color=#A9AFB9>none active - waiting for next roll</color>";
            return ev.Count == 1
                ? $"<b><color=#4cc38a>{Features.World.WorldEventText.Title(ev[0])}</color></b>  <color=#A9AFB9>{ev[0].Description}</color>"
                : $"<b><color=#4cc38a>{ev.Count} active</color></b>  <color=#A9AFB9>{string.Join(", ", ev.ConvertAll(Features.World.WorldEventText.Title))}</color>";
        }

        private static string WorldEventsTooltip()
        {
            if (!Features.World.WorldCache.HasSnapshot) return "Waiting for the server…";
            var ev = Features.World.WorldCache.ActiveEvents();
            if (ev.Count == 0) return "No events are active right now.";

            var sb = new System.Text.StringBuilder();
            foreach (var e in ev)
            {
                if (sb.Length > 0) sb.Append("\n\n");
                string left = Features.World.WorldEventText.Remaining(e);
                sb.Append(Features.World.WorldEventText.Title(e));
                if (left.Length > 0) sb.Append("  (").Append(left).Append(" left)");
                string desc = (e.Description ?? "").Trim();
                if (desc.Length > 0) sb.Append('\n').Append(desc);
            }
            sb.Append("\n\nFull details in the World window.");
            return sb.ToString();
        }

        private static string GlobalQuestsSummary()
        {
            if (!Features.World.WorldCache.HasSnapshot) return LoadingText;
            var qs = Features.World.WorldCache.ActiveServerQuests();
            if (qs.Count == 0) return "<color=#A9AFB9>0 active</color>";
            if (qs.Count == 1)
            {
                var q = qs[0]; string me = Me();
                string pool = q.RewardPool > 0 ? $" · Pool <b>{SilverFmt.Format(q.RewardPool)}</b>" : "";
                string time = q.EndsUtcTicks > 0 ? $" · {DialogLayout.TimeLeft(q.EndsUtcTicks, DateTime.UtcNow.Ticks)}" : "";
                int mine = (q.Contributors != null && !string.IsNullOrEmpty(me) && q.Contributors.TryGetValue(me, out int c)) ? c : 0;
                string you = mine > 0 ? $"  <color=#82acff>you: {mine}</color>" : "";
                return $"<b><color=#f0c04f>{q.Title}</color></b>  <color=#A9AFB9>{q.ProgressQty}/{q.GoalQty} {q.TargetDefName}{pool}{time}</color>{you}";
            }
            return $"<b><color=#f0c04f>{qs.Count} active</color></b>  <color=#A9AFB9>{string.Join(", ", qs.ConvertAll(q => q.Title))}</color>";
        }

        private static string StandingsSummary()
        {
            var e = Features.PlayerStats.PlayerStatsCache.Entries;
            if (e == null) return LoadingText;
            return e.Count == 0 ? "<color=#A9AFB9>ready - no players ranked yet</color>" : $"<b>{e.Count}</b> player(s) ranked · open Server Standings";
        }

        private static string EnforcementSummary()
        {
            if (!Features.Enforcement.EnforcementCache.HasProfile && !Features.Enforcement.EnforcementCache.Enabled)
                return "<color=#A9AFB9>disabled by server</color>";
            if (!Features.Enforcement.EnforcementCache.Enabled) return "<color=#A9AFB9>off (profile present)</color>";
            return Features.Enforcement.EnforcementCache.IsLockActive()
                ? "<color=#f0c04f>active</color>"
                : "<color=#4cc38a>unlocked (admin)</color>";
        }

        // Linked account, shown status and Join offer are separate facts, or a missing Join goes unexplained.
        private static string DiscordSummary()
        {
            string link = LinkSummary();
            return link + "  <color=#A9AFB9>·</color>  " + Features.Discord.KmhDiscordStatus.PresenceLine();
        }

        private static string LinkSummary()
        {
            if (Features.Account.KmhDiscordLinkClient.Linked)
            {
                string name = Features.Account.KmhDiscordLinkClient.DisplayName;
                return name.Length > 0 ? $"linked as <b>{KmhDisplayText.Inert(name, 48)}</b>" : "linked";
            }

            // The per-server bot link is a different thing and still worth naming where it exists.
            string me = Me();
            if (LinkedAccountsCache.HasSnapshot && !string.IsNullOrEmpty(me) && LinkedAccountsCache.IsLinked(me))
                return $"this server: <b>{KmhDisplayText.Inert(LinkedAccountsCache.DiscordNameFor(me) ?? "(unknown)", 48)}</b>";

            return "<color=#A9AFB9>not linked</color>";
        }

        private static string DiscordJoinSummary() => Features.Discord.KmhDiscordStatus.JoinLine();

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
