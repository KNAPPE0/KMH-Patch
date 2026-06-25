using System;
using System.Collections.Generic;
using GameClient.Misc;
using KMHPatch.Features.Guilds.Dto;
using KMHPatch.Features.LinkedAccounts;
using KMHPatch.UI;
using UnityEngine;
using Verse;

namespace KMHPatch.Features.Guilds
{
    // Guild Hall dialog for members, perks, MOTD, settings, and diplomacy; guild rankings live in Server Standings.
    public class Dialog_KMHGuildHall : Window_KMHBase
    {
        public override Vector2 InitialSize => new Vector2(940f, 640f);

        private Vector2  _membersScroll;
        private Vector2  _perksScroll;
        private float    _refreshTimer   = DialogLayout.AutoRefreshSeconds;
        private DateTime _lastRefreshUtc = DateTime.UtcNow;

        private List<GuildMemberDto> _orderedMembers;
        private object               _orderedMembersSource;

        public Dialog_KMHGuildHall()
        {
            doCloseX                = true;
            absorbInputAroundWindow = true;
            forcePause              = false;
            draggable               = true;
            resizeable              = true;

            GuildHandler.RequestSnapshot();
            _lastRefreshUtc   = DateTime.UtcNow;
            GuildCache.Updated += OnSnapshotUpdated;
        }

        public override void PostClose()
        {
            base.PostClose();
            GuildCache.Updated -= OnSnapshotUpdated;
        }

        private void OnSnapshotUpdated()
        {
            _orderedMembers = null;
            _lastRefreshUtc = DateTime.UtcNow;
        }

        public override void WindowUpdate()
        {
            base.WindowUpdate();
            _refreshTimer -= Time.unscaledDeltaTime;
            if (_refreshTimer <= 0f)
            {
                _refreshTimer = DialogLayout.AutoRefreshSeconds;
                GuildHandler.RequestSnapshot();
                _lastRefreshUtc = DateTime.UtcNow;
            }
        }

