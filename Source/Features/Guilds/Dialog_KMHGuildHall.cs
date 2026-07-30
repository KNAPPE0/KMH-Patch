using System;
using System.Collections.Generic;
using KMHPatch.Features.Guilds.Dto;
using KMHPatch.Features.LinkedAccounts;
using KMHPatch.UI;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace KMHPatch.Features.Guilds
{
    // Guild Hall dialog for members, perks, MOTD, settings, and diplomacy; guild rankings live in Server Standings.
    public class Dialog_KMHGuildHall : Window_KMHBase
    {
        public override Vector2 InitialSize => new Vector2(1000f, 680f);

        // Owner has every Admin power; treat both as "admin" for UI gating.
        private static bool IsAdminRank(string rank)
            => string.Equals(rank, GuildMemberDto.RankAdmin, StringComparison.OrdinalIgnoreCase)
            || string.Equals(rank, GuildMemberDto.RankOwner, StringComparison.OrdinalIgnoreCase);

        // One deliberate click per request: swallow re-clicks within 1.5s (snapshot round-trip window).
        private static DateTime _lastJoinToggleUtc = DateTime.MinValue;
        private static bool DebounceOk(ref DateTime last)
        {
            if ((DateTime.UtcNow - last).TotalSeconds < 1.5) return false;
            last = DateTime.UtcNow;
            return true;
        }

        private Vector2  _membersScroll;
        private Vector2  _perksScroll;

        private List<GuildMemberDto> _orderedMembers;
        private object               _orderedMembersSource;

        public Dialog_KMHGuildHall()
        {
            doCloseX                = true;
            absorbInputAroundWindow = true;
            forcePause              = false;
            draggable               = true;
            resizeable              = true;

            EnableAutoRefresh(() => GuildHandler.RequestSnapshot());
            GuildCache.Updated += OnSnapshotUpdated;
        }

        public override void PostClose()
        {
            base.PostClose();
            GuildCache.Updated -= OnSnapshotUpdated;
        }

        // Also invalidate the ordered-members cache so the fresh snapshot rebuilds it.
        private void OnSnapshotUpdated()
        {
            _orderedMembers = null;
            MarkRefreshed();
        }

        protected override void DrawContents(Rect rect)
        {
            string headerName = GuildCache.Guild?.Name ?? "(no guild)";
            float y = DialogLayout.DrawTitle(rect, $"Guild Hall - {headerName}");

            DialogLayout.DrawLiveBadge(rect, SecondsSinceRefresh);

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
                    "<color=grey>Create your own guild, accept an invite below, or join an open guild by name.</color>");
                if (Widgets.ButtonText(new Rect(0f, y + 58f, 160f, 30f), "Create a guild…"))
                {
                    Find.WindowStack.Add(new Dialog_KMHTextInput(
                        title:        "Create a guild",
                        confirmLabel: "Create",
                        initial:      "",
                        maxChars:     64,
                        rejectEmpty:  true,
                        onConfirm:    n => GuildHandler.CreateGuild(n)));
                }
                if (Widgets.ButtonText(new Rect(168f, y + 58f, 160f, 30f), "Join a guild…"))
                {
                    Find.WindowStack.Add(new Dialog_KMHGuildPicker());
                }
                y += 96f;

                // Standing invites for this player - accept joins immediately, decline removes the invite.
                List<GuildInviteDto> invites = GuildCache.Envelope?.MyInvites;
                DialogLayout.DrawSectionDivider(rect, ref y);
                DialogLayout.LabelTrunc(new Rect(0f, y + 4f, rect.width, 24f), "<b>Guild invites for you</b>");
                y += 30f;
                if (invites == null || invites.Count == 0)
                {
                    DialogLayout.LabelTrunc(new Rect(0f, y, rect.width, 22f),
                        "<color=grey>No pending invites. When a guild invites you, it appears here.</color>");
                }
                else
                {
                    foreach (GuildInviteDto inv in invites)
                    {
                        Rect row = new Rect(0f, y, rect.width, 30f);
                        Widgets.DrawLightHighlight(row);
                        string by = string.IsNullOrEmpty(inv.Inviter) ? "" : $"  <color=grey>invited by {inv.Inviter}</color>";
                        DialogLayout.LabelTrunc(new Rect(6f, y + 5f, rect.width - 200f, 22f),
                            $"<b>{inv.GuildName}</b>  <color=grey>({inv.Members} member{(inv.Members == 1 ? "" : "s")})</color>{by}");
                        if (Widgets.ButtonText(new Rect(rect.width - 190f, y + 2f, 90f, 26f), "Accept"))
                            GuildHandler.TryJoin(inv.GuildName);
                        if (Widgets.ButtonText(new Rect(rect.width - 94f, y + 2f, 90f, 26f), "Decline"))
                            GuildHandler.TryDeclineInvite(inv.GuildName);
                        y += 34f;
                    }
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
            bool isAdmin = IsAdminRank(MyRank(g));
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

            // Guild Hall row: shows the hall status; the admin can set it at the current colony/caravan or clear it.
            // Only relevant when a server enables Guild Hall rules, but the status is always informative.
            {
                bool hasHall = g.Hall != null && g.Hall.HasHall;
                string hallText = hasHall
                    ? $"<b>Guild Hall:</b> <color=#7CD37C>set at world tile {g.Hall.Tile}</color> <color=grey>(access radius {g.Hall.RadiusTiles})</color>"
                    : "<b>Guild Hall:</b> <color=grey>not set (only needed if the server enables Guild Hall rules)</color>";
                float jumpW    = hasHall ? 110f : 0f;
                float hallBtnW = isAdmin ? 160f : 0f;
                DialogLayout.LabelTrunc(new Rect(0f, y, rect.width - hallBtnW - jumpW - 8f, 22f), hallText);
                if (hasHall && Widgets.ButtonText(new Rect(rect.width - hallBtnW - jumpW - 4f, y - 2f, jumpW - 4f, 26f), "Jump to Hall"))
                {
                    Close(false);
                    CameraJumper.TryJump(new GlobalTargetInfo(g.Hall.Tile));
                }
                if (isAdmin)
                {
                    if (Widgets.ButtonText(new Rect(rect.width - hallBtnW, y - 2f, hallBtnW - 4f, 26f), hasHall ? "Move / Remove Hall ▾" : "Set Hall ▾"))
                        Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption>
                        {
                            new FloatMenuOption("Set via map (pick a world tile)…", BeginHallViaMap),
                            new FloatMenuOption("Set at my current colony/caravan tile", () => GuildHandler.TrySetHall()),
                            new FloatMenuOption("Remove Guild Hall", () => { if (hasHall) GuildHandler.TryRemoveHall(); }),
                        }));
                }
                y += 26f;
            }

            // Action row: invite (admin/mod), open-join toggle (admin), refresh. Server enforces rank; we hide
            // affordances that would clearly no-op to keep the row clean
            string myRankNow = MyRank(g);
            bool canInvite = IsAdminRank(myRankNow)
                          || string.Equals(myRankNow, GuildMemberDto.RankModerator, StringComparison.OrdinalIgnoreCase);
            // Row 1 - guild management (invite + join-mode), Refresh anchored right. Each control advances the cursor so
            // nothing overlaps; personal actions move to a second row below so buttons never clip at common UI scales.
            const float bh = 28f;
            float ax = 0f;
            if (canInvite)
            {
                if (Widgets.ButtonText(new Rect(ax, y, 130f, bh), "Invite player…"))
                    Find.WindowStack.Add(new Dialog_KMHPlayerPicker(n => GuildHandler.TryInvite(n)));
                ax += 138f;
            }
            if (isAdmin)
            {
                // Explicit action button (NOT a checkbox): one deliberate click = one request, with a client debounce so
                // repeated clicks while the snapshot is in flight can't flip-flop the server state.
                string joinLabel = g.OpenJoin ? "Set Invite-only" : "Set Open to join";
                if (Widgets.ButtonText(new Rect(ax, y, 150f, bh), joinLabel) && DebounceOk(ref _lastJoinToggleUtc))
                    GuildHandler.TrySetOpenJoin(!g.OpenJoin);
                ax += 158f;
            }
            DialogLayout.LabelTrunc(new Rect(ax, y + 5f, rect.width - ax - 110f, 20f),
                g.OpenJoin ? "<color=#7CD37C>Anyone can join</color>" : "<color=grey>Invite-only</color>");
            if (Widgets.ButtonText(new Rect(rect.width - 100f, y, 96f, bh), "Refresh"))
            {
                GuildHandler.RequestSnapshot();
                MarkRefreshed();
            }
            y += 34f;

            // Row 2 - personal member actions (server enforces rank caps / owner-leave rule).
            float bx = 0f;
            if (g.GuildTreasuryEnabled)
            {
                if (Widgets.ButtonText(new Rect(bx, y, 120f, bh), "Donate silver…"))
                    Find.WindowStack.Add(new Dialog_KMHAmountInput("Donate to guild vault", "Donate", "silver (from your personal Treasury)", 0,
                        qty => GuildHandler.TryDonate(qty)));
                bx += 128f;
                if (Widgets.ButtonText(new Rect(bx, y, 130f, bh), "Withdraw silver…"))
                    Find.WindowStack.Add(new Dialog_KMHAmountInput("Withdraw from guild vault", "Withdraw", "silver (to your personal Treasury; daily cap by rank)", g.GuildSilver > int.MaxValue ? int.MaxValue : (int)g.GuildSilver,
                        qty => GuildHandler.TryWithdrawFromGuild(qty)));
                bx += 138f;
            }
            if (Widgets.ButtonText(new Rect(bx, y, 100f, bh), "Leave guild"))
                Find.WindowStack.Add(Verse.Dialog_MessageBox.CreateConfirmation(
                    "Leave this guild? You lose your rank and contribution credit. Guild vault/sites/perks are not deleted. The guild Owner must transfer ownership or disband before leaving.",
                    () => GuildHandler.TryLeave()));
            y += 34f;

            // Guild vault (silver-only) + this member's lifetime contribution - shown by donate/perks so it reads clearly.
            long myDonated = 0;
            GuildMemberDto meMem = g.Members?.Find(x => KmhSession.IsMe(x.Username));
            if (meMem != null) myDonated = meMem.SilverContributed;
            string vaultText = g.GuildTreasuryEnabled
                ? $"<b>Guild vault:</b> <color=#E2C16B>{g.GuildSilver:N0} silver</color> <color=grey>(silver-only)</color>    <b>Your donated:</b> {myDonated:N0}"
                : "<b>Guild vault:</b> <color=grey>disabled by server</color>";
            DialogLayout.LabelTrunc(new Rect(0f, y, rect.width, 20f), vaultText);
            y += 24f;

            // Donations still waiting on a donor's save-confirm: visible, but not spendable for perks/withdraws.
            if (g.GuildTreasuryEnabled && g.PendingDonationsSilver > 0)
            {
                long pendingMine = 0;
                var tSnap = Treasury.TreasuryCache.HasSnapshot ? Treasury.TreasuryCache.Snapshot : null;
                if (tSnap?.PendingDeposits != null)
                    foreach (Treasury.Dto.PendingDeposit p in tSnap.PendingDeposits)
                        if (p != null && p.Kind == Treasury.Dto.PendingDeposit.KindGuildDonate
                            && string.Equals(p.GuildName, g.Name, StringComparison.OrdinalIgnoreCase))
                            pendingMine += p.Silver;
                string mine = pendingMine > 0 ? $"    <b>Yours:</b> {pendingMine:N0} <color=grey>(save your game to finalize)</color>" : "";
                DialogLayout.LabelTrunc(new Rect(0f, y, rect.width, 18f),
                    $"<color=grey>⏳ Pending donations:</color> {g.PendingDonationsSilver:N0} silver <color=grey>(not spendable yet)</color>{mine}");
                y += 22f;
            }

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

            DialogLayout.LabelTrunc(new Rect(rightX, y, rightW, 20f), $"<b>Perks</b>  <color=grey>· guild silver {g.GuildSilver:N0}</color>");
            Rect perksBox = new Rect(rightX, y + 22f, rightW, paneH - 22f);
            Widgets.DrawMenuSection(perksBox);
            DrawPerksList(perksBox, g.Perks, g.GuildSilver);

            // Settings overview band at the bottom.
            float settingsY = y + paneH + 6f;
            DrawSettingsOverview(new Rect(0f, settingsY, rect.width, 110f), g.Settings);

            if (DialogLayout.DrawCloseButton(rect)) Close();
        }

        // Caller's own rank within this guild - derived from snapshot so we can gate per-row mutation buttons.
        // Returns null if caller isn't in the member list (shouldn't happen for valid snapshots)
        private static string MyRank(GuildSnapshot g)
        {
            string mine = KmhSession.Me;
            if (string.IsNullOrEmpty(mine) || g?.Members == null) return null;
            foreach (GuildMemberDto m in g.Members)
            {
                if (KmhSession.Same(m.Username, mine))
                    return m.Rank;
            }
            return null;
        }

        private void DrawMembersList(Rect box, GuildSnapshot g)
        {
            Rect inner = box.ContractedBy(DialogLayout.ListInnerPad);
            const float rowH = 28f;
            string myRank = MyRank(g);
            string mine   = KmhSession.Me;

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

        // "Set via map" workflow (mirrors how Sites pick a build tile): close the dialog, show the world, target a tile.
        private void BeginHallViaMap()
        {
            Close(false);
            CameraJumper.TryShowWorld();
            Find.WorldTargeter.BeginTargeting(OnHallTilePicked, true);
        }

        private bool OnHallTilePicked(GlobalTargetInfo target)
        {
            GuildHandler.TrySetHallAt(target.Tile.tileId);
            return true;
        }

        // Admin diplomacy menu: propose/declare by guild name, plus accept/break/clear actions for existing relationships.
        // Other guilds selectable by NAME from the leaderboard snapshot - no manual typing. Relation state shown inline.
        private static List<FloatMenuOption> OtherGuildOptions(GuildSnapshot g, string verb, Action<string> act)
        {
            List<FloatMenuOption> outp = new List<FloatMenuOption>();
            var rows = GuildLeaderboardCache.Snapshot?.Guilds;
            if (rows != null)
                foreach (var row in rows)
                {
                    if (row == null || string.IsNullOrEmpty(row.Name)) continue;
                    if (string.Equals(row.Name, g?.Name, StringComparison.OrdinalIgnoreCase)) continue;   // not ourselves
                    string rel = g?.Relationships != null && g.Relationships.TryGetValue(row.Name, out string r) && r != GuildSnapshot.RelationNone
                        ? $"  <color=grey>({r})</color>" : "";
                    string captured = row.Name;
                    outp.Add(new FloatMenuOption($"{verb} {row.Name} ({row.MemberCount} member(s)){rel}", () => act(captured)));
                }
            if (outp.Count == 0) outp.Add(new FloatMenuOption("No other guilds available.", null));
            return outp;
        }

        private void OpenDiplomacyMenu(GuildSnapshot g)
        {
            GuildHandler.RequestLeaderboard();   // refresh the guild list for next open; current cache serves this one
            List<FloatMenuOption> opts = new List<FloatMenuOption>();

            opts.Add(new FloatMenuOption("Propose alliance with…",
                () => Find.WindowStack.Add(new FloatMenu(OtherGuildOptions(g, "Ally with", n => GuildHandler.TryProposeAlliance(n))))));
            opts.Add(new FloatMenuOption("Declare hostile against…",
                () => Find.WindowStack.Add(new FloatMenu(OtherGuildOptions(g, "Declare hostile:", n => GuildHandler.TryDeclareHostile(n))))));

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
            if (KmhSession.Same(target.Username, mine)) return;

            int myOrder     = RankOrder(myRank);
            int targetOrder = RankOrder(target.Rank);

            // Only Mod (1) and Admin (0) can act on others.
            if (myOrder > RankOrder(GuildMemberDto.RankModerator)) return;
            // Can't act on someone equal-or-higher than you.
            if (targetOrder <= myOrder) return;

            if (Widgets.ButtonText(rect, "Manage ▾"))
            {
                List<FloatMenuOption> opts = new List<FloatMenuOption>();

                // Promote one rung, mirroring the server rule: the new rank must stay strictly BELOW the actor's.
                // (Owner can raise a Mod to Admin here - the old gate hid Promote for Mods entirely, forcing the
                // ownership-transfer workaround. Owner itself is only reachable via Transfer ownership.)
                int promotedOrder = targetOrder - 1;
                if (promotedOrder > myOrder)
                    opts.Add(new FloatMenuOption($"Promote to {FriendlyRank(RankFromOrder(promotedOrder))}",
                        () => GuildHandler.TryPromote(target.Username)));
                // Demote one rung toward Member.
                if (targetOrder < RankOrder(GuildMemberDto.RankMember))
                    opts.Add(new FloatMenuOption($"Demote to {FriendlyRank(RankFromOrder(targetOrder + 1))}",
                        () => GuildHandler.TryDemote(target.Username)));
                // Kick available to anyone with rank-authority over the target.
                opts.Add(new FloatMenuOption("Kick from guild",
                    () => GuildHandler.TryKick(target.Username)));
                // Ownership transfer: Owner only, and the only way anyone becomes Owner (outgoing owner drops to Admin).
                if (string.Equals(myRank, GuildMemberDto.RankOwner, StringComparison.OrdinalIgnoreCase))
                    opts.Add(new FloatMenuOption($"Transfer ownership to {target.Username}…",
                        () => Find.WindowStack.Add(Verse.Dialog_MessageBox.CreateConfirmation(
                            $"Make {target.Username} the guild Owner? You become an Admin. This cannot be undone by you.",
                            () => GuildHandler.TryTransferOwner(target.Username)))));

                Find.WindowStack.Add(new FloatMenu(opts));
            }
        }

        // Client mirror of the server perk cost ladder (GuildPerksDto.CostFor): levels 1/2/3 cost 5k/15k/30k.
        private static long NextPerkCost(int currentLevel) => currentLevel <= 0 ? 5000 : (currentLevel == 1 ? 15000 : 30000);

        private void DrawPerksList(Rect box, GuildPerksDto p, long guildSilver)
        {
            Rect inner = box.ContractedBy(DialogLayout.ListInnerPad);
            const float rowH = 66f; // three text lines: name+level / effect / cost+needed - nothing crammed or cut off
            // Buy button only for Owner/Admin (server enforces; we hide to avoid dangling no-op affordance for Members)
            bool canBuy = IsAdminRank(MyRank(GuildCache.Guild));

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
                // Effect + next-level cost + affordability, so the admin sees the price against the guild vault up front.
                // Line 2 = effect, line 3 = next cost + shortfall on its own line so nothing truncates in the narrow pane.
                DialogLayout.LabelTrunc(new Rect(6f, ly + 24f, textW, 18f), $"<color=grey>{effect}</color>");
                string costLine;
                if (level >= GuildPerksDto.MaxLevel) costLine = "<color=grey>maxed</color>";
                else
                {
                    long nextCost = NextPerkCost(level);
                    costLine = guildSilver < nextCost
                        ? $"<color=grey>next: {SilverFmt.Format(nextCost)}</color> <color=#ff8080>need {SilverFmt.Format(nextCost - guildSilver)} more</color>"
                        : $"<color=grey>next: {SilverFmt.Format(nextCost)}</color> <color=#7CD37C>affordable</color>";
                }
                DialogLayout.LabelTrunc(new Rect(6f, ly + 44f, textW, 18f), costLine);
                if (level < GuildPerksDto.MaxLevel)
                    TooltipHandler.TipRegion(new Rect(0f, ly, viewRect.width, rowH),
                        $"{label}\nCurrent: {effect} (Lv {level}/{GuildPerksDto.MaxLevel})\nNext level costs {SilverFmt.Format(NextPerkCost(level))} from the guild vault (balance {SilverFmt.Format(guildSilver)}).");

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

            bool isAdmin = IsAdminRank(MyRank(GuildCache.Guild));
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
                $"Guild tax to vault - site rewards: {s.SiteRewardSilverTaxPercent}%  •  member sales: {s.MarketplaceSaleTaxPercent}%  •  " +
                $"Default listings: {(s.DefaultListingsGuildOnly ? "guild-only" : "public")}");

            // Server tax vs guild perk reduction vs effective, so a 0% guild tax can't be misread as "no server tax".
            int serverTax = Features.Marketplace.MarketplaceCache.Snapshot?.ServerTaxPercent ?? 0;
            int perkCut   = GuildCache.Guild?.Perks?.MarketplaceTaxReductionLevel ?? 0;
            DialogLayout.LabelTrunc(new Rect(inner.x, inner.y + 40f, inner.width, 18f),
                $"Server market tax: {serverTax}%  •  guild perk reduction: -{perkCut}%  •  effective: {Math.Max(0, serverTax - perkCut)}%");

            DialogLayout.LabelTrunc(new Rect(inner.x, inner.y + 60f, inner.width, 18f),
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
            return SilverFmt.Format(cap);
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
                case GuildMemberDto.RankOwner:     return 0;
                case GuildMemberDto.RankAdmin:     return 1;
                case GuildMemberDto.RankModerator: return 2;
                case GuildMemberDto.RankOfficer:   return 3;
                case GuildMemberDto.RankMember:    return 4;
                default:                           return 5;
            }
        }

        // Mirror of the server ladder; Owner (0) is deliberately unreachable - only Transfer ownership assigns it.
        private static string RankFromOrder(int order)
        {
            switch (order)
            {
                case 1:  return GuildMemberDto.RankAdmin;
                case 2:  return GuildMemberDto.RankModerator;
                case 3:  return GuildMemberDto.RankOfficer;
                default: return GuildMemberDto.RankMember;
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
