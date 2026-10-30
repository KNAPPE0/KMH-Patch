using System;
using System.Collections.Generic;
using KMHPatch.Features.ConnectedGuild;
using KMHPatch.Features.ConnectedGuild.Dto;
using KMHPatch.Features.Federation;
using KMHPatch.UI;
using RimWorld;
using UnityEngine;
using Verse;

namespace KMHPatch.Dialogs
{
    // Apart from the guild vault on purpose. Never says silver moved: it shows the server's durable state and the
    // balance it last read from Global, with that time, and says pending for everything between.
    public class Dialog_KMHConnectedGuildTreasury : Window_KMHBase
    {
        public override Vector2 InitialSize => DialogLayout.FitToScreen(660f, 640f);

        private string _depositText = "";
        private string _withdrawText = "";
        private Vector2 _opsScroll = Vector2.zero;
        private Vector2 _historyScroll = Vector2.zero;
        private float _nextRefreshRealtime;

        public Dialog_KMHConnectedGuildTreasury()
        {
            doCloseX = true;
            forcePause = false;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
        }

        public static void Open()
        {
            if (!FederationGate.TreasuryOffered) return;
            if (Find.WindowStack.IsOpen<Dialog_KMHConnectedGuildTreasury>()) return;
            Find.WindowStack.Add(new Dialog_KMHConnectedGuildTreasury());
        }

        public override void PreOpen()
        {
            base.PreOpen();
            Refresh();
        }

        // Also makes the server re-read Global, so a newly demanded re-proof shows without a restart.
        private void Refresh()
        {
            ConnectedGuildHandler.RequestSnapshot();
            _nextRefreshRealtime = Time.realtimeSinceStartup + 5f;
        }

        protected override Features.Discord.KmhActivity PresenceActivity => Features.Discord.KmhActivity.Guild;

        protected override void DrawContents(Rect inRect)
        {
            // A new hello can switch the treasury off while this is open; it must stop offering it at once.
            if (!FederationGate.TreasuryOffered) { Close(false); return; }
            // Other officers' deposits, reconnects and rank changes alter this unseen, so it re-asks on a timer.
            if (Time.realtimeSinceStartup >= _nextRefreshRealtime) Refresh();

            Listing_Standard listing = new Listing_Standard();
            listing.Begin(inRect);

            Text.Font = GameFont.Medium;
            listing.Label(KmhText.Get("KMH_Cg_ConnectedGuildTreasury", "Connected Guild Treasury"));
            Text.Font = GameFont.Small;
            listing.Label(KmhText.Get("KMH_Cg_SilverHeldByYour2", "<color=#9AA4B2>Silver held by your guild at Colony Nexus Global. Every server your guild has linked reaches the same silver; the guild vault on this server is separate.</color>"));
            listing.GapLine(8f);

            ConnectedGuildSnapshot s = ConnectedGuildCache.Snapshot;
            if (s == null)
            {
                listing.Label(KmhText.Get("KMH_Cg_AskingTheServerFor2", "<color=#9AA4B2>Asking the server for your guild's federated state…</color>"));
                listing.End();
                return;
            }
            if (!s.InGuild)
            {
                listing.Label(KmhText.Get("KMH_Cg_YouAreNotIn", "You are not in a guild, so there is no guild treasury to federate."));
                listing.End();
                return;
            }

            DrawLink(listing, s);

            if (s.Linked && !s.Revoked)
            {
                listing.Gap(6f);
                DrawBalance(listing, s);
                listing.Gap(6f);
                if (!s.Available) DrawUnavailable(listing);
                else DrawActions(listing, s);
            }

            listing.GapLine(8f);
            float used = listing.CurHeight;
            listing.End();

            Rect rest = new Rect(inRect.x, inRect.y + used, inRect.width, inRect.height - used);
            DrawOperations(rest.TopPart(0.42f));
            DrawHistory(rest.BottomPart(0.56f));
        }


