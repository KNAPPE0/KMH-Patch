using System;
using System.Collections.Generic;
using KMHPatch.Features.LinkedAccounts;
using KMHPatch.Features.WantBoard.Dto;
using KMHPatch.UI;
using UnityEngine;
using Verse;

namespace KMHPatch.Features.WantBoard
{
    // The buyer's silver is escrowed server-side and fulfilling moves items treasury->treasury, so every move is server-authoritative.
    public class Dialog_KMHWantBoard : Window_KMHBase
    {
        public override Vector2 InitialSize => KMHPatch.UI.DialogLayout.FitToScreen(900f, 620f);

        private Vector2  _scroll;

        private string _filter   = "";
        private bool   _mineOnly = false;

        // Cached filtered/sorted view - rebuilt only when the snapshot, filter, or toggle change (not every frame).
        private List<WantDto> _visible;
        private object _visibleSource;
        private string _visibleFilter;
        private bool   _visibleMine;
        private string _visibleMe;

        public Dialog_KMHWantBoard()
        {
            doCloseX                = true;
            absorbInputAroundWindow = true;
            forcePause              = false;
            draggable               = true;
            resizeable              = true;

            EnableAutoRefresh(() => WantHandler.RequestSnapshot());
            WantCache.Updated += MarkRefreshed;
        }

        public override void PostClose() { base.PostClose(); WantCache.Updated -= MarkRefreshed; }

        protected override void DrawContents(Rect rect)
        {
            float y = DialogLayout.DrawTitle(rect, "Want Board");
            DialogLayout.DrawLiveBadge(rect, SecondsSinceRefresh, HasReceivedData, 0f);
            DialogLayout.DrawSectionDivider(rect, ref y);

            // Below ~650px the left cursor (~380px) and the right-anchored buttons overlapped, so the buttons wrap to their own row.
            float btnW = Mathf.Max(IconButton.WidthFor("Refresh", false), IconButton.WidthFor("Post want", true));
            bool  oneRow = rect.width >= 380f + btnW * 2f + 8f;
            float leftW  = oneRow ? rect.width - btnW * 2f - 8f : rect.width;
            float filterW = Mathf.Clamp(leftW - 80f, 110f, 300f);

            _filter = DialogLayout.SearchField(new Rect(0f, y, filterW, 28f), _filter, "Filter by item or player");
            DialogLayout.DrawTightCheckbox(filterW + 16f, y + 4f, "Mine", ref _mineOnly);

            float btnY = oneRow ? y : y + 32f;
            float cell = oneRow ? btnW : rect.width / 2f;
            float btnX = oneRow ? rect.width - btnW * 2f : 0f;

            if (Widgets.ButtonText(new Rect(btnX, btnY, cell - 4f, 28f), "Refresh"))
            {
                WantHandler.RequestSnapshot();
            }
            if (IconButton.Draw(new Rect(btnX + cell, btnY, cell - 4f, 28f), KMHTextures.Post, "Post want"))
                Dialog_KMHPostWant.Open();

            y = btnY + 34f;

            // Measured: the hint wraps to two lines on a narrow window, where a fixed 18px box cut the second line off.
            Color old = GUI.color;
            GUI.color = DialogLayout.MutedColor;
            const string hint = "Deliver from your treasury (deposit the goods to your vault first) to fulfill a want for its posted price.";
            float hintH = Text.CalcHeight(hint, rect.width);
            Widgets.Label(new Rect(0f, y, rect.width, hintH), hint);
            GUI.color = old;
            y += hintH + 4f;

            Rect listBox = new Rect(0f, y, rect.width, DialogLayout.BodyHeight(rect, y));
            Widgets.DrawMenuSection(listBox);
            DrawList(listBox);

            if (DialogLayout.DrawCloseButton(rect)) Close();
        }