        protected override void DrawContents(Rect rect)
        {
            string headerName = GuildCache.Guild?.Name ?? "(no guild)";
            float y = DialogLayout.DrawTitle(rect, $"Guild Hall - {headerName}");

            int secsSince = Math.Max(0, (int)(DateTime.UtcNow - _lastRefreshUtc).TotalSeconds);
            DialogLayout.DrawLiveBadge(rect, secsSince);

            if (!GuildCache.HasSnapshot)
            {
                DialogLayout.LabelTrunc(new Rect(0f, y + 4f, rect.width, 20f),
                    "<color=grey>Loading guild snapshot…</color>");
                if (DialogLayout.DrawCloseButton(rect)) Close();
                return;
            }

            // No-guild state - handshake completed, server confirmed caller isn't a member of any guild
            if (!GuildCache.InGuild || GuildCache.Guild == null)
            {
                DialogLayout.DrawSectionDivider(rect, ref y);
                DialogLayout.LabelTrunc(new Rect(0f, y + 6f, rect.width, 22f),
                    "<color=grey>You're not currently in a guild.</color>");
                DialogLayout.LabelTrunc(new Rect(0f, y + 30f, rect.width, 22f),
                    "<color=grey>Join an open guild below, or ask an admin to invite you. Create one with /kmh guild create.</color>");
                if (Widgets.ButtonText(new Rect(0f, y + 58f, 160f, 30f), "Join a guild…"))
                {
                    Find.WindowStack.Add(new Dialog_KMHTextInput(
                        title:        "Join a guild",
                        confirmLabel: "Join",
                        initial:      "",
                        maxChars:     64,
                        rejectEmpty:  true,
                        onConfirm:    n => GuildHandler.TryJoin(n)));
                }
                if (DialogLayout.DrawCloseButton(rect)) Close();
                return;
            }

            GuildSnapshot g = GuildCache.Guild;
            DialogLayout.DrawSectionDivider(rect, ref y);

            // MOTD banner - admins get a small Edit affordance on the right.
            string motd = string.IsNullOrWhiteSpace(g.Motd)
                ? "<color=grey>(no message of the day)</color>"
                : g.Motd;
            bool isAdmin = string.Equals(MyRank(g), GuildMemberDto.RankAdmin, StringComparison.OrdinalIgnoreCase);
            float motdEditW = isAdmin ? 110f : 0f;
            DialogLayout.LabelTrunc(new Rect(0f, y, rect.width - motdEditW - 4f, 22f), $"<b>MOTD:</b> {motd}");
            if (isAdmin)
            {
                if (Widgets.ButtonText(new Rect(rect.width - motdEditW, y - 2f, motdEditW - 4f, 26f), "Edit MOTD…"))
                {
                    Find.WindowStack.Add(new Dialog_KMHTextInput(
                        title:        "Edit guild MOTD",
                        confirmLabel: "Save",
                        initial:      g.Motd ?? "",
                        maxChars:     256,
                        rejectEmpty:  false,
                        onConfirm:    s => GuildHandler.TrySetMotd(s)));
                }
            }
            y += 26f;

            // Diplomacy summary + admin action button on the right.
            string diplomacy = FormatDiplomacy(g.Relationships);
            Color oldCol = GUI.color;
            GUI.color = DialogLayout.MutedColor;
            float diplomacyBtnW = isAdmin ? 130f : 0f;
            DialogLayout.LabelTrunc(new Rect(0f, y, rect.width - diplomacyBtnW - 4f, 20f), diplomacy);
            GUI.color = oldCol;
            if (isAdmin)
            {
                if (Widgets.ButtonText(new Rect(rect.width - diplomacyBtnW, y - 4f, diplomacyBtnW - 4f, 26f), "Diplomacy ▾"))
                {
                    OpenDiplomacyMenu(g);
                }
            }
            y += 24f;

            // Action row: invite (admin/mod), open-join toggle (admin), refresh. Server enforces rank; we hide
            // affordances that would clearly no-op to keep the row clean
            string myRankNow = MyRank(g);
            bool canInvite = string.Equals(myRankNow, GuildMemberDto.RankAdmin,     StringComparison.OrdinalIgnoreCase)
                          || string.Equals(myRankNow, GuildMemberDto.RankModerator, StringComparison.OrdinalIgnoreCase);
            float ax = 0f;
            if (canInvite)
            {
                if (Widgets.ButtonText(new Rect(ax, y, 130f, 28f), "Invite player…"))
                {
                    Find.WindowStack.Add(new Dialog_KMHTextInput(
                        title:        "Invite a player",
                        confirmLabel: "Invite",
                        initial:      "",
                        maxChars:     64,
                        rejectEmpty:  true,
                        onConfirm:    n => GuildHandler.TryInvite(n)));
                }
                ax += 138f;
            }
            if (isAdmin)
            {
                bool open = g.OpenJoin, newOpen = g.OpenJoin;
                Widgets.CheckboxLabeled(new Rect(ax, y, 150f, 28f), "Open to join", ref newOpen);
                if (newOpen != open) GuildHandler.TrySetOpenJoin(newOpen);
            }

            // Refresh button on the right.
            if (Widgets.ButtonText(new Rect(rect.width - 110f, y, 100f, 28f), "Refresh"))
            {
                GuildHandler.RequestSnapshot();
                _lastRefreshUtc = DateTime.UtcNow;
            }
            y += 34f;

            // Two-column main body: Members (left, wider) | Perks (right, narrower).
            float bottomReserve = 120f + DialogLayout.FooterReserve; // settings overview at the bottom
            float paneH  = rect.height - y - bottomReserve;
            float leftW  = rect.width * 0.58f - 4f;
            float rightX = leftW + 8f;
            float rightW = rect.width - rightX;

            DialogLayout.LabelTrunc(new Rect(0f, y, leftW, 20f), $"<b>Members</b>  <color=grey>({g.Members?.Count ?? 0})</color>");
            Rect membersBox = new Rect(0f, y + 22f, leftW, paneH - 22f);
            Widgets.DrawMenuSection(membersBox);
            DrawMembersList(membersBox, g);

            DialogLayout.LabelTrunc(new Rect(rightX, y, rightW, 20f), "<b>Perks</b>");
            Rect perksBox = new Rect(rightX, y + 22f, rightW, paneH - 22f);
            Widgets.DrawMenuSection(perksBox);
            DrawPerksList(perksBox, g.Perks);

            // Settings overview band at the bottom.
            float settingsY = y + paneH + 6f;
            DrawSettingsOverview(new Rect(0f, settingsY, rect.width, 110f), g.Settings);

            if (DialogLayout.DrawCloseButton(rect)) Close();
        }