        // Membership belongs to the Guild Federation window; this one only says where the guild stands.
        private static void DrawLink(Listing_Standard l, ConnectedGuildSnapshot s)
        {
            string guild = KmhDisplayText.Inert(s.Guild ?? "", 64);
            if (!s.Linked || s.Revoked)
            {
                l.Label(s.Revoked
                    ? "<color=#f0c04f>" + KmhText.Get("KMH_Cg_NoLongerLinked", "'{0}' is no longer linked to Colony Nexus Global.", guild) + "</color>"
                    : KmhText.Get("KMH_Cg_NotInFederation", "'{0}' is not in a guild federation, so it has no connected guild treasury yet.", guild));
                if (s.Revoked)
                    l.Label("<color=#9AA4B2>" + KmhText.Get("KMH_Cg_RevokedKeepsSilver", "Any silver the guild held stays with its federation at Colony Nexus Global, and every member server still reaches it.") + "</color>");
                l.Gap(4f);
                DrawFederationPointer(l);
                return;
            }

            l.Label(KmhText.Get("KMH_Cg_IsLinked", "'{0}' is linked to Colony Nexus Global.", guild));
            if (!s.NeedsReproof) return;

            l.Gap(6f);
            l.Label(KmhText.Get("KMH_Cg_ThisLinkMustBe", "<color=#f0c04f><b>This link must be proved again before federated silver can move.</b></color>"));
            if (!string.IsNullOrEmpty(s.StateWhy)) l.Label("<color=#9AA4B2>" + KmhDisplayText.Inert(s.StateWhy, 240) + "</color>");
            l.Label("<color=#9AA4B2>" + KmhText.Get("KMH_Cg_ReproveInFederation", "A guild Admin or the Owner proves it again from the Guild Federation window. The guild vault on this server is unaffected, and the balance below is still readable.") + "</color>");
            DrawFederationPointer(l);
        }

        private static void DrawFederationPointer(Listing_Standard l)
        {
            l.Label("<color=#9AA4B2>" + KmhText.Get("KMH_Cg_MembershipInFederation", "Creating, joining and leaving a federation all happen in the Guild Federation window.") + "</color>");
            if (FederationGate.FederationOffered && l.ButtonText(KmhText.Get("KMH_Cg_OpenFederation", "Open Guild Federation")))
                Dialog_KMHFederation.Open();
        }


        private static void DrawUnavailable(Listing_Standard l)
        {
            l.Label(KmhText.Get("KMH_Cg_KmhGlobalIsUnreachable4", "<color=#f0c04f>Colony Nexus Global is unreachable from this server, so federated deposits and withdrawals are paused.</color>"));
            l.Gap(4f);
            l.Label(KmhText.Get("KMH_Cg_TheGuildVaultOn", "<color=#9AA4B2>The guild vault on this server is unaffected and works normally. Anything already in flight finishes on its own once Global answers.</color>"));
        }

        private static void DrawBalance(Listing_Standard l, ConnectedGuildSnapshot s)
        {
            if (s.BalanceReadUtcTicks == 0)
            {
                l.Label(KmhText.Get("KMH_Cg_TheServerHasNot", "<color=#9AA4B2>The server has not read this guild's balance from Colony Nexus Global yet.</color>"));
            }
            else
            {
                l.Label(KmhText.Get("KMH_Cg_AvailableAt", "Available at Colony Nexus Global: <b>{0}</b> silver", s.AvailableSilver.ToString("N0")));
                if (s.ReservedSilver > 0)
                    l.Label(KmhText.Get("KMH_Bal_Reserved", "<color=#9AA4B2>Reserved for withdrawals in progress: {0}</color>", s.ReservedSilver.ToString("N0")));
                if (s.Debt > 0)
                    l.Label(KmhText.Get("KMH_Bal_Owed", "<color=#f0c04f>Owed back: {0} - silver delivered after a hold expired.</color>", s.Debt.ToString("N0")));
                if (s.Frozen)
                    l.Label(KmhText.Get("KMH_Cg_ThisGuildTreasuryIs", "<color=#f0c04f>This guild treasury is frozen: it can receive, but not be drawn on.</color>"));

                // Stamped rather than presented as current: these numbers came from Global, not from this server.
                l.Label(KmhText.Get("KMH_Cg_AsReported", "<color=#9AA4B2>As Colony Nexus Global reported it {0}.</color>", Ago(s.BalanceReadUtcTicks)));
            }

            if (s.PendingIn > 0)  l.Label(KmhText.Get("KMH_Bal_PendingIn", "<color=#9AA4B2>Pending deposit: {0} (not credited yet)</color>", s.PendingIn.ToString("N0")));
            if (s.PendingOut > 0) l.Label(KmhText.Get("KMH_Cg_PendingOut", "<color=#9AA4B2>Pending withdrawal: {0} (not in the vault yet)</color>", s.PendingOut.ToString("N0")));
        }

