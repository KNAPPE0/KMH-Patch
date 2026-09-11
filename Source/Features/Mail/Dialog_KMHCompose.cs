using System;
using System.Collections.Generic;
using KMHPatch.Features.LinkedAccounts;
using KMHPatch.Features.Treasury;
using KMHPatch.UI;
using UnityEngine;
using Verse;

namespace KMHPatch.Features.Mail
{
    // The server sets the sender from the session, clamps the text and re-checks the goods, so this only gathers and sends.
    public class Dialog_KMHCompose : Window_KMHBase
    {
        public override Vector2 InitialSize => KMHPatch.UI.DialogLayout.FitToScreen(560f, 610f);

        private string _to      = "";
        private string _subject = "";
        private string _body    = "";
        private string _attach  = "";
        private readonly Dictionary<string, int> _attachItems = new Dictionary<string, int>();   // compact defName -> qty
        private readonly Dictionary<string, int> _attachGear  = new Dictionary<string, int>();   // gear fingerprint -> qty
        private readonly Dictionary<string, string> _gearLabel = new Dictionary<string, string>(); // fingerprint -> display
        private Vector2 _itemScroll;
        private Vector2 _bodyScroll;

        private Dialog_KMHCompose(string to)
        {
            _to = to ?? "";
            doCloseX                = true;
            absorbInputAroundWindow = true;
            // Mail is multiline: Enter must insert a newline, not accept-and-close the window. Ctrl+Enter sends.
            closeOnAccept           = false;
            forcePause              = false;
            draggable               = true;
        }

        public static void Open(string to = "") => Find.WindowStack.Add(new Dialog_KMHCompose(to));

        protected override void DrawContents(Rect rect)
        {
            float y = DialogLayout.DrawTitle(rect, "New mail");
            DialogLayout.DrawSectionDivider(rect, ref y);

            const float labelW = 90f;

            DialogLayout.LabelTrunc(new Rect(0f, y + 4f, labelW, 22f), "To");
            string toLabel = string.IsNullOrEmpty(_to) ? "<color=grey>(pick a player)</color>" : LinkedAccountsCache.Format(_to);
            if (Widgets.ButtonText(new Rect(labelW, y, rect.width - labelW, 26f), toLabel))
                Dialog_KMHPlayerPicker.Open("Send mail to", u => _to = u);
            y += 32f;

            DialogLayout.LabelTrunc(new Rect(0f, y + 4f, labelW, 22f), "Subject");
            _subject = Widgets.TextField(new Rect(labelW, y, rect.width - labelW, 26f), _subject ?? "");
            y += 32f;

            DialogLayout.LabelTrunc(new Rect(0f, y + 4f, labelW, 22f), "Attach silver");
            const float hintW = 150f;
            // Floored: below ~244px this subtraction went negative and the field was drawn inside-out.
            float silverW = Mathf.Max(40f, rect.width - labelW - hintW - 4f);
            _attach = Widgets.TextField(new Rect(labelW, y, silverW, 26f), _attach ?? "");
            long have = PersonalSilver();
            Color oc = GUI.color; GUI.color = DialogLayout.MutedColor;
            DialogLayout.LabelTrunc(new Rect(rect.width - hintW, y + 4f, hintW, 22f),
                have > 0 ? $"you have {SilverFmt.Format(have)}" : "", TextAnchor.MiddleRight);
            GUI.color = oc;
            y += 32f;

            DialogLayout.LabelTrunc(new Rect(0f, y + 4f, labelW, 22f), "Attach goods");
            if (Widgets.ButtonText(new Rect(labelW, y, Mathf.Min(170f, Mathf.Max(0f, rect.width - labelW)), 26f), "Add item / gear")) OpenPicker();
            y += 30f;
            Rect itemBox = new Rect(0f, y, rect.width, 58f);
            Widgets.DrawMenuSection(itemBox);
            DrawAttached(itemBox);
            y += 62f;

            DialogLayout.LabelTrunc(new Rect(0f, y, rect.width, 20f), "Message");
            y += 22f;
            float bodyH = DialogLayout.BodyHeight(rect, y) - 4f;

            // 1.6 has no scrollable TextArea, so the scroll view is our own.
            Rect editorRect = new Rect(0f, y, rect.width, bodyH);
            float lineH = Text.LineHeight;
            // Measured, not estimated per character: a narrow dialog wraps onto more lines than a guess reserves.
            float editW = Mathf.Max(1f, editorRect.width - DialogLayout.ScrollbarReserveWidth);
            float textH = Text.CalcHeight(string.IsNullOrEmpty(_body) ? " " : _body, editW);
            Rect viewRect = new Rect(0f, 0f, editW, Mathf.Max(textH + lineH * 2f, bodyH));
            Widgets.BeginScrollView(editorRect, ref _bodyScroll, viewRect);
            _body = Widgets.TextArea(viewRect, _body ?? "");
            Widgets.EndScrollView();

            // Anchored to opposite edges with the hint between, so they shrink rather than overlap on a narrow dialog.
            const float btnH = 32f;
            float btnW = Mathf.Clamp((rect.width - 16f) / 3f, 64f, 120f);
            float btnY = rect.height - btnH - 4f;
            if (IconButton.Draw(new Rect(0f, btnY, btnW, btnH), KMHTextures.Cancel, "Cancel")) Close();
            if (IconButton.Draw(new Rect(rect.width - btnW, btnY, btnW, btnH), KMHTextures.Post, "Send")) Submit();

            Color prev = GUI.color;
            GUI.color = DialogLayout.MutedColor;
            DialogLayout.LabelTrunc(new Rect(btnW + 8f, btnY + 7f, Mathf.Max(0f, rect.width - btnW * 2f - 16f), DialogLayout.TextRowH),
                "Ctrl+Enter to send", TextAnchor.MiddleCenter);
            GUI.color = prev;

            HandleCtrlEnter();
        }

