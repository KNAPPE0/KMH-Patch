using System;
using System.Collections.Generic;
using System.Threading;
using KMHPatch.Features.ConnectedTreasury;
using KMHPatch.Features.ConnectedTreasury.Dto;
using KMHPatch.UI;
using RimWorld;
using UnityEngine;
using Verse;

namespace KMHPatch.Dialogs
{
    // Apart from the local treasury on purpose: confusing the two loses silver. Never says silver moved: it shows
    // what the server and Global report, and says pending for everything between.
    public class Dialog_KMHConnectedTreasury : Window_KMHBase
    {
        public override Vector2 InitialSize => DialogLayout.FitToScreen(640f, 620f);

        private string _depositText = "";
        private string _withdrawText = "";
        private Vector2 _historyScroll = Vector2.zero;
        private Vector2 _opsScroll = Vector2.zero;
        private float _nextRefreshRealtime;

        public Dialog_KMHConnectedTreasury()
        {
            doCloseX = true;
            forcePause = false;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
        }

        public static void Open()
        {
            if (!Features.Federation.FederationGate.TreasuryOffered) return;
            if (Find.WindowStack.IsOpen<Dialog_KMHConnectedTreasury>()) return;
            Find.WindowStack.Add(new Dialog_KMHConnectedTreasury());
        }

        public override void PreOpen()
        {
            base.PreOpen();
            Refresh();
        }

        private void Refresh()
        {
            ConnectedTreasuryHandler.RequestSnapshot();
            // Not awaited: the cache raises its own event, and a failure shows as unreachable, not a draw-call exception.
            _ = ConnectedGlobalClient.RefreshAsync(25, CancellationToken.None);
            _nextRefreshRealtime = Time.realtimeSinceStartup + 5f;
        }

        protected override Features.Discord.KmhActivity PresenceActivity => Features.Discord.KmhActivity.Treasury;

        protected override void DrawContents(Rect inRect)
        {
            // A new hello can switch the treasury off while this is open; it must stop offering it at once.
            if (!Features.Federation.FederationGate.TreasuryOffered) { Close(false); return; }
            // Mutations elsewhere and reconnects change this unseen, so it re-asks on a timer.
            if (Time.realtimeSinceStartup >= _nextRefreshRealtime) Refresh();

            Listing_Standard listing = new Listing_Standard();
            listing.Begin(inRect);

            Text.Font = GameFont.Medium;
            listing.Label(KmhText.Get("KMH_Ct_ConnectedTreasury", "Connected Treasury"));
            Text.Font = GameFont.Small;
            listing.Label(KmhText.Get("KMH_Ct_SilverHeldByYour", "<color=#9AA4B2>Silver held by your Colony Nexus account. It follows you between servers, and is separate from this colony's local treasury.</color>"));
            listing.GapLine(8f);

            if (!ConnectedTreasuryCache.HasSnapshot) { DrawWaiting(listing); listing.End(); return; }
            if (!ConnectedTreasuryCache.Linked) { DrawNotLinked(listing); listing.End(); return; }

            DrawBalance(listing);
            listing.Gap(6f);

            if (!ConnectedTreasuryCache.ServerAvailable) DrawUnavailable(listing);
            else DrawActions(listing);

            listing.GapLine(8f);
            float used = listing.CurHeight;
            listing.End();

            Rect rest = new Rect(inRect.x, inRect.y + used, inRect.width, inRect.height - used);
            DrawOperations(rest.TopPart(0.42f));
            DrawHistory(rest.BottomPart(0.56f));
        }

        private static void DrawWaiting(Listing_Standard l)
            => l.Label(KmhText.Get("KMH_Ct_AskingTheServerFor", "<color=#9AA4B2>Asking the server for your Connected Treasury state…</color>"));