        // Caller's own rank within this guild - derived from snapshot so we can gate per-row mutation buttons.
        // Returns null if caller isn't in the member list (shouldn't happen for valid snapshots)
        private static string MyRank(GuildSnapshot g)
        {
            string mine = SessionHandler.Username;
            if (string.IsNullOrEmpty(mine) || g?.Members == null) return null;
            foreach (GuildMemberDto m in g.Members)
            {
                if (string.Equals(m.Username, mine, StringComparison.OrdinalIgnoreCase))
                    return m.Rank;
            }
            return null;
        }

        private void DrawMembersList(Rect box, GuildSnapshot g)
        {
            Rect inner = box.ContractedBy(DialogLayout.ListInnerPad);
            const float rowH = 28f;
            string myRank = MyRank(g);
            string mine   = SessionHandler.Username ?? string.Empty;

            // Sort: admins first, then moderators, officers, members. Within each rank, by silver contributed desc
            if (_orderedMembers == null || !ReferenceEquals(_orderedMembersSource, g.Members))
            {
                List<GuildMemberDto> source = g.Members ?? new List<GuildMemberDto>();
                List<GuildMemberDto> sorted = new List<GuildMemberDto>(source);
                sorted.Sort((a, b) =>
                {
                    int rankCmp = RankOrder(a.Rank).CompareTo(RankOrder(b.Rank));
                    if (rankCmp != 0) return rankCmp;
                    return b.SilverContributed.CompareTo(a.SilverContributed);
                });
                _orderedMembers       = sorted;
                _orderedMembersSource = g.Members;
            }

            List<GuildMemberDto> rows = _orderedMembers;
            float viewH    = Mathf.Max(inner.height, rows.Count * rowH + 8f);
            Rect  viewRect = new Rect(0f, 0f, inner.width - DialogLayout.ScrollbarReserveWidth, viewH);

            Widgets.BeginScrollView(inner, ref _membersScroll, viewRect);
            DialogLayout.VisibleRange(_membersScroll, inner.height, rowH, rows.Count, out int firstM, out int lastM);
            for (int i = firstM; i < lastM; i++)
            {
                GuildMemberDto m = rows[i];
                float ly = i * rowH;
                Rect row = new Rect(0f, ly, viewRect.width, rowH);
                if (i % 2 == 0) Widgets.DrawAltRect(row);
                Widgets.DrawHighlightIfMouseover(row);

                // Rank chip + name on left, contributions middle, mutation buttons right (gated on caller's rank +
                // target's rank)
                const float actionW = 130f;
                string rankChip = $"<color={RankColor(m.Rank)}>{FriendlyRank(m.Rank)}</color>";
                DialogLayout.LabelTrunc(new Rect(6f, ly + 4f, 90f, rowH - 8f), rankChip);
                DialogLayout.LabelTrunc(new Rect(100f, ly + 4f, viewRect.width * 0.35f, rowH - 8f),
                    $"<b>{LinkedAccountsCache.Format(m.Username)}</b>");
                Color oldCol = GUI.color;
                GUI.color = DialogLayout.MutedColor;
                Widgets.Label(
                    new Rect(viewRect.width * 0.45f, ly + 4f,
                             viewRect.width - actionW - viewRect.width * 0.45f - 6f, rowH - 8f),
                    $"{SilverFmt.Format(m.SilverContributed)} donated  •  {m.QuestsCompleted}q");
                GUI.color = oldCol;

                DrawMemberActionButton(
                    new Rect(viewRect.width - actionW + 2f, ly + 2f, actionW - 6f, rowH - 4f),
                    m, myRank, mine);
            }
            if (rows.Count == 0)
            {
                DialogLayout.LabelTrunc(new Rect(6f, 6f, viewRect.width, 20f),
                    "<color=grey>No members.</color>");
            }
            Widgets.EndScrollView();
        }