        // Enter stays a newline, so a stray one can never close the composer and lose a half-written letter.
        private void HandleCtrlEnter()
        {
            if (Event.current.type != EventType.KeyDown) return;
            if (Event.current.keyCode != KeyCode.Return && Event.current.keyCode != KeyCode.KeypadEnter) return;
            if (!Event.current.control && !Event.current.command) return;

            Event.current.Use();
            Submit();
        }

        private void Submit()
        {
            if (string.IsNullOrEmpty(_to)) { Reject("Pick a recipient first"); return; }

            long attach = 0;
            string a = (_attach ?? "").Trim();
            if (a.Length > 0 && (!long.TryParse(a, out attach) || attach < 0)) { Reject("Attach silver: a whole number, or leave it blank"); return; }
            if (attach > PersonalSilver()) { Reject("You don't have that much silver in your treasury to attach"); return; }

            if (attach <= 0 && _attachItems.Count == 0 && _attachGear.Count == 0
                && string.IsNullOrWhiteSpace(_body) && string.IsNullOrWhiteSpace(_subject))
            { Reject("Write a subject or message, or attach silver/items/gear"); return; }

            if (MailHandler.TrySend(_to, _subject, _body, attach,
                    _attachItems.Count > 0 ? _attachItems : null, _attachGear.Count > 0 ? _attachGear : null)) Close();
        }

        // Combined attached list: compact items (by defName) then full-state gear (by fingerprint), each removable.
        private void DrawAttached(Rect box)
        {
            List<string> items = new List<string>(_attachItems.Keys);
            List<string> gear  = new List<string>(_attachGear.Keys);
            int count = items.Count + gear.Count;
            DialogLayout.ScrollList(box, ref _itemScroll, count, 22f, (i, row) =>
            {
                Rect inner = row.ContractedBy(3f);
                if (i < items.Count)
                {
                    string def = items[i];
                    if (Widgets.ButtonText(new Rect(inner.x, inner.y, 18f, 18f), "x")) _attachItems.Remove(def);
                    DialogLayout.LabelTrunc(new Rect(row.x + 24f, row.y, Mathf.Max(0f, row.width - 28f), DialogLayout.TextRowH),
                        $"{ItemLabels.ResolveLabel(def)}  <color=grey>x{_attachItems[def]}</color>");
                }
                else
                {
                    string fp = gear[i - items.Count];
                    if (Widgets.ButtonText(new Rect(inner.x, inner.y, 18f, 18f), "x")) { _attachGear.Remove(fp); _gearLabel.Remove(fp); }
                    string label = _gearLabel.TryGetValue(fp, out string l) ? l : "gear";
                    DialogLayout.LabelTrunc(new Rect(row.x + 24f, row.y, Mathf.Max(0f, row.width - 28f), DialogLayout.TextRowH),
                        $"<color=#B9D0FF>{label}</color>  <color=grey>x{_attachGear[fp]}</color>");
                }
            }, "<color=grey>(nothing attached)</color>");
        }