        private static void DrawNotLinked(Listing_Standard l)
        {
            l.Label(KmhText.Get("KMH_Ct_YouHaveNotLinked", "You have not linked a Colony Nexus account on this server yet."));
            l.Gap(4f);
            l.Label(KmhText.Get("KMH_Ct_AKmhAccountIs", "<color=#9AA4B2>A Colony Nexus account is what carries silver between servers. Linking is free and uses Steam - no password reaches this server.</color>"));
            l.Gap(8f);
            if (l.ButtonText(KmhText.Get("KMH_Ct_LinkAKmhAccount", "Link a Colony Nexus account"))) Dialog_KMHAccount.Open();
        }

        private static void DrawUnavailable(Listing_Standard l)
        {
            l.Label(KmhText.Get("KMH_Ct_KmhGlobalIsUnreachable", "<color=#f0c04f>Colony Nexus Global is unreachable from this server, so deposits and withdrawals are paused.</color>"));
            l.Gap(4f);
            l.Label(KmhText.Get("KMH_Ct_YourLocalTreasuryIs", "<color=#9AA4B2>Your local treasury is unaffected and works normally. Anything already in flight finishes on its own once Global answers.</color>"));
        }

        private static void DrawBalance(Listing_Standard l)
        {
            ConnectedBalance b = ConnectedTreasuryCache.Balance;
            if (b == null)
            {
                string why = ConnectedTreasuryCache.BalanceError;
                l.Label(string.IsNullOrEmpty(why)
                    ? "<color=#9AA4B2>Reading your balance from Colony Nexus Global…</color>"
                    : $"<color=#f0c04f>Could not read your balance from Colony Nexus Global ({KmhDisplayText.Inert(why, 120)}).</color>");
                return;
            }

            l.Label(KmhText.Get("KMH_Ct_Available", "Available: <b>{0}</b> silver", b.Available.ToString("N0")));
            if (b.Reserved > 0)
                l.Label(KmhText.Get("KMH_Bal_Reserved", "<color=#9AA4B2>Reserved for withdrawals in progress: {0}</color>", b.Reserved.ToString("N0")));
            if (b.Debt > 0)
                l.Label(KmhText.Get("KMH_Bal_Owed", "<color=#f0c04f>Owed back: {0} - silver delivered after a hold expired.</color>", b.Debt.ToString("N0")));
            if (b.Frozen)
                l.Label(KmhText.Get("KMH_Ct_ThisAccountIsFrozen", "<color=#f0c04f>This account is frozen: it can receive, but not be drawn on.</color>"));

            long pin = ConnectedTreasuryCache.PendingIn, pout = ConnectedTreasuryCache.PendingOut;
            if (pin > 0) l.Label(KmhText.Get("KMH_Bal_PendingIn", "<color=#9AA4B2>Pending deposit: {0} (not credited yet)</color>", pin.ToString("N0")));
            if (pout > 0) l.Label(KmhText.Get("KMH_Ct_PendingOut", "<color=#9AA4B2>Pending withdrawal: {0} (not delivered yet)</color>", pout.ToString("N0")));
        }

        private void DrawActions(Listing_Standard l)
        {
            bool busy = ConnectedTreasuryCache.HasOpenOperation;
            if (busy)
            {
                l.Label(KmhText.Get("KMH_Ct_AnOperationIsAlready", "<color=#9AA4B2>An operation is already running. It finishes on its own - only one at a time.</color>"));
                return;
            }

            long max = ConnectedTreasuryCache.Snapshot?.MaxAmount ?? 0;
            long localSilver = Features.Treasury.TreasuryCache.Snapshot?.SilverBalance ?? 0;

            Rect row = l.GetRect(28f);
            Rect left = row.LeftPart(0.48f), right = row.RightPart(0.48f);

            _depositText = Widgets.TextField(left.LeftPart(0.58f), _depositText);
            if (Widgets.ButtonText(left.RightPart(0.40f), "Deposit"))
                Submit(_depositText, max, localSilver, deposit: true);

            _withdrawText = Widgets.TextField(right.LeftPart(0.58f), _withdrawText);
            if (Widgets.ButtonText(right.RightPart(0.40f), "Withdraw"))
                Submit(_withdrawText, max, localSilver, deposit: false);

            l.Gap(4f);
            l.Label(KmhText.Get("KMH_Ct_LocalHolds", "<color=#9AA4B2>Local treasury holds {0} silver. Most this server moves at once: {1}.</color>", localSilver.ToString("N0"), max.ToString("N0")));
        }