        private void DrawActions(Listing_Standard l, ConnectedGuildSnapshot s)
        {
            if (s.NeedsReproof)
            {
                l.Label(KmhText.Get("KMH_Cg_FederatedSilverCannotMove", "<color=#9AA4B2>Federated silver cannot move until the link is proved again. The guild vault still works normally.</color>"));
                return;
            }
            if (!s.MayMove)
            {
                l.Label("<color=#9AA4B2>" + Reason(s.MoveWhy, KmhText.Get("KMH_Cg_RankCannotMove", "Your rank cannot move this guild's silver.")) + "</color>");
                return;
            }
            if (ConnectedGuildCache.HasOpenOperation)
            {
                l.Label(KmhText.Get("KMH_Cg_ThisGuildAlreadyHas", "<color=#9AA4B2>This guild already has an operation running. It finishes on its own - one at a time, whoever started it.</color>"));
                return;
            }

            Rect row = l.GetRect(28f);
            Rect left = row.LeftPart(0.48f), right = row.RightPart(0.48f);

            _depositText = Widgets.TextField(left.LeftPart(0.58f), _depositText);
            if (Widgets.ButtonText(left.RightPart(0.40f), "Deposit"))
                Submit(_depositText, s, deposit: true);

            _withdrawText = Widgets.TextField(right.LeftPart(0.58f), _withdrawText);
            if (Widgets.ButtonText(right.RightPart(0.40f), "Withdraw"))
                Submit(_withdrawText, s, deposit: false);

            l.Gap(4f);
            l.Label(KmhText.Get("KMH_Cg_VaultHolds", "<color=#9AA4B2>The guild vault on this server holds {0} silver. Most this server moves at once: {1}.</color>", s.LocalSilver.ToString("N0"), s.MaxAmount.ToString("N0")));
            l.Label(KmhText.Get("KMH_Cg_DepositsCountAgainstYour", "<color=#9AA4B2>Deposits count against your rank's daily withdraw cap, the same as taking silver out of the vault.</color>"));
        }

        private void Submit(string text, ConnectedGuildSnapshot s, bool deposit)
        {
            if (!ConnectedAmountCheck.TryRead(text, s.MaxAmount, s.LocalSilver, deposit,
                    "The guild vault does not hold that much silver.", out long amount))
                return;

            if (deposit) ConnectedGuildHandler.TryDeposit(amount);
            else ConnectedGuildHandler.TryWithdraw(amount);

            _depositText = "";
            _withdrawText = "";
            // The snapshot that comes back is what updates the display - nothing here assumes it worked.
            Refresh();
        }


        private readonly List<ConnectedGuildOperation> _openOps = new List<ConnectedGuildOperation>();
        private readonly List<ConnectedGuildOperation> _doneOps = new List<ConnectedGuildOperation>();

        private void DrawOperations(Rect rect)
        {
            List<ConnectedGuildOperation> open = _openOps;
            open.Clear();
            foreach (ConnectedGuildOperation o in ConnectedGuildCache.Operations)
                if (o != null && o.Open) open.Add(o);

            Widgets.Label(rect.TopPartPixels(22f), "<b>In progress</b>");
            Rect body = new Rect(rect.x, rect.y + 24f, rect.width, rect.height - 24f);

            if (open.Count == 0)
            {
                Widgets.Label(body, KmhText.Get("KMH_Ct_NothingInProgress", "<color=#9AA4B2>Nothing in progress.</color>"));
                return;
            }

            float lineH = DialogLayout.TextRowH, pitch = DialogLayout.TextRowsH(2, 2f);
            Rect view = new Rect(0f, 0f, body.width - 16f, open.Count * pitch);
            Widgets.BeginScrollView(body, ref _opsScroll, view);
            float y = 0f;
            foreach (ConnectedGuildOperation o in open)
            {
                string verb = o.IsDeposit ? "Deposit" : "Withdrawal";
                string colour = o.Retrying ? "#D0B15A" : "#9AA4B2";
                string who = string.IsNullOrEmpty(o.Who) ? "" : $" <color=#9AA4B2>by {KmhDisplayText.Inert(o.Who, 64)}</color>";

                DialogLayout.LabelTrunc(new Rect(0f, y, view.width, lineH),
                    $"{verb} <b>{o.Amount:N0}</b>{who}  <color={colour}>{KmhDisplayText.Inert(o.Status, 64)}</color>");

                // A stuck operation says why, so "retrying" is never a silent spinner.
                if (o.Retrying)
                    DialogLayout.LabelTrunc(new Rect(0f, y + lineH, view.width, lineH),
                        $"<color=#9AA4B2>retrying - {Explain(o.Detail)}</color>");
                y += pitch;
            }
            Widgets.EndScrollView();
        }