        // Admin diplomacy menu: propose/declare by guild name, plus accept/break/clear actions for existing relationships.
        private void OpenDiplomacyMenu(GuildSnapshot g)
        {
            List<FloatMenuOption> opts = new List<FloatMenuOption>();

            opts.Add(new FloatMenuOption("Propose alliance with…", () =>
            {
                Find.WindowStack.Add(new Dialog_KMHTextInput(
                    title:        "Propose alliance",
                    confirmLabel: "Propose",
                    initial:      "",
                    maxChars:     64,
                    rejectEmpty:  true,
                    onConfirm:    n => GuildHandler.TryProposeAlliance(n)));
            }));
            opts.Add(new FloatMenuOption("Declare hostile against…", () =>
            {
                Find.WindowStack.Add(new Dialog_KMHTextInput(
                    title:        "Declare hostile",
                    confirmLabel: "Declare",
                    initial:      "",
                    maxChars:     64,
                    rejectEmpty:  true,
                    onConfirm:    n => GuildHandler.TryDeclareHostile(n)));
            }));

            if (g?.Relationships != null)
            {
                foreach (KeyValuePair<string, string> kv in g.Relationships)
                {
                    string other = kv.Key;
                    switch (kv.Value)
                    {
                        case GuildSnapshot.RelationAlliedRequested:
                            opts.Add(new FloatMenuOption($"Accept alliance - {other}",
                                () => GuildHandler.TryAcceptAlliance(other)));
                            break;
                        case GuildSnapshot.RelationAllied:
                            opts.Add(new FloatMenuOption($"Break alliance - {other}",
                                () => GuildHandler.TryBreakAlliance(other)));
                            break;
                        case GuildSnapshot.RelationHostile:
                            opts.Add(new FloatMenuOption($"Clear hostility - {other}",
                                () => GuildHandler.TryClearHostile(other)));
                            break;
                    }
                }
            }

            Find.WindowStack.Add(new FloatMenu(opts));
        }

        // Member actions: admins/mods can manage others, never themselves, and only promote below their own rank.
        private static void DrawMemberActionButton(Rect rect, GuildMemberDto target, string myRank, string mine)
        {
            // No caller rank known -> caller isn't in this guild's roster.
            if (string.IsNullOrEmpty(myRank)) return;
            // Can't act on yourself.
            if (string.Equals(target.Username, mine, StringComparison.OrdinalIgnoreCase)) return;

            int myOrder     = RankOrder(myRank);
            int targetOrder = RankOrder(target.Rank);

            // Only Mod (1) and Admin (0) can act on others.
            if (myOrder > RankOrder(GuildMemberDto.RankModerator)) return;
            // Can't act on someone equal-or-higher than you.
            if (targetOrder <= myOrder) return;

            if (Widgets.ButtonText(rect, "Manage ▾"))
            {
                List<FloatMenuOption> opts = new List<FloatMenuOption>();

                // Promote: only if target is below max-promotable rank (Mod is the highest non-Admin rank we expose for promotion)
                if (targetOrder > RankOrder(GuildMemberDto.RankModerator))
                {
                    opts.Add(new FloatMenuOption("Promote",
                        () => GuildHandler.TryPromote(target.Username)));
                }
                // Demote: only if target is above Member.
                if (targetOrder < RankOrder(GuildMemberDto.RankMember))
                {
                    opts.Add(new FloatMenuOption("Demote",
                        () => GuildHandler.TryDemote(target.Username)));
                }
                // Kick available to anyone with rank-authority over the target.
                opts.Add(new FloatMenuOption("Kick from guild",
                    () => GuildHandler.TryKick(target.Username)));

                Find.WindowStack.Add(new FloatMenu(opts));
            }
        }