        private void Submit(string text, long max, long localSilver, bool deposit)
        {
            if (!ConnectedAmountCheck.TryRead(text, max, localSilver, deposit,
                    "Your local treasury does not hold that much silver.", out long amount))
                return;

            if (deposit) ConnectedTreasuryHandler.TryDeposit(amount);
            else ConnectedTreasuryHandler.TryWithdraw(amount);

            _depositText = "";
            _withdrawText = "";
            // The snapshot that comes back is what updates the display - nothing here assumes it worked.
            Refresh();
        }

        private void DrawOperations(Rect rect)
        {
            List<ConnectedOperation> ops = ConnectedTreasuryCache.Operations;
            Widgets.Label(rect.TopPartPixels(22f), "<b>Operations</b>");
            Rect body = new Rect(rect.x, rect.y + 24f, rect.width, rect.height - 24f);

            if (ops.Count == 0)
            {
                Widgets.Label(body, KmhText.Get("KMH_Ct_NothingInProgress", "<color=#9AA4B2>Nothing in progress.</color>"));
                return;
            }

            float lineH = DialogLayout.TextRowH, pitch = DialogLayout.TextRowsH(2, 2f);
            Rect view = new Rect(0f, 0f, body.width - 16f, ops.Count * pitch);
            Widgets.BeginScrollView(body, ref _opsScroll, view);
            float y = 0f;
            foreach (ConnectedOperation o in ops)
            {
                if (o == null) continue;
                string verb = o.IsDeposit ? "Deposit" : "Withdrawal";
                string colour = !o.Open
                    ? (o.Phase == "Cancelled" ? "#D08A5A" : "#7FB77E")
                    : (o.Retrying ? "#D0B15A" : "#9AA4B2");

                // Server and Global text, drawn as rich text: made inert like the guild window's.
                DialogLayout.LabelTrunc(new Rect(0f, y, view.width, lineH),
                    $"{verb} <b>{o.Amount:N0}</b>  <color={colour}>{KmhDisplayText.Inert(o.Status, 64)}</color>");

                string note = o.Retrying
                    ? $"retrying - {KmhDisplayText.Inert(o.Detail, 240)}"
                    : (!o.Open && o.Phase == "Cancelled" ? "did not complete; no silver was lost" : "");
                if (!string.IsNullOrEmpty(note))
                    DialogLayout.LabelTrunc(new Rect(0f, y + lineH, view.width, lineH), $"<color=#9AA4B2>{note}</color>");
                y += pitch;
            }
            Widgets.EndScrollView();
        }

        private void DrawHistory(Rect rect)
        {
            ConnectedBalance b = ConnectedTreasuryCache.Balance;
            Widgets.Label(rect.TopPartPixels(22f), "<b>History</b>");
            Rect body = new Rect(rect.x, rect.y + 24f, rect.width, rect.height - 24f);

            if (b == null || b.History.Count == 0)
            {
                Widgets.Label(body, KmhText.Get("KMH_Ct_NoMovementsRecordedYet", "<color=#9AA4B2>No movements recorded yet.</color>"));
                return;
            }

            Rect view = new Rect(0f, 0f, body.width - 16f, b.History.Count * 22f);
            Widgets.BeginScrollView(body, ref _historyScroll, view);
            float y = 0f;
            foreach (ConnectedHistoryLine h in b.History)
            {
                if (h == null) continue;
                string when = DateTimeOffset.FromUnixTimeSeconds(h.AtUnix).LocalDateTime.ToString("yyyy-MM-dd HH:mm");
                DialogLayout.LabelTrunc(new Rect(0f, y, view.width, 20f),
                    $"<color=#9AA4B2>{when}</color>  {KmhDisplayText.Inert(h.Kind, 32)} <b>{h.Amount:N0}</b>  <color=#9AA4B2>balance {h.SettledAfter:N0}</color>");
                y += 22f;
            }
            Widgets.EndScrollView();
        }
    }
}
