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
    public class Dialog_KMHGuildHall : Window_KMHBase
    {
        public override Vector2 InitialSize => KMHPatch.UI.DialogLayout.FitToScreen(1000f, 680f);

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
        private Vector2  _invitesScroll;

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
            // The federation tag in the title comes from this; the server's cached view is enough for it.
            Features.Federation.FederationHandler.RequestSnapshot(false);
        }

        public override void PostClose()
        {
            base.PostClose();
            GuildCache.Updated -= OnSnapshotUpdated;
        }

        private void OnSnapshotUpdated()
        {
            _orderedMembers = null;
            MarkRefreshed();
        }

        protected override Features.Discord.KmhActivity PresenceActivity => Features.Discord.KmhActivity.Guild;

        // Takes the raw cached name, which the federation tag still matches on; only what is drawn is made inert.
        internal static string TitleFor(string guildName)
        {
            string shown = guildName == null ? KmhText.Get("KMH_GH_NoGuildTitle", "(no guild)") : KmhDisplayText.GuildName(guildName);
            string fedTag = Features.Federation.FederationCache.TitleTagFor(guildName);
            string title = KmhText.Get("KMH_GH_Title", "Guild Hall - {0}", shown);
            return fedTag.Length > 0 ? title + "  " + fedTag : title;
        }

        internal static string MemberCount(int n)
            => n == 1 ? KmhText.Get("KMH_GH_MemberOne", "1 member") : KmhText.Get("KMH_GH_MemberMany", "{0} members", n);

        internal static string TransferLabel(string username)
            => KmhText.Get("KMH_GH_TransferLabel", "Transfer ownership to {0}", KmhDisplayText.Inert(username));

        internal static string TransferConfirm(string username)
            => KmhText.Get("KMH_GH_TransferConfirm", "Make {0} the guild Owner? You become an Admin. This cannot be undone by you.", KmhDisplayText.Inert(username));

        private static (string Title, string Text)[] WhyAGuild => new[]
        {
            (KmhText.Get("KMH_GH_WhyVault", "Shared vault"),
             KmhText.Get("KMH_GH_WhyVaultText", "Pool silver and goods in one vault. Ranks decide who may take from it.")),
            (KmhText.Get("KMH_GH_WhyRanks", "Ranks and perks"),
             KmhText.Get("KMH_GH_WhyRanksText", "Give members ranks, and buy perks from the guild's funds for everyone.")),
            (KmhText.Get("KMH_GH_WhySites", "Sites and outposts"),
             KmhText.Get("KMH_GH_WhySitesText", "Build sites for the guild and claim Frontier outposts in its name.")),
            (KmhText.Get("KMH_GH_WhyAllies", "Alliances and Federation"),
             KmhText.Get("KMH_GH_WhyAlliesText", "Ally with other guilds here, or federate with guilds on other servers for renown and weekly objectives.")),
        };

        private static void DrawWhyAGuild(Rect area)
        {
            if (area.height < 90f) return;
            DialogLayout.LabelTrunc(new Rect(area.x, area.y, area.width, 24f), "<b>" + KmhText.Get("KMH_GH_WhyTitle", "What a guild gives you") + "</b>");
            float gap = 8f, cardW = (area.width - gap) / 2f;
            var whys = WhyAGuild;
            float textH = 0f;
            Text.Font = GameFont.Small;
            foreach (var why in whys) textH = Mathf.Max(textH, Text.CalcHeight(why.Text, Mathf.Max(1f, cardW - 16f)));
            float cardH = Mathf.Max(80f, 16f + 22f + textH);
            for (int i = 0; i < whys.Length; i++)
            {
                Rect card = new Rect(area.x + (i % 2) * (cardW + gap), area.y + 28f + (i / 2) * (cardH + gap), cardW, cardH);
                if (card.yMax > area.yMax) break;
                Widgets.DrawMenuSection(card);
                Rect inner = card.ContractedBy(8f);
                DialogLayout.LabelTrunc(new Rect(inner.x, inner.y, inner.width, 22f), $"<b>{whys[i].Title}</b>");
                Text.Font = GameFont.Small;
                GUI.color = new Color(0.66f, 0.69f, 0.73f);
                Widgets.Label(new Rect(inner.x, inner.y + 22f, inner.width, inner.height - 22f), whys[i].Text);
                GUI.color = Color.white;
            }
        }

        protected override void DrawContents(Rect rect)
        {
            float y = DialogLayout.DrawTitle(rect, TitleFor(GuildCache.Guild?.Name), DialogLayout.LiveBadgeReserve);

            DialogLayout.DrawLiveBadge(rect, SecondsSinceRefresh, HasReceivedData, 0f);

            if (!GuildCache.HasSnapshot)
            {
                DialogLayout.LabelTrunc(new Rect(0f, y + 4f, rect.width, 20f),
                    "<color=#A9AFB9>" + KmhText.Get("KMH_GH_Loading", "Loading guild snapshot…") + "</color>");
                if (DialogLayout.DrawCloseButton(rect)) Close();
                return;
            }

            if (!GuildCache.InGuild || GuildCache.Guild == null)
            {
                DialogLayout.DrawSectionDivider(rect, ref y);
                DialogLayout.LabelTrunc(new Rect(0f, y + 6f, rect.width, 22f),
                    "<color=#A9AFB9>" + KmhText.Get("KMH_GH_NotInGuild", "You're not currently in a guild.") + "</color>");
                DialogLayout.LabelTrunc(new Rect(0f, y + 30f, rect.width, 22f),
                    "<color=#A9AFB9>" + KmhText.Get("KMH_GH_NotInGuildHow", "Create your own guild, accept an invite below, or join an open guild by name.") + "</color>");
                string create = KmhText.Get("KMH_GH_CreateGuild", "Create a guild"), join = KmhText.Get("KMH_GH_JoinGuild", "Join a guild");
                float createW = DialogLayout.ButtonW(create, 160f, 260f), joinW = DialogLayout.ButtonW(join, 160f, 260f);
                if (KmhBrand.Button(new Rect(0f, y + 58f, createW, 30f), create, true))
                {
                    Find.WindowStack.Add(new Dialog_KMHTextInput(
                        title:        create,
                        confirmLabel: KmhText.Get("KMH_GH_Create", "Create"),
                        initial:      "",
                        maxChars:     64,
                        rejectEmpty:  true,
                        onConfirm:    n => GuildHandler.CreateGuild(n)));
                }
                if (KmhBrand.Button(new Rect(createW + 8f, y + 58f, joinW, 30f), join, false))
                {
                    Find.WindowStack.Add(new Dialog_KMHGuildPicker());
                }
                y += 96f;

                List<GuildInviteDto> invites = GuildCache.Envelope?.MyInvites;
                DialogLayout.DrawSectionDivider(rect, ref y);
                DialogLayout.LabelTrunc(new Rect(0f, y + 4f, rect.width, 24f), "<b>" + KmhText.Get("KMH_GH_InvitesTitle", "Guild invites for you") + "</b>");
                y += 30f;
                if (invites == null || invites.Count == 0)
                {
                    DialogLayout.LabelTrunc(new Rect(0f, y, rect.width, 22f),
                        "<color=#A9AFB9>" + KmhText.Get("KMH_GH_NoInvites", "No pending invites. When a guild invites you, it appears here.") + "</color>");
                }
                else
                {
                    const float invH = 34f;
                    float listH = Mathf.Min(invites.Count * invH, Mathf.Max(invH, rect.height - y - DialogLayout.FooterReserve - 4f));
                    Rect listRect = new Rect(0f, y, rect.width, listH);
                    bool scrolls = invites.Count * invH > listH + 0.5f;
                    Rect view = new Rect(0f, 0f, rect.width - (scrolls ? DialogLayout.ScrollbarReserveWidth : 0f), invites.Count * invH);
                    Widgets.BeginScrollView(listRect, ref _invitesScroll, view);
                    float iy = 0f;
                    foreach (GuildInviteDto inv in invites)
                    {
                        Rect row = new Rect(0f, iy, view.width, 30f);
                        Widgets.DrawLightHighlight(row);
                        string by = string.IsNullOrEmpty(inv.Inviter) ? ""
                            : "  <color=#A9AFB9>" + KmhText.Get("KMH_GH_InvitedBy", "invited by {0}", KmhDisplayText.Inert(inv.Inviter, 48)) + "</color>";
                        DialogLayout.LabelTrunc(new Rect(6f, iy + 5f, view.width - 200f, 22f),
                            $"<b>{KmhDisplayText.GuildName(inv.GuildName)}</b>  <color=#A9AFB9>({MemberCount(inv.Members)})</color>{by}");
                        if (KmhBrand.Button(new Rect(view.width - 190f, iy + 2f, 90f, 26f), KmhText.Get("KMH_Guild_Accept", "Accept"), true))
                            GuildHandler.TryJoin(inv.GuildName);
                        if (KmhBrand.Button(new Rect(view.width - 94f, iy + 2f, 90f, 26f), KmhText.Get("KMH_Guild_Decline", "Decline"), false))
                            GuildHandler.TryDeclineInvite(inv.GuildName);
                        iy += invH;
                    }
                    Widgets.EndScrollView();
                    y += listH;
                }
                y += 30f;
                DrawWhyAGuild(new Rect(0f, y, rect.width, Mathf.Max(0f, rect.height - y - 44f)));
                if (DialogLayout.DrawCloseButton(rect)) Close();
                return;
            }

            GuildSnapshot g = GuildCache.Guild;
            DialogLayout.DrawSectionDivider(rect, ref y);

            string motd = KmhDisplayText.Motd(g.Motd);
            string myRank = MyRank(g);
            bool isAdmin = IsAdminRank(myRank);
            string editMotd = KmhText.Get("KMH_GH_EditMotd", "Edit MOTD");
            float motdEditW = isAdmin ? DialogLayout.ButtonW(editMotd, 110f, 200f) : 0f;
            DialogLayout.LabelTrunc(new Rect(0f, y, rect.width - motdEditW - 4f, 22f), "<b>" + KmhText.Get("KMH_GH_Motd", "MOTD:") + "</b> " + motd);
            if (isAdmin)
            {
                if (KmhBrand.Button(new Rect(rect.width - motdEditW, y - 2f, motdEditW - 4f, 26f), editMotd, false))
                {
                    Find.WindowStack.Add(new Dialog_KMHTextInput(
                        title:        KmhText.Get("KMH_GH_EditMotdTitle", "Edit guild MOTD"),
                        confirmLabel: KmhText.Get("KMH_GH_Save", "Save"),
                        initial:      g.Motd ?? "",
                        maxChars:     256,
                        rejectEmpty:  false,
                        onConfirm:    s => GuildHandler.TrySetMotd(s)));
                }
            }
            y += 26f;

            string diplomacy = FormatDiplomacy(g.Relationships, g.IncomingProposals);
            Color oldCol = GUI.color;
            GUI.color = DialogLayout.MutedColor;
            string diplomacyLabel = KmhText.Get("KMH_GH_DiplomacyMenu", "Diplomacy ▾");
            float diplomacyBtnW = isAdmin ? DialogLayout.ButtonW(diplomacyLabel, 130f, 220f) : 0f;
            Rect diplomacyRow = new Rect(0f, y, rect.width - diplomacyBtnW - 4f, 20f);
            DialogLayout.LabelTrunc(diplomacyRow, diplomacy);
            if (Mouse.IsOver(diplomacyRow)) TooltipHandler.TipRegion(diplomacyRow, DiplomacyTip);
            GUI.color = oldCol;
            if (isAdmin)
            {
                if (KmhBrand.Button(new Rect(rect.width - diplomacyBtnW, y - 4f, diplomacyBtnW - 4f, 26f), diplomacyLabel, false))
                {
                    OpenDiplomacyMenu(g);
                }
            }
            y += 24f;
            if (g.IncomingProposals != null && g.IncomingProposals.Count > 0)
            {
                DrawIncomingProposal(new Rect(0f, y, rect.width, 26f), g.IncomingProposals, isAdmin);
                y += 30f;
            }

            // Shown even when the server enables no Guild Hall rules, since the status is still informative.
            {
                bool hasHall = g.Hall != null && g.Hall.HasHall;
                string hallLabel = "<b>" + KmhText.Get("KMH_GH_HallLabel", "Guild Hall:") + "</b> ";
                string hallText = hasHall
                    ? hallLabel + "<color=#4cc38a>" + KmhText.Get("KMH_GH_HallSet", "set at world tile {0}", g.Hall.Tile) + "</color> <color=#A9AFB9>"
                      + KmhText.Get("KMH_GH_HallRadius", "(access radius {0})", g.Hall.RadiusTiles) + "</color>"
                    : hallLabel + "<color=#A9AFB9>" + KmhText.Get("KMH_GH_HallNotSet", "not set (only needed if the server enables Guild Hall rules)") + "</color>";
                string jumpLabel = KmhText.Get("KMH_GH_JumpToHall", "Jump to Hall");
                string hallMenuLabel = hasHall ? KmhText.Get("KMH_GH_MoveHall", "Move / Remove Hall ▾") : KmhText.Get("KMH_GH_SetHall", "Set Hall ▾");
                float jumpW    = hasHall ? DialogLayout.ButtonW(jumpLabel, 110f, 200f) : 0f;
                float hallBtnW = isAdmin ? DialogLayout.ButtonW(hallMenuLabel, 160f, 260f) : 0f;
                DialogLayout.LabelTrunc(new Rect(0f, y, rect.width - hallBtnW - jumpW - 8f, 22f), hallText);
                if (hasHall && KmhBrand.Button(new Rect(rect.width - hallBtnW - jumpW - 4f, y - 2f, jumpW - 4f, 26f), jumpLabel, false))
                {
                    Close(false);
                    CameraJumper.TryJump(new GlobalTargetInfo(g.Hall.Tile));
                }
                if (isAdmin)
                {
                    if (KmhBrand.Button(new Rect(rect.width - hallBtnW, y - 2f, hallBtnW - 4f, 26f), hallMenuLabel, false))
                        Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption>
                        {
                            new FloatMenuOption(KmhText.Get("KMH_GH_HallViaMap", "Set via map (pick a world tile)"), BeginHallViaMap),
                            new FloatMenuOption(KmhText.Get("KMH_GH_HallHere", "Set at my current colony/caravan tile"), () => GuildHandler.TrySetHall()),
                            new FloatMenuOption(KmhText.Get("KMH_GH_HallRemove", "Remove Guild Hall"), () => { if (hasHall) GuildHandler.TryRemoveHall(); }),
                        }));
                }
                y += 26f;
            }

            // The server enforces rank; hiding a no-op affordance only keeps the row clean.
            bool canInvite = isAdmin
                          || string.Equals(myRank, GuildMemberDto.RankModerator, StringComparison.OrdinalIgnoreCase);
            const float bh = 28f;
            float ax = 0f;
            if (canInvite)
            {
                string invite = KmhText.Get("KMH_GH_InvitePlayer", "Invite player");
                float inviteW = DialogLayout.ButtonW(invite, 130f, 220f);
                if (KmhBrand.Button(new Rect(ax, y, inviteW, bh), invite, false))
                    Find.WindowStack.Add(new Dialog_KMHPlayerPicker(n => GuildHandler.TryInvite(n)));
                ax += inviteW + 8f;
            }
            if (isAdmin)
            {
                // A button, not a checkbox: one deliberate click is one request, debounced while a snapshot is in flight.
                string joinLabel = g.OpenJoin ? KmhText.Get("KMH_GH_SetInviteOnly", "Set Invite-only") : KmhText.Get("KMH_GH_SetOpen", "Set Open to join");
                float joinW = DialogLayout.ButtonW(joinLabel, 150f, 240f);
                if (KmhBrand.Button(new Rect(ax, y, joinW, bh), joinLabel, false) && DebounceOk(ref _lastJoinToggleUtc))
                    GuildHandler.TrySetOpenJoin(!g.OpenJoin);
                ax += joinW + 8f;
            }
            // Right-anchored against a left cursor, so it reserves its own space or a narrow window overdraws the button.
            string refresh = KmhText.Get("KMH_GH_Refresh", "Refresh");
            float refreshW = Mathf.Clamp(rect.width - ax - 8f, 0f, DialogLayout.ButtonW(refresh, 96f, 160f));
            bool  showRefresh = refreshW >= 60f;
            float statusW = Mathf.Max(0f, rect.width - ax - (showRefresh ? refreshW + 12f : 0f));

            DialogLayout.LabelTrunc(new Rect(ax, y + 5f, statusW, 20f),
                g.OpenJoin ? "<color=#4cc38a>" + KmhText.Get("KMH_GH_AnyoneCanJoin", "Anyone can join") + "</color>"
                           : "<color=#A9AFB9>" + KmhText.Get("KMH_GH_InviteOnly", "Invite-only") + "</color>");
            if (showRefresh && KmhBrand.Button(new Rect(rect.width - refreshW - 4f, y, refreshW, bh), refresh, false))
            {
                GuildHandler.RequestSnapshot();
            }
            y += 34f;

            float bx = 0f;
            if (g.GuildTreasuryEnabled)
            {
                string donate = KmhText.Get("KMH_GH_DonateSilver", "Donate silver"), withdraw = KmhText.Get("KMH_GH_WithdrawSilver", "Withdraw silver");
                float donateW = DialogLayout.ButtonW(donate, 120f, 220f), withdrawW = DialogLayout.ButtonW(withdraw, 130f, 220f);
                if (KmhBrand.Button(new Rect(bx, y, donateW, bh), donate, false))
                    Find.WindowStack.Add(new Dialog_KMHAmountInput(KmhText.Get("KMH_GH_DonateTitle", "Donate to guild vault"), KmhText.Get("KMH_GH_Donate", "Donate"),
                        KmhText.Get("KMH_GH_DonateUnit", "silver (from your personal Treasury)"), 0,
                        qty => GuildHandler.TryDonate(qty)));
                bx += donateW + 8f;
                if (KmhBrand.Button(new Rect(bx, y, withdrawW, bh), withdraw, false))
                    Find.WindowStack.Add(new Dialog_KMHAmountInput(KmhText.Get("KMH_Guild_VaultWithdrawTitle", "Withdraw from guild vault"), KmhText.Get("KMH_GH_Withdraw", "Withdraw"),
                        KmhText.Get("KMH_GH_WithdrawUnit", "silver (to your personal Treasury; daily cap by rank)"), g.GuildSilver > int.MaxValue ? int.MaxValue : (int)g.GuildSilver,
                        qty => GuildHandler.TryWithdrawFromGuild(qty)));
                bx += withdrawW + 8f;
            }
            string leave = KmhText.Get("KMH_GH_LeaveGuild", "Leave guild");
            if (KmhBrand.Button(new Rect(bx, y, DialogLayout.ButtonW(leave, 100f, 200f), bh), leave, false))
                Find.WindowStack.Add(Verse.Dialog_MessageBox.CreateConfirmation(
                    KmhText.Get("KMH_GH_LeaveConfirm", "Leave this guild? You lose your rank and contribution credit. Guild vault/sites/perks are not deleted. The guild Owner must transfer ownership or disband before leaving."),
                    () => GuildHandler.TryLeave()));
            y += 34f;

            long myDonated = 0;
            GuildMemberDto meMem = g.Members?.Find(x => KmhSession.IsMe(x.Username));
            if (meMem != null) myDonated = meMem.SilverContributed;
            string vaultLabel = "<b>" + KmhText.Get("KMH_GH_VaultLabel", "Guild vault:") + "</b> ";
            string vaultText = g.GuildTreasuryEnabled
                ? vaultLabel + $"<color={UI.KmhTheme.Hex(UI.KmhTheme.Accent)}>" + KmhText.Get("KMH_GH_SilverAmount", "{0} silver", g.GuildSilver.ToString("N0")) + "</color>    <b>"
                  + KmhText.Get("KMH_GH_YourDonated", "Your donated:") + "</b> " + myDonated.ToString("N0")
                : vaultLabel + "<color=#A9AFB9>" + KmhText.Get("KMH_GH_VaultDisabled", "disabled by server") + "</color>";
            DialogLayout.LabelTrunc(new Rect(0f, y, rect.width, 20f), vaultText);
            y += 24f;

            if (g.GuildTreasuryEnabled)
            {
                long vaultUnits = VaultUnits(g);
                bool mayTake = MayWithdrawFromVault(g, myRank) && vaultUnits > 0;
                float takeW = mayTake ? 150f : 0f;
                Rect itemsRow = new Rect(0f, y, rect.width - takeW - 4f, 20f);
                DialogLayout.LabelTrunc(itemsRow, VaultItemsSummary(g, ItemLabels.ResolveLabel, 4));
                if (vaultUnits > 0 && Mouse.IsOver(itemsRow))
                    TooltipHandler.TipRegion(itemsRow, VaultItemsSummary(g, ItemLabels.ResolveLabel, int.MaxValue));
                if (mayTake)
                {
                    Rect takeR = new Rect(rect.width - takeW, y - 3f, takeW - 4f, 24f);
                    if (KmhBrand.Button(takeR, KmhText.Get("KMH_GH_WithdrawItems", "Withdraw items ▾"), false)) OpenVaultItemPicker(g);
                    TooltipHandler.TipRegion(takeR, KmhText.Get("KMH_Guild_VaultWithdrawTip", "Delivered to your colony. Your rank sets a daily cap."));
                }
                y += 24f;
            }

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
                string mine = pendingMine > 0
                    ? "    <b>" + KmhText.Get("KMH_GH_Yours", "Yours:") + "</b> " + pendingMine.ToString("N0") + " <color=#A9AFB9>"
                      + KmhText.Get("KMH_GH_SaveToFinalize", "(save your game to finalize)") + "</color>"
                    : "";
                DialogLayout.LabelTrunc(new Rect(0f, y, rect.width, 18f),
                    "<color=#A9AFB9>" + KmhText.Get("KMH_GH_PendingDonations", "Pending donations:") + "</color> "
                    + KmhText.Get("KMH_GH_SilverAmount", "{0} silver", g.PendingDonationsSilver.ToString("N0")) + " <color=#A9AFB9>"
                    + KmhText.Get("KMH_GH_NotSpendable", "(not spendable yet)") + "</color>" + mine);
                y += 22f;
            }

            // The extra 120f is the settings overview, which sits below both columns.
            float paneH  = DialogLayout.BodyHeight(rect, y + 120f);
            float leftW  = rect.width * 0.58f - 4f;
            float rightX = leftW + 8f;
            float rightW = rect.width - rightX;

            DialogLayout.LabelTrunc(new Rect(0f, y, leftW, 20f), "<b>" + KmhText.Get("KMH_GH_Members", "Members") + $"</b>  <color=#A9AFB9>({g.Members?.Count ?? 0})</color>");
            Rect membersBox = new Rect(0f, y + 22f, leftW, paneH - 22f);
            Widgets.DrawMenuSection(membersBox);
            DrawMembersList(membersBox, g);

            DialogLayout.LabelTrunc(new Rect(rightX, y, rightW, 20f), "<b>" + KmhText.Get("KMH_GH_Perks", "Perks") + "</b>  <color=#A9AFB9>· "
                + KmhText.Get("KMH_GH_GuildSilver", "guild silver {0}", g.GuildSilver.ToString("N0")) + "</color>");
            Rect perksBox = new Rect(rightX, y + 22f, rightW, paneH - 22f);
            Widgets.DrawMenuSection(perksBox);
            DrawPerksList(perksBox, g.Perks, g.GuildSilver);

            float settingsY = y + paneH + 6f;
            DrawSettingsOverview(new Rect(0f, settingsY, rect.width, 110f), g.Settings);

            if (DialogLayout.DrawCloseButton(rect)) Close();
        }

        // Mirror of the server's per-rank cap: 0 means the rank may not take anything out, silver or goods.
        internal static bool MayWithdrawFromVault(GuildSnapshot g, string rank)
        {
            if (g?.Settings == null || string.IsNullOrEmpty(rank)) return false;
            switch (rank)
            {
                case GuildMemberDto.RankOwner:     return true;
                case GuildMemberDto.RankAdmin:     return g.Settings.AdminDailyWithdrawCap != 0;
                case GuildMemberDto.RankModerator: return g.Settings.ModeratorDailyWithdrawCap != 0;
                case GuildMemberDto.RankOfficer:   return g.Settings.OfficerDailyWithdrawCap != 0;
                default:                           return g.Settings.MemberDailyWithdrawCap != 0;
            }
        }

        internal static long VaultUnits(GuildSnapshot g)
        {
            long n = 0;
            if (g?.GuildItems != null) foreach (KeyValuePair<string, int> kv in g.GuildItems) if (kv.Value > 0) n += kv.Value;
            if (g?.GuildPayloads != null) foreach (KMHPatch.Items.KmhThingPayload p in g.GuildPayloads) if (p != null && p.StackCount > 0) n += p.StackCount;
            return n;
        }

        // Largest stacks first, so the line names what matters when the vault holds more kinds than fit.
        internal static string VaultItemsSummary(GuildSnapshot g, Func<string, string> labelFor, int maxNamed)
        {
            var rows = new List<(string Label, long Count)>();
            if (g?.GuildItems != null)
                foreach (KeyValuePair<string, int> kv in g.GuildItems)
                    if (kv.Value > 0) rows.Add((labelFor?.Invoke(kv.Key) ?? kv.Key, kv.Value));
            if (g?.GuildPayloads != null)
                foreach (KMHPatch.Items.KmhThingPayload p in g.GuildPayloads)
                    if (p != null && p.StackCount > 0) rows.Add((labelFor?.Invoke(p.DefName) ?? p.DefName, p.StackCount));
            string label = "<b>" + KmhText.Get("KMH_GH_VaultItems", "Vault items:") + "</b> ";
            if (rows.Count == 0) return label + "<color=#A9AFB9>" + KmhText.Get("KMH_GH_None", "none") + "</color>";
            rows.Sort((a, b) => b.Count.CompareTo(a.Count));
            var named = new List<string>();
            for (int i = 0; i < rows.Count && i < maxNamed; i++) named.Add($"{rows[i].Count:N0}× {KmhDisplayText.Inert(rows[i].Label)}");
            string more = rows.Count > maxNamed ? " <color=#A9AFB9>" + KmhText.Get("KMH_GH_PlusMore", "(+{0} more)", rows.Count - maxNamed) + "</color>" : "";
            return label + string.Join(", ", named) + more;
        }

        private static void OpenVaultItemPicker(GuildSnapshot g)
        {
            UI.KmhItemPickerService.Open(
                title:           KmhText.Get("KMH_Guild_VaultWithdrawTitle", "Withdraw from guild vault"),
                pickActionLabel: KmhText.Get("KMH_GH_Withdraw", "Withdraw"),
                source:          g.GuildItems ?? new Dictionary<string, int>(),
                onPick:          (key, qty) => GuildHandler.TryWithdrawGuildItem(key, qty),
                refreshSource:   () => GuildCache.Guild?.GuildItems,
                payloads:        g.GuildPayloads,
                onPickPayload:   (pl, qty) => GuildHandler.TryWithdrawGuildPayload(pl.Fingerprint, qty),
                refreshPayloads: () => GuildCache.Guild?.GuildPayloads,
                intoThisGame:    true);
        }

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

            // Cached against the snapshot's own list so a large roster is not re-sorted every frame.
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

                const float actionW = 130f, nameX = 100f;
                float rest  = viewRect.width - nameX - actionW - 6f;
                float nameW = rest * 0.38f;
                string rankChip = $"<color={RankColor(m.Rank)}>{FriendlyRank(m.Rank)}</color>";
                DialogLayout.LabelTrunc(new Rect(6f, ly + 4f, 90f, rowH - 8f), rankChip);
                // Unwrapped: bold measures a pixel short, and a "[SIM] name" that just fit broke onto a second line.
                bool wrap = Text.WordWrap;
                Text.WordWrap = false;
                Rect nameR = new Rect(nameX, ly + 4f, nameW - 6f, rowH - 8f);
                DialogLayout.LabelTrunc(nameR, $"<b>{LinkedAccountsCache.Format(m.Username)}</b>");
                Text.WordWrap = wrap;
                if (!KmhSession.IsMe(m.Username)) KmhPlayerMenu.LinkRegion(nameR, m.Username);
                Color oldCol = GUI.color;
                GUI.color = DialogLayout.MutedColor;
                Rect stats = new Rect(nameX + nameW, ly + 4f, rest - nameW, rowH - 8f);
                string statsText = m.QuestsCompleted == 1
                    ? KmhText.Get("KMH_GH_MemberStatsOne", "{0} donated • 1 quest", SilverFmt.Format(m.SilverContributed))
                    : KmhText.Get("KMH_GH_MemberStats", "{0} donated • {1} quests", SilverFmt.Format(m.SilverContributed), m.QuestsCompleted);
                DialogLayout.LabelTrunc(stats, statsText);
                if (Mouse.IsOver(stats) && Text.CalcSize(statsText).x <= stats.width)
                    TooltipHandler.TipRegion(stats, KmhText.Get("KMH_GH_MemberStatsTip", "{0} donated to the guild vault, {1} quest(s) completed",
                        SilverFmt.Format(m.SilverContributed), m.QuestsCompleted));
                GUI.color = oldCol;

                DrawMemberActionButton(
                    new Rect(viewRect.width - actionW + 2f, ly + 2f, actionW - 6f, rowH - 4f),
                    m, myRank, mine);
            }
            if (rows.Count == 0)
            {
                DialogLayout.LabelTrunc(new Rect(6f, 6f, viewRect.width, 20f),
                    "<color=#A9AFB9>" + KmhText.Get("KMH_GH_NoMembers", "No members.") + "</color>");
            }
            Widgets.EndScrollView();
        }

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

        // Picked by name from the leaderboard snapshot, never typed, so a misspelling cannot reach the server.
        private static List<FloatMenuOption> OtherGuildOptions(GuildSnapshot g, string verb, Action<string> act)
        {
            List<FloatMenuOption> outp = new List<FloatMenuOption>();
            var rows = GuildLeaderboardCache.Snapshot?.Guilds;
            if (rows != null)
                foreach (var row in rows)
                {
                    if (row == null || string.IsNullOrEmpty(row.Name)) continue;
                    if (string.Equals(row.Name, g?.Name, StringComparison.OrdinalIgnoreCase)) continue;
                    string relText = RelationLabel(g, row.Name);
                    string rel = relText.Length > 0 ? $"  <color=#A9AFB9>({relText})</color>" : "";
                    string captured = row.Name;
                    outp.Add(new FloatMenuOption($"{verb} {KmhDisplayText.GuildName(row.Name)} ({MemberCount(row.MemberCount)}){rel}", () => act(captured)));
                }
            if (outp.Count == 0) outp.Add(new FloatMenuOption(KmhText.Get("KMH_GH_NoOtherGuilds", "No other guilds available."), null));
            return outp;
        }

        private void OpenDiplomacyMenu(GuildSnapshot g)
        {
            GuildHandler.RequestLeaderboard();   // refresh the guild list for next open; current cache serves this one
            List<FloatMenuOption> opts = new List<FloatMenuOption>();

            opts.Add(new FloatMenuOption(KmhText.Get("KMH_GH_ProposeWith", "Propose alliance with"),
                () => Find.WindowStack.Add(new FloatMenu(OtherGuildOptions(g, KmhText.Get("KMH_GH_AllyWith", "Ally with"), n => GuildHandler.TryProposeAlliance(n))))));
            opts.Add(new FloatMenuOption(KmhText.Get("KMH_Guild_MenuRival", "Mark as a rival (a label only, no effect on play)"),
                () => Find.WindowStack.Add(new FloatMenu(OtherGuildOptions(g, KmhText.Get("KMH_Guild_MenuRivalVerb", "Rival:"), n => GuildHandler.TryDeclareHostile(n))))));

            if (g?.IncomingProposals != null)
                foreach (string from in g.IncomingProposals)
                {
                    if (string.IsNullOrEmpty(from)) continue;
                    string other = from, shown = KmhDisplayText.GuildName(from);
                    opts.Add(new FloatMenuOption(KmhText.Get("KMH_Guild_MenuAccept", "Accept the alliance from {0}", shown),
                        () => GuildHandler.TryAcceptAlliance(other)));
                    opts.Add(new FloatMenuOption(KmhText.Get("KMH_Guild_MenuDecline", "Decline the alliance from {0}", shown),
                        () => GuildHandler.TryDeclineAlliance(other)));
                }

            if (g?.Relationships != null)
            {
                foreach (KeyValuePair<string, string> kv in g.Relationships)
                {
                    string other = kv.Key;
                    string shown = KmhDisplayText.GuildName(other);
                    switch (kv.Value)
                    {
                        case GuildSnapshot.RelationAlliedRequested:
                            opts.Add(new FloatMenuOption(KmhText.Get("KMH_Guild_MenuCancel", "Proposal sent to {0} - cancel it", shown),
                                () => GuildHandler.TryCancelProposal(other)));
                            break;
                        case GuildSnapshot.RelationAllied:
                            opts.Add(new FloatMenuOption(KmhText.Get("KMH_Guild_MenuBreak", "End the alliance with {0}", shown),
                                () => GuildHandler.TryBreakAlliance(other)));
                            break;
                        case GuildSnapshot.RelationHostile:
                            opts.Add(new FloatMenuOption(KmhText.Get("KMH_Guild_MenuClearRival", "Clear the rival mark on {0}", shown),
                                () => GuildHandler.TryClearHostile(other)));
                            break;
                    }
                }
            }

            Find.WindowStack.Add(new FloatMenu(opts));
        }

        private static string DiplomacyTip => KmhText.Get("KMH_Guild_DiplomacyTip",
            "An alliance needs both guilds: one proposes, the other accepts, and either can end it. Ending it ends it for both, so allying again takes a new proposal. A rival mark is only a label and has no effect on play.");

        private static void DrawIncomingProposal(Rect row, List<string> from, bool canAnswer)
        {
            string first = from[0];
            string more = from.Count > 1 ? " " + KmhText.Get("KMH_Guild_IncomingMore", "(and {0} more under Diplomacy)", from.Count - 1) : "";
            string accept = KmhText.Get("KMH_Guild_Accept", "Accept"), decline = KmhText.Get("KMH_Guild_Decline", "Decline");
            float acceptW = DialogLayout.ButtonW(accept, 90f, 160f), declineW = DialogLayout.ButtonW(decline, 90f, 160f);
            float buttonsW = canAnswer ? acceptW + declineW + 6f : 0f;
            Widgets.DrawLightHighlight(row);
            string text = KmhText.Get("KMH_Guild_IncomingRow", "{0} proposes an alliance with your guild.", $"<b>{KmhDisplayText.GuildName(first)}</b>") + more;
            if (!canAnswer) text += "  <color=#A9AFB9>" + KmhText.Get("KMH_Guild_IncomingAdminOnly", "An Admin or the Owner can answer it.") + "</color>";
            DialogLayout.LabelTrunc(new Rect(row.x + 6f, row.y + 2f, Mathf.Max(1f, row.width - buttonsW - 12f), 22f), text);
            if (!canAnswer) return;
            if (KmhBrand.Button(new Rect(row.xMax - buttonsW, row.y + 1f, acceptW, 24f), accept, true))
                GuildHandler.TryAcceptAlliance(first);
            if (KmhBrand.Button(new Rect(row.xMax - declineW, row.y + 1f, declineW, 24f), decline, false))
                GuildHandler.TryDeclineAlliance(first);
        }

        internal static string RelationLabel(GuildSnapshot g, string other)
        {
            if (g == null || string.IsNullOrEmpty(other)) return "";
            if (g.IncomingProposals != null && g.IncomingProposals.Exists(n => string.Equals(n, other, StringComparison.OrdinalIgnoreCase)))
                return KmhText.Get("KMH_Guild_RelIncoming", "proposes an alliance to you");
            string r = null;
            if (g.Relationships != null)
                foreach (KeyValuePair<string, string> kv in g.Relationships)
                    if (string.Equals(kv.Key, other, StringComparison.OrdinalIgnoreCase)) { r = kv.Value; break; }
            switch (r)
            {
                case GuildSnapshot.RelationAllied:          return KmhText.Get("KMH_Guild_RelAllied", "allied");
                case GuildSnapshot.RelationAlliedRequested: return KmhText.Get("KMH_Guild_RelSent", "proposal sent");
                case GuildSnapshot.RelationHostile:         return KmhText.Get("KMH_Guild_RelRival", "rival");
                default:                                    return "";
            }
        }

        private static void DrawMemberActionButton(Rect rect, GuildMemberDto target, string myRank, string mine)
        {
            if (string.IsNullOrEmpty(myRank)) return;
            if (KmhSession.Same(target.Username, mine)) return;

            int myOrder     = RankOrder(myRank);
            int targetOrder = RankOrder(target.Rank);

            if (myOrder > RankOrder(GuildMemberDto.RankModerator)) return;
            if (targetOrder <= myOrder) return;

            if (KmhBrand.Button(rect, KmhText.Get("KMH_GH_Manage", "Manage ▾"), false))
            {
                List<FloatMenuOption> opts = new List<FloatMenuOption>();

                // Mirrors the server rule: the new rank must stay strictly below the actor's, and Owner only via transfer.
                int promotedOrder = targetOrder - 1;
                if (promotedOrder > myOrder)
                    opts.Add(new FloatMenuOption(KmhText.Get("KMH_GH_PromoteTo", "Promote to {0}", FriendlyRank(RankFromOrder(promotedOrder))),
                        () => GuildHandler.TryPromote(target.Username)));
                if (targetOrder < RankOrder(GuildMemberDto.RankMember))
                    opts.Add(new FloatMenuOption(KmhText.Get("KMH_GH_DemoteTo", "Demote to {0}", FriendlyRank(RankFromOrder(targetOrder + 1))),
                        () => GuildHandler.TryDemote(target.Username)));
                opts.Add(new FloatMenuOption(KmhText.Get("KMH_GH_Kick", "Kick from guild"),
                    () => GuildHandler.TryKick(target.Username)));
                // The only way anyone becomes Owner; the outgoing owner drops to Admin.
                if (string.Equals(myRank, GuildMemberDto.RankOwner, StringComparison.OrdinalIgnoreCase))
                    opts.Add(new FloatMenuOption(TransferLabel(target.Username),
                        () => Find.WindowStack.Add(Verse.Dialog_MessageBox.CreateConfirmation(
                            TransferConfirm(target.Username), () => GuildHandler.TryTransferOwner(target.Username)))));

                Find.WindowStack.Add(new FloatMenu(opts));
            }
        }

        // Client mirror of the server perk cost ladder (GuildPerksDto.CostFor): levels 1/2/3 cost 5k/15k/30k.
        private static long NextPerkCost(int currentLevel) => currentLevel <= 0 ? 5000 : (currentLevel == 1 ? 15000 : 30000);

        private void DrawPerksList(Rect box, GuildPerksDto p, long guildSilver)
        {
            Rect inner = box.ContractedBy(DialogLayout.ListInnerPad);
            float rowH = DialogLayout.TextRowsH(3, 8f);
            bool canBuy = IsAdminRank(MyRank(GuildCache.Guild));

            (string label, int level, string effect, string key)[] perks = new[]
            {
                (KmhText.Get("KMH_GH_PerkWorkers", "Site Max Workers"), p.SiteMaxWorkersBonusLevel,
                 KmhText.Get("KMH_GH_PerkWorkersFx", "+{0} workers/site", p.SiteMaxWorkersBonusLevel * 2), GuildHandler.PerkSiteMaxWorkers),
                (KmhText.Get("KMH_GH_PerkTax", "Market Tax Cut"), p.MarketplaceTaxReductionLevel,
                 KmhText.Get("KMH_GH_PerkTaxFx", "-{0}% market tax", p.MarketplaceTaxReductionLevel), GuildHandler.PerkMarketplaceTaxCut),
                (KmhText.Get("KMH_GH_PerkXp", "Worker XP Bonus"), p.WorkerXpBonusLevel,
                 KmhText.Get("KMH_GH_PerkXpFx", "x{0} XP", WorkerXpMultiplierText(p.WorkerXpBonusLevel)), GuildHandler.PerkWorkerXpBonus),
                (KmhText.Get("KMH_GH_PerkCost", "Custom Site Cost"), p.CustomSiteCostDiscountLevel,
                 KmhText.Get("KMH_GH_PerkCostFx", "-{0}% build cost", p.CustomSiteCostDiscountLevel * 10), GuildHandler.PerkCustomSiteCostCut),
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

                const float btnW = 80f;
                float textW = viewRect.width - btnW - 16f;

                string lv = KmhText.Get("KMH_GH_PerkLevel", "(Lv {0}/{1})", level, GuildPerksDto.MaxLevel);
                DialogLayout.LabelTrunc(new Rect(6f, ly + 4f, textW, DialogLayout.TextRowH),
                    $"<b>{label}</b>  <color=#A9AFB9>{lv}</color>");
                DialogLayout.LabelTrunc(new Rect(6f, ly + 4f + DialogLayout.TextRowH, textW, DialogLayout.TextRowH), $"<color=#A9AFB9>{effect}</color>");
                string costLine;
                if (level >= GuildPerksDto.MaxLevel) costLine = "<color=#A9AFB9>" + KmhText.Get("KMH_GH_PerkMaxed", "maxed") + "</color>";
                else
                {
                    long nextCost = NextPerkCost(level);
                    string next = "<color=#A9AFB9>" + KmhText.Get("KMH_GH_PerkNext", "next: {0}", SilverFmt.Format(nextCost)) + "</color> ";
                    costLine = guildSilver < nextCost
                        ? next + "<color=#f07167>" + KmhText.Get("KMH_GH_PerkNeed", "need {0} more", SilverFmt.Format(nextCost - guildSilver)) + "</color>"
                        : next + "<color=#4cc38a>" + KmhText.Get("KMH_GH_PerkAffordable", "affordable") + "</color>";
                }
                DialogLayout.LabelTrunc(new Rect(6f, ly + 4f + DialogLayout.TextRowH * 2f, textW, DialogLayout.TextRowH), costLine);
                if (level < GuildPerksDto.MaxLevel)
                    TooltipHandler.TipRegion(new Rect(0f, ly, viewRect.width, rowH),
                        label + "\n" + KmhText.Get("KMH_GH_PerkTipNow", "Current: {0} {1}", effect, lv) + "\n"
                        + KmhText.Get("KMH_GH_PerkTipNext", "Next level costs {0} from the guild vault (balance {1}).",
                            SilverFmt.Format(NextPerkCost(level)), SilverFmt.Format(guildSilver)));

                if (canBuy)
                {
                    Rect btn = new Rect(viewRect.width - btnW - 4f, ly + (rowH - 28f) / 2f, btnW, 28f);
                    if (level >= GuildPerksDto.MaxLevel)
                    {
                        DialogLayout.DrawCenteredLabel(btn, "<color=#A9AFB9>" + KmhText.Get("KMH_GH_PerkMax", "MAX") + "</color>");
                    }
                    else
                    {
                        string capturedKey = key;
                        // Filled only when the vault can pay; the server still refuses a short one.
                        if (KmhBrand.Button(btn, KmhText.Get("KMH_GH_Buy", "Buy"), guildSilver >= NextPerkCost(level))) GuildHandler.TryBuyPerk(capturedKey);
                    }
                }

                ly += rowH;
            }
            Widgets.EndScrollView();
        }

        private static void DrawSettingsOverview(Rect r, GuildSettingsDto s)
        {
            Widgets.DrawMenuSection(r);
            Rect inner = r.ContractedBy(8f);
            Color oldCol = GUI.color;

            bool isAdmin = IsAdminRank(MyRank(GuildCache.Guild));
            string edit = KmhText.Get("KMH_GH_Edit", "Edit");
            float editW = isAdmin ? DialogLayout.ButtonW(edit, 100f, 180f) : 0f;
            DialogLayout.LabelTrunc(new Rect(inner.x, inner.y, inner.width - editW - 4f, DialogLayout.TextRowH), "<b>" + KmhText.Get("KMH_GH_Settings", "Settings") + "</b>");
            if (isAdmin)
            {
                if (KmhBrand.Button(new Rect(inner.xMax - editW, inner.y - 2f, editW - 4f, 22f), edit, false))
                {
                    Find.WindowStack.Add(new Dialog_KMHGuildSettings(s));
                }
            }

            GUI.color = DialogLayout.MutedColor;
            DialogLayout.LabelTrunc(new Rect(inner.x, inner.y + DialogLayout.TextRowH, inner.width, DialogLayout.TextRowH),
                KmhText.Get("KMH_GH_SettingsTax", "Guild tax to vault - site rewards: {0}%  •  member sales: {1}%  •  Default listings: {2}",
                    s.SiteRewardSilverTaxPercent, s.MarketplaceSaleTaxPercent,
                    s.DefaultListingsGuildOnly ? KmhText.Get("KMH_GH_GuildOnly", "guild-only") : KmhText.Get("KMH_GH_Public", "public")));

            // Both shown so a 0% guild tax is not misread as no server tax; this is the configured rate, before events.
            int serverTax = Features.Marketplace.MarketplaceCache.Snapshot?.ServerTaxPercent ?? 0;
            int perkCut   = GuildCache.Guild?.Perks?.MarketplaceTaxReductionLevel ?? 0;
            DialogLayout.LabelTrunc(new Rect(inner.x, inner.y + DialogLayout.TextRowH * 2f, inner.width, DialogLayout.TextRowH),
                KmhText.Get("KMH_GH_SettingsMarket", "Server market tax: {0}%  •  guild perk reduction: -{1}%  •  effective: {2}% (before world events)",
                    serverTax, perkCut, Math.Max(0, serverTax - perkCut)));

            DialogLayout.LabelTrunc(new Rect(inner.x, inner.y + DialogLayout.TextRowH * 3f, inner.width, DialogLayout.TextRowH),
                KmhText.Get("KMH_GH_SettingsCaps", "Daily withdraw caps - Member: {0}  •  Officer: {1}  •  Moderator: {2}  •  Admin: {3}",
                    CapStr(s.MemberDailyWithdrawCap), CapStr(s.OfficerDailyWithdrawCap), CapStr(s.ModeratorDailyWithdrawCap), CapStr(s.AdminDailyWithdrawCap)));

            GUI.color = oldCol;
        }

        private static string CapStr(int cap)
        {
            if (cap < 0) return KmhText.Get("KMH_GH_Unlimited", "unlimited");
            if (cap == 0) return KmhText.Get("KMH_GH_None", "none");
            return SilverFmt.Format(cap);
        }

        internal static string FormatDiplomacy(Dictionary<string, string> rels, List<string> incoming)
        {
            List<string> allies     = new List<string>();
            List<string> sent       = new List<string>();
            List<string> rivals     = new List<string>();
            List<string> asking     = new List<string>();
            if (rels != null)
                foreach (var kv in rels)
                {
                    switch (kv.Value)
                    {
                        case GuildSnapshot.RelationAllied:          allies.Add(KmhDisplayText.GuildName(kv.Key)); break;
                        case GuildSnapshot.RelationAlliedRequested: sent.Add(KmhDisplayText.GuildName(kv.Key));   break;
                        case GuildSnapshot.RelationHostile:         rivals.Add(KmhDisplayText.GuildName(kv.Key)); break;
                    }
                }
            if (incoming != null) foreach (string n in incoming) if (!string.IsNullOrEmpty(n)) asking.Add(KmhDisplayText.GuildName(n));
            List<string> parts = new List<string>();
            if (allies.Count > 0) parts.Add($"<color=#4cc38a>{KmhText.Get("KMH_Guild_DipAllies", "Allies:")}</color> {string.Join(", ", allies)}");
            if (asking.Count > 0) parts.Add($"<color=#f0c04f>{KmhText.Get("KMH_Guild_DipIncoming", "Proposals to you:")}</color> {string.Join(", ", asking)}");
            if (sent.Count   > 0) parts.Add($"<color=#f0c04f>{KmhText.Get("KMH_Guild_DipSent", "Proposal sent:")}</color> {string.Join(", ", sent)}");
            if (rivals.Count > 0) parts.Add($"<color=#f07167>{KmhText.Get("KMH_Guild_DipRivals", "Rivals:")}</color> {string.Join(", ", rivals)}");
            return parts.Count == 0
                ? "<color=#A9AFB9>" + KmhText.Get("KMH_Guild_DipNone", "Diplomacy: no declared alliances.") + "</color>"
                : string.Join("    ", parts);
        }

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
                case GuildMemberDto.RankOwner:     return KmhText.Get("KMH_GH_RankOwner", "OWNER");
                case GuildMemberDto.RankAdmin:     return KmhText.Get("KMH_GH_RankAdmin", "ADMIN");
                case GuildMemberDto.RankModerator: return KmhText.Get("KMH_GH_RankMod", "MOD");
                case GuildMemberDto.RankOfficer:   return KmhText.Get("KMH_GH_RankOfficer", "OFFICER");
                case GuildMemberDto.RankMember:    return KmhText.Get("KMH_GH_RankMember", "MEMBER");
                default:                           return rank?.ToUpper() ?? "?";
            }
        }

        private static string RankColor(string rank)
        {
            switch (rank)
            {
                case GuildMemberDto.RankAdmin:     return "#ff8888";
                case GuildMemberDto.RankModerator: return "#ffce4d";
                case GuildMemberDto.RankOfficer:   return "#79b8ff";
                case GuildMemberDto.RankMember:    return "#cccccc";
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