        private void DrawHistory(Rect rect)
        {
            List<ConnectedGuildOperation> done = _doneOps;
            done.Clear();
            foreach (ConnectedGuildOperation o in ConnectedGuildCache.Operations)
                if (o != null && !o.Open) done.Add(o);
            done.Reverse();

            Widgets.Label(rect.TopPartPixels(22f), "<b>History</b>");
            Rect body = new Rect(rect.x, rect.y + 24f, rect.width, rect.height - 24f);

            if (done.Count == 0)
            {
                Widgets.Label(body, KmhText.Get("KMH_Cg_NoFederatedMovementsRecorded", "<color=#9AA4B2>No federated movements recorded yet.</color>"));
                return;
            }

            Rect view = new Rect(0f, 0f, body.width - 16f, done.Count * 22f);
            Widgets.BeginScrollView(body, ref _historyScroll, view);
            float y = 0f;
            foreach (ConnectedGuildOperation o in done)
            {
                string when = o.UpdatedUtcTicks > 0
                    ? new DateTime(o.UpdatedUtcTicks, DateTimeKind.Utc).ToLocalTime().ToString("yyyy-MM-dd HH:mm")
                    : "";
                string verb = o.IsDeposit ? "Deposit" : "Withdrawal";
                string colour = o.Phase == "Cancelled" ? "#D08A5A" : "#7FB77E";
                string who = string.IsNullOrEmpty(o.Who) ? "" : $"  <color=#9AA4B2>{KmhDisplayText.Inert(o.Who, 64)}</color>";
                DialogLayout.LabelTrunc(new Rect(0f, y, view.width, 20f),
                    $"<color=#9AA4B2>{when}</color>  {verb} <b>{o.Amount:N0}</b>{who}  <color={colour}>{KmhDisplayText.Inert(o.Status, 64)}</color>");
                y += 22f;
            }
            Widgets.EndScrollView();
        }


        // The server's own words when given; a blank reason would read as no reason at all.
        private static string Reason(string given, string fallback)
            => string.IsNullOrEmpty(given) ? fallback : KmhDisplayText.Inert(given, 240);

        // Global's refusal codes mean nothing to a player, so the ones a guild can actually hit are named.
        private static string Explain(string detail)
        {
            switch (detail)
            {
                case "global unreachable": return "Colony Nexus Global is not answering";
                case "no response":        return "Colony Nexus Global did not answer";
                case "KMH-GLD-959":        return "the guild's link must be proved again";
                case "KMH-TRS-955":        return "the guild treasury does not hold that much";
                // Trust refusals. Each names what is still true, because "blocked" on its own reads as lost.
                case "KMH-TRS-964":        return "this server is not approved to move federated silver yet";
                case "KMH-TRS-965":        return "Colony Nexus Global has suspended this server's federated silver";
                case "KMH-TRS-966":        return "that is more than this server may move at once";
                case "KMH-TRS-967":        return "this server has moved all it may today";
                case "KMH-TRS-968":        return "some of the guild's silver has not settled yet";
                case "KMH-TRS-952":        return "that amount is not allowed";
                case "persistence.degraded": return "this server could not write the change down; it resumes on its own";
                default:                   return KmhDisplayText.Inert(detail, 160);
            }
        }

        private static string Ago(long utcTicks)
        {
            TimeSpan since = DateTime.UtcNow - new DateTime(utcTicks, DateTimeKind.Utc);
            if (since.TotalSeconds < 0)  return "just now";
            if (since.TotalSeconds < 90) return "moments ago";
            if (since.TotalMinutes < 90) return $"{(int)since.TotalMinutes} minutes ago";
            if (since.TotalHours < 36)   return $"{(int)since.TotalHours} hours ago";
            return $"{(int)since.TotalDays} days ago";
        }
    }
}