        private void OpenPicker()
        {
            Dictionary<string, int> haveItems = PersonalItems();
            List<KMHPatch.Items.KmhThingPayload> haveGear = PersonalPayloads();
            if (haveItems.Count == 0 && haveGear.Count == 0) { Reject("No items or gear in your personal treasury to attach"); return; }
            KmhItemPickerService.Open(
                title:           "Attach from your treasury",
                pickActionLabel: "Choose",
                source:          haveItems,
                onPick:          (key, _) => PromptQty(key),
                refreshSource:   () => PersonalItems(),
                payloads:        haveGear,
                onPickPayload:   (pl, qty) => AddGear(pl, qty),
                refreshPayloads: () => PersonalPayloads(),
                closeOnPick:     true);
        }

        private void AddGear(KMHPatch.Items.KmhThingPayload pl, int qty)
        {
            if (pl == null || string.IsNullOrEmpty(pl.Fingerprint)) return;
            int avail = Math.Max(1, pl.StackCount);
            _attachGear.TryGetValue(pl.Fingerprint, out int already);
            int room = avail - already;
            if (room <= 0) { Reject("You've already attached all of those"); return; }
            int add = Math.Min(qty <= 0 ? 1 : qty, room);
            _attachGear[pl.Fingerprint] = already + add;
            _gearLabel[pl.Fingerprint]  = string.IsNullOrEmpty(pl.DisplayLabel) ? ItemLabels.ResolveLabel(pl.DefName) : pl.DisplayLabel;
        }

        private void PromptQty(string defName)
        {
            PersonalItems().TryGetValue(defName, out int have);
            _attachItems.TryGetValue(defName, out int already);
            int max = Math.Max(0, have - already);
            if (max <= 0) { Reject("You've already attached all of those"); return; }
            Find.WindowStack.Add(new Dialog_KMHAmountInput(
                title:        $"Attach {ItemLabels.ResolveLabel(defName)}",
                confirmLabel: "Attach",
                unitLabel:    $"units (you have {have})",
                maxHint:      max,
                onConfirm:    q => { if (q > 0) _attachItems[defName] = already + Math.Min(q, max); }));
        }

        // A guild treasury never backs a mail attachment, so a guild-owned snapshot reports 0.
        private static long PersonalSilver()
        {
            Treasury.Dto.TreasurySnapshot s = TreasuryCache.Snapshot;
            return (s != null && !s.IsGuildOwned) ? Math.Max(0, s.SilverBalance) : 0;
        }

        // Compact items in my personal treasury (defName -> qty). Empty when the snapshot is guild-owned or absent.
        private static Dictionary<string, int> PersonalItems()
        {
            Treasury.Dto.TreasurySnapshot s = TreasuryCache.Snapshot;
            return (s != null && !s.IsGuildOwned && s.Items != null) ? s.Items : new Dictionary<string, int>();
        }

        // Full-state gear payloads in my personal treasury. Empty when the snapshot is guild-owned or absent.
        private static List<KMHPatch.Items.KmhThingPayload> PersonalPayloads()
        {
            Treasury.Dto.TreasurySnapshot s = TreasuryCache.Snapshot;
            return (s != null && !s.IsGuildOwned && s.ItemPayloads != null) ? s.ItemPayloads : new List<KMHPatch.Items.KmhThingPayload>();
        }

        private static void Reject(string msg) => Notifications.KmhNotifications.Rejected(msg);
    }
}