        private void DrawPerksList(Rect box, GuildPerksDto p)
        {
            Rect inner = box.ContractedBy(DialogLayout.ListInnerPad);
            const float rowH = 48f; // two text lines (name + effect), each needs a full font line
            // Buy button only for Admin (server enforces; we hide to avoid dangling no-op affordance for Members)
            bool canBuy = string.Equals(MyRank(GuildCache.Guild), GuildMemberDto.RankAdmin, StringComparison.OrdinalIgnoreCase);

            // Four perks, each with current/max level, an effect summary, and the perk_key the GuildBuyPerk envelope expects
            (string label, int level, string effect, string key)[] perks = new[]
            {
                ("Site Max Workers", p.SiteMaxWorkersBonusLevel,    $"+{p.SiteMaxWorkersBonusLevel * 2} workers/site",       GuildHandler.PerkSiteMaxWorkers),
                ("Market Tax Cut",   p.MarketplaceTaxReductionLevel, $"-{p.MarketplaceTaxReductionLevel}% market tax",         GuildHandler.PerkMarketplaceTaxCut),
                ("Worker XP Bonus",  p.WorkerXpBonusLevel,           $"x{WorkerXpMultiplierText(p.WorkerXpBonusLevel)} XP",    GuildHandler.PerkWorkerXpBonus),
                ("Custom Site Cost", p.CustomSiteCostDiscountLevel,  $"-{p.CustomSiteCostDiscountLevel * 10}% build cost",     GuildHandler.PerkCustomSiteCostCut),
            };

            float viewH    = Mathf.Max(inner.height, perks.Length * rowH + 8f);
            Rect  viewRect = new Rect(0f, 0f, inner.width - DialogLayout.ScrollbarReserveWidth, viewH);

            Widgets.BeginScrollView(inner, ref _perksScroll, viewRect);
            float ly = 0f;
            for (int i = 0; i < perks.Length; i++)
            {
                var (label, level, effect, key) = perks[i];
                Rect row = new Rect(0f, ly, viewRect.width, rowH);
                if (i % 2 == 0) Widgets.DrawAltRect(row);

                // Reserve right edge for the Buy button (admins only); when capped, render a 'MAX' label instead
                const float btnW = 80f;
                float textW = viewRect.width - btnW - 16f;

                DialogLayout.LabelTrunc(new Rect(6f, ly + 4f, textW, 22f),
                    $"<b>{label}</b>  <color=grey>(Lv {level}/{GuildPerksDto.MaxLevel})</color>");
                Color oldCol = GUI.color;
                GUI.color = DialogLayout.MutedColor;
                DialogLayout.LabelTrunc(new Rect(6f, ly + 26f, textW, 20f), effect);
                GUI.color = oldCol;

                if (canBuy)
                {
                    Rect btn = new Rect(viewRect.width - btnW - 4f, ly + 4f, btnW, rowH - 8f);
                    if (level >= GuildPerksDto.MaxLevel)
                    {
                        DialogLayout.DrawCenteredLabel(btn, "<color=grey>MAX</color>");
                    }
                    else
                    {
                        // Capture so the lambda closes over a stable copy of the loop's perk key
                        string capturedKey = key;
                        if (Widgets.ButtonText(btn, "Buy")) GuildHandler.TryBuyPerk(capturedKey);
                    }
                }

                ly += rowH;
            }
            Widgets.EndScrollView();
        }