        private void DrawList(Rect box)
        {
            // Three stacked text lines plus padding, measured rather than assumed.
            float rowH = DialogLayout.TextRowsH(3, 8f);

            string me  = KmhSession.Me;
            string flt = (_filter ?? "").Trim().ToLowerInvariant();
            object src = WantCache.HasSnapshot ? (object)WantCache.Snapshot.Wants : null;

            if (_visible == null || !ReferenceEquals(_visibleSource, src)
                || _visibleFilter != flt || _visibleMine != _mineOnly || _visibleMe != me)
            {
                _visible = Build(src as List<WantDto>, flt, _mineOnly, me);
                _visibleSource = src; _visibleFilter = flt; _visibleMine = _mineOnly; _visibleMe = me;
            }
            List<WantDto> rows = _visible;

            long now = DateTime.UtcNow.Ticks;

            string empty = !WantCache.HasSnapshot
                ? $"<color=grey>{DialogLayout.AwaitingSnapshot("Loading wants…", "Wants")}</color>"
                : (flt.Length > 0 || _mineOnly) ? "<color=grey>No wants match.</color>"
                : "<color=grey>No open wants. Post one!</color>";

            DialogLayout.ScrollList(box, ref _scroll, rows.Count, rowH, (i, row) => DrawRow(row, rows[i], me, now), empty);
        }

        // Filter + sort (ending soonest first). Built only on change, not per frame.
        private static List<WantDto> Build(List<WantDto> src, string flt, bool mineOnly, string me)
        {
            List<WantDto> outList = new List<WantDto>();
            if (src == null) return outList;
            foreach (WantDto w in src)
            {
                if (w == null) continue;
                if (mineOnly && !KmhSession.Same(w.BuyerUsername, me)) continue;
                if (flt.Length > 0)
                {
                    string item = (ItemLabels.ResolveLabel(w.ItemDefName) ?? "").ToLowerInvariant();
                    string buy  = (w.BuyerUsername ?? "").ToLowerInvariant();
                    if (!item.Contains(flt) && !buy.Contains(flt) && w.Id.ToString() != flt) continue;
                }
                outList.Add(w);
            }
            outList.Sort((x, z) => x.EndsUtcTicks.CompareTo(z.EndsUtcTicks));
            return outList;
        }

        private static bool Eq(string x, string y) => !string.IsNullOrEmpty(x) && string.Equals(x, y, StringComparison.OrdinalIgnoreCase);

        private static void DrawRow(Rect row, WantDto w, string me, long now)
        {
            Rect inner = row.ContractedBy(6f);
            const float reservedRight = 116f;
            // Floored: on a narrow row this subtraction went negative and the text column ran back under the action buttons.
            float textW = Mathf.Max(0f, inner.width - reservedRight);

            bool mine      = KmhSession.Same(w.BuyerUsername, me);
            int  remaining = Math.Max(0, w.QtyWanted - w.QtyFilled);
            string label   = ItemLabels.ResolveLabel(w.ItemDefName);

            const float iconSize = 22f;
            ItemLabels.DrawIcon(new Rect(inner.x, inner.y, iconSize, iconSize), w.ItemDefName);
            UI.KmhItemInfo.ButtonForDef(inner.x + iconSize + 4f, inner.y, w.ItemDefName);

            // Line 1: item + qty progress + buyer
            DialogLayout.LabelTrunc(new Rect(inner.x + iconSize + UI.KmhItemInfo.Size + 8f, inner.y, textW - iconSize - UI.KmhItemInfo.Size - 8f, DialogLayout.TextRowH),
                $"<b>#{w.Id}  {label}</b>  <color=grey>·</color> {w.QtyFilled}/{w.QtyWanted} filled  <color=grey>· wanted by</color> {Buyer(w.BuyerUsername)}");

            // Line 2: unit price + total escrow
            Color old = GUI.color; GUI.color = new Color(0.85f, 0.85f, 0.85f);
            DialogLayout.LabelTrunc(new Rect(inner.x, inner.y + DialogLayout.TextRowH, textW, DialogLayout.TextRowH),
                $"Pays <color=yellow>{SilverFmt.Format(w.UnitPriceSilver)}</color> each  <color=grey>· {SilverFmt.Format(w.EscrowRemaining)} left in escrow</color>");
            GUI.color = old;

            // Line 3: time left + accepted-state constraints
            GUI.color = DialogLayout.MutedColor;
            DialogLayout.LabelTrunc(new Rect(inner.x, inner.y + DialogLayout.TextRowH * 2f, textW, DialogLayout.TextRowH),
                $"ends in {DialogLayout.TimeLeft(w.EndsUtcTicks, now)}{Accepts(w)}");
            GUI.color = old;

            float bw = 106f, bx = inner.xMax - bw;
            if (mine)
            {
                if (IconButton.Draw(new Rect(bx, inner.y + 4f, bw, 24f), KMHTextures.Cancel, "Cancel"))
                    WantHandler.TryCancel(w.Id);
            }
            else if (remaining > 0)
            {
                if (IconButton.Draw(new Rect(bx, inner.y + 4f, bw, 24f), KMHTextures.Post, "Fulfill"))
                    OpenFulfill(w, remaining);
            }
        }