        // Admins get an Edit button on the Settings band header; everyone else just sees the read-only summary
        private static void DrawSettingsOverview(Rect r, GuildSettingsDto s)
        {
            Widgets.DrawMenuSection(r);
            Rect inner = r.ContractedBy(8f);
            Color oldCol = GUI.color;

            bool isAdmin = string.Equals(MyRank(GuildCache.Guild), GuildMemberDto.RankAdmin, StringComparison.OrdinalIgnoreCase);
            float editW = isAdmin ? 100f : 0f;
            DialogLayout.LabelTrunc(new Rect(inner.x, inner.y, inner.width - editW - 4f, 18f), "<b>Settings</b>");
            if (isAdmin)
            {
                if (Widgets.ButtonText(new Rect(inner.xMax - editW, inner.y - 2f, editW - 4f, 22f), "Edit…"))
                {
                    Find.WindowStack.Add(new Dialog_KMHGuildSettings(s));
                }
            }

            GUI.color = DialogLayout.MutedColor;
            DialogLayout.LabelTrunc(new Rect(inner.x, inner.y + 20f, inner.width, 18f),
                $"Site reward tax: {s.SiteRewardSilverTaxPercent}%   •   Market sale tax: {s.MarketplaceSaleTaxPercent}%   •   " +
                $"Default listings: {(s.DefaultListingsGuildOnly ? "guild-only" : "public")}");

            DialogLayout.LabelTrunc(new Rect(inner.x, inner.y + 40f, inner.width, 18f),
                $"Daily withdraw caps - " +
                $"Member: {CapStr(s.MemberDailyWithdrawCap)}  •  " +
                $"Officer: {CapStr(s.OfficerDailyWithdrawCap)}  •  " +
                $"Moderator: {CapStr(s.ModeratorDailyWithdrawCap)}  •  " +
                $"Admin: {CapStr(s.AdminDailyWithdrawCap)}");

            GUI.color = oldCol;
        }

        private static string CapStr(int cap)
        {
            if (cap < 0) return "unlimited";
            if (cap == 0) return "none";
            return $"{cap}s";
        }

        // Friendly summary of declared diplomacy. Empty dict → muted "no declared diplomacy" line
        private static string FormatDiplomacy(Dictionary<string, string> rels)
        {
            if (rels == null || rels.Count == 0)
            {
                return "<color=grey>Diplomacy: no declared alliances.</color>";
            }
            List<string> allies     = new List<string>();
            List<string> pending    = new List<string>();
            List<string> hostiles   = new List<string>();
            foreach (var kv in rels)
            {
                switch (kv.Value)
                {
                    case GuildSnapshot.RelationAllied:          allies.Add(kv.Key);   break;
                    case GuildSnapshot.RelationAlliedRequested: pending.Add(kv.Key);  break;
                    case GuildSnapshot.RelationHostile:         hostiles.Add(kv.Key); break;
                }
            }
            List<string> parts = new List<string>();
            if (allies.Count   > 0) parts.Add($"<color=#7CD37C>Allies:</color> {string.Join(", ", allies)}");
            if (pending.Count  > 0) parts.Add($"<color=#FFCE4D>Pending:</color> {string.Join(", ", pending)}");
            if (hostiles.Count > 0) parts.Add($"<color=#FF7777>Hostile:</color> {string.Join(", ", hostiles)}");
            return parts.Count == 0
                ? "<color=grey>Diplomacy: no declared alliances.</color>"
                : string.Join("    ", parts);
        }

        // Rank ordering for member-list sort. Admin first.
        private static int RankOrder(string rank)
        {
            switch (rank)
            {
                case GuildMemberDto.RankAdmin:     return 0;
                case GuildMemberDto.RankModerator: return 1;
                case GuildMemberDto.RankOfficer:   return 2;
                case GuildMemberDto.RankMember:    return 3;
                default:                           return 4;
            }
        }

        private static string FriendlyRank(string rank)
        {
            switch (rank)
            {
                case GuildMemberDto.RankAdmin:     return "ADMIN";
                case GuildMemberDto.RankModerator: return "MOD";
                case GuildMemberDto.RankOfficer:   return "OFFICER";
                case GuildMemberDto.RankMember:    return "Member";
                default:                           return rank?.ToUpper() ?? "?";
            }
        }

        private static string RankColor(string rank)
        {
            switch (rank)
            {
                case GuildMemberDto.RankAdmin:     return "#ff8888"; // red-ish
                case GuildMemberDto.RankModerator: return "#ffce4d"; // amber
                case GuildMemberDto.RankOfficer:   return "#79b8ff"; // blue
                case GuildMemberDto.RankMember:    return "#cccccc"; // muted
                default:                           return "#aaaaaa";
            }
        }

        private static string WorkerXpMultiplierText(int level)
        {
            switch (level)
            {
                case 0: return "1.0";
                case 1: return "1.25";
                case 2: return "1.5";
                case 3: return "2.0";
                default: return "?";
            }
        }
    }
}