        // Fulfillment delivers from the player's KMH treasury, so cap the amount at what they actually hold there.
        private static void OpenFulfill(WantDto w, int remaining)
        {
            int have = 0;
            Treasury.Dto.TreasurySnapshot snap = Treasury.TreasuryCache.Snapshot;
            // Compact stacks are keyed def|stuff|quality, so a bare-defName lookup misses every stuffed or quality item.
            if (snap?.Items != null)
                foreach (KeyValuePair<string, int> kv in snap.Items)
                    if (ItemKeys.Matches(kv.Key, w.ItemDefName, w.RequiredStuff, w.MinQuality))
                        have += Math.Max(0, kv.Value);
            // Complex wants match full-state gear held in the payload store; count those units too (server re-validates).
            if (w.AllowComplex && snap?.ItemPayloads != null)
                foreach (Items.KmhThingPayload p in snap.ItemPayloads)
                    if (WantMatches(w, p)) have += Math.Max(0, p.StackCount);
            if (have <= 0)
            {
                Notifications.KmhNotifications.Rejected($"Deposit {ItemLabels.ResolveLabel(w.ItemDefName)} to your treasury first to fulfill this want.");
                return;
            }
            int max = Math.Min(remaining, have);
            long id = w.Id;
            Find.WindowStack.Add(new Dialog_KMHAmountInput(
                title:        $"Fulfill: deliver {ItemLabels.ResolveLabel(w.ItemDefName)}",
                confirmLabel: "Deliver",
                unitLabel:    $"units (you have {have}, want needs {remaining})",
                maxHint:      max,
                onConfirm:    qty => WantHandler.TryFulfill(id, qty)));
        }

        private static string Buyer(string u)
            => LinkedAccountsCache.Format(u);   // Format() already renders empty as "(unknown)"

        // Requirements exclude stock while allow-flags admit stock the safe default rejects, so they read as two sentences, not one "accepts" list.
        private static string Accepts(WantDto w)
        {
            List<string> requires = new List<string>();
            if (w.MinQuality > 0) requires.Add(ItemKeys.QualityName(w.MinQuality) + " or better");
            if (!string.IsNullOrEmpty(w.RequiredStuff)) requires.Add(ItemLabels.ResolveLabel(w.RequiredStuff));

            List<string> accepts = new List<string>();
            if (w.AllowComplex) accepts.Add("used gear");
            if (w.AllowDamaged) accepts.Add("damaged");
            if (w.AllowTainted) accepts.Add("tainted");

            string s = "";
            if (requires.Count > 0) s += "  <color=grey>· requires " + string.Join(", ", requires) + "</color>";
            if (accepts.Count > 0)  s += "  <color=grey>· accepts " + string.Join(", ", accepts) + "</color>";
            return s;
        }

        // Client-side mirror of TreasuryStore.TryWithdrawMatchingPayloads, only to size the "you have N" cap; the server re-validates on fulfill.
        private static bool WantMatches(WantDto w, Items.KmhThingPayload p)
        {
            if (p == null) return false;
            if (!Eq(p.DefName, w.ItemDefName)) return false;
            if (!string.IsNullOrEmpty(w.RequiredStuff) && !Eq(p.StuffDefName ?? "", w.RequiredStuff)) return false;
            if (w.MinQuality > 0 && p.Quality < w.MinQuality) return false;
            if (!w.AllowTainted && p.Tainted) return false;
            if (!w.AllowDamaged && p.HitPoints >= 0 && p.MaxHitPoints > 0 && p.HitPoints < p.MaxHitPoints) return false;
            return true;
        }
    }
}
