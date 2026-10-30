using System.Collections.Generic;
using KMHPatch.UI;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace KMHPatch.Features.Marketplace
{
    public class Dialog_KMHPostListing : Window_KMHBase
    {
        public override Vector2 InitialSize => KMHPatch.UI.DialogLayout.FitToScreen(580f, 440f);

        // The vault as it looked at open only, so anything shown or bounding a quantity reads Live* instead.
        private readonly Dictionary<string, int> _treasuryItems;
        private readonly List<KMHPatch.Items.KmhThingPayload> _payloads;
        private bool _subscribed;

        private string _itemDefName    = "";
        private string _fingerprint    = "";
        private int    _itemAvailable  = 0;
        private string _qty            = "";
        private string _unitPriceSilver = "";
        private string _visibility     = MarketplaceHandler.VisibilityPublic;
        // Auto-cancel after this many hours; 0 / blank = the longest the server allows (it has no "never").
        private string _expiresHours   = "0";

        private Dialog_KMHPostListing(Dictionary<string, int> treasuryItems, List<KMHPatch.Items.KmhThingPayload> payloads)
        {
            _treasuryItems = treasuryItems ?? new Dictionary<string, int>();
            _payloads      = payloads ?? new List<KMHPatch.Items.KmhThingPayload>();

            doCloseX                = true;
            absorbInputAroundWindow = true;
            forcePause              = false;
            draggable               = true;
        }

        public static bool Open()
        {
            Treasury.Dto.TreasurySnapshot snap = Treasury.TreasuryCache.Snapshot;
            bool hasSimple  = snap?.Items != null && snap.Items.Count > 0;
            bool hasPayload = snap?.ItemPayloads != null && snap.ItemPayloads.Count > 0;
            if (!hasSimple && !hasPayload)
            {
                int pending = Treasury.TreasuryCache.PendingItemUnits();
                Notifications.KmhNotifications.Rejected(
                    pending == 1 ? KmhText.Get("KMH_PostListing_PendingOne", "You have 1 item pending in your treasury - save your game to finalize it before listing")
                  : pending > 1  ? KmhText.Get("KMH_PostListing_PendingMany", "You have {0} items pending in your treasury - save your game to finalize them before listing", pending)
                  : KmhText.Get("KMH_PostListing_NoItems", "Your treasury has no items to list - deposit some to your vault first"));
                return false;
            }
            Find.WindowStack.Add(new Dialog_KMHPostListing(
                hasSimple ? new Dictionary<string, int>(snap.Items, System.StringComparer.OrdinalIgnoreCase) : null,
                hasPayload ? new List<KMHPatch.Items.KmhThingPayload>(snap.ItemPayloads) : null));
            return true;
        }

        protected override void DrawContents(Rect rect)
        {
            float y = DialogLayout.DrawTitle(rect, KmhText.Get("KMH_PostListing_Title", "Post listing - from your treasury"));
            DialogLayout.DrawSectionDivider(rect, ref y);

            const float labelW = 180f;

            DialogLayout.LabelTrunc(new Rect(0f, y + 4f, labelW, 22f), KmhText.Get("KMH_Market_ColItem", "Item"));
            string itemDisplay = string.IsNullOrEmpty(_itemDefName)
                ? "<color=#A9AFB9>" + KmhText.Get("KMH_PostListing_PickFromTreasury", "(pick from treasury)") + "</color>"
                : ItemKeys.LabelForKey(_itemDefName) + "  <color=#A9AFB9>"
                  + KmhText.Get("KMH_PostListing_Available", "(available x{0})", _itemAvailable) + "</color>";
            if (Widgets.ButtonText(new Rect(labelW, y, rect.width - labelW, 26f), itemDisplay))
            {
                UI.KmhItemPickerService.Open(
                    title:           KmhText.Get("KMH_PostListing_PickTitle", "Pick item to list"),
                    pickActionLabel: KmhText.Get("KMH_PostListing_Select", "Select"),
                    source:          _treasuryItems,
                    onPick:          (defName, qty) =>
                    {
                        _itemDefName   = defName;
                        _fingerprint   = "";
                        _itemAvailable = LiveCount(defName);
                        _qty           = qty.ToString();
                    },
                    refreshSource:   () => Treasury.TreasuryCache.Snapshot?.Items,
                    payloads:        _payloads,
                    onPickPayload:   (pl, qty) =>
                    {
                        _itemDefName   = pl.DisplayLabel;
                        _fingerprint   = pl.Fingerprint;
                        _itemAvailable = pl.StackCount;
                        _qty           = qty.ToString();
                    },
                    refreshPayloads: () => Treasury.TreasuryCache.Snapshot?.ItemPayloads,
                    closeOnPick:     true);
            }
            y += 30f;

            y = DrawTextRow(rect, y, KmhText.Get("KMH_PostListing_Quantity", "Quantity"), ref _qty);
            y = DrawTextRow(rect, y, KmhText.Get("KMH_PostListing_UnitPrice", "Unit price (silver, e.g. 0.55)"), ref _unitPriceSilver);
            y = DrawTextRow(rect, y, KmhText.Get("KMH_PostListing_ExpiresHours", "Expires in (hours, 0 = longest allowed)"), ref _expiresHours);

            DialogLayout.LabelTrunc(new Rect(0f, y + 4f, labelW, 22f), KmhText.Get("KMH_PostListing_Visibility", "Visibility"));
            string visPublic = KmhText.Get("KMH_PostListing_VisPublic", "Public (all servers)");
            string visGuild  = KmhText.Get("KMH_PostListing_VisGuild", "Guild + allies only");
            string visLabel = _visibility == MarketplaceHandler.VisibilityGuildOnly ? visGuild : visPublic;
            if (Widgets.ButtonText(new Rect(labelW, y, rect.width - labelW, 26f), visLabel))
            {
                List<FloatMenuOption> opts = new List<FloatMenuOption>
                {
                    new FloatMenuOption(visPublic,
                        () => _visibility = MarketplaceHandler.VisibilityPublic),
                    new FloatMenuOption(visGuild,
                        () => _visibility = MarketplaceHandler.VisibilityGuildOnly),
                };
                Find.WindowStack.Add(new FloatMenu(opts));
            }
            y += 30f;

            // Estimated payout after the server tax (guild sale tax / world events can shift it - hence "~").
            if (int.TryParse((_qty ?? "").Trim(), out int pvQty) && pvQty > 0
                && TryParseMilli(_unitPriceSilver, out int pvMilli)
                && MarketplaceCache.HasSnapshot)
            {
                int pct = System.Math.Max(0, MarketplaceCache.Snapshot?.ServerTaxPercent ?? 0);
                long gross = (long)System.Math.Round((long)pvMilli * (double)pvQty / 1000.0);   // rounded whole silver, like the sale
                long net   = gross - (long)System.Math.Round(gross * (pct / 100.0));
                DialogLayout.LabelTrunc(new Rect(0f, y, rect.width, 20f),
                    "<color=#A9AFB9>" + KmhText.Get("KMH_PostListing_Payout", "If it fully sells: buyer pays {0}, you receive ~{1} after the {2}% server tax.",
                        "<b>" + SilverFmt.Format(gross) + "</b>", "<b>" + SilverFmt.Format(net) + "</b>", pct) + "</color>");
                y += 24f;
            }

            const float btnW = 120f;
            const float btnH = 32f;
            float btnY = rect.height - btnH - 4f;

            if (IconButton.Draw(new Rect(0f, btnY, btnW, btnH), KMHTextures.Cancel, KmhText.Get("KMH_Common_Cancel", "Cancel")))
            {
                Close();
            }
            if (IconButton.Draw(new Rect(rect.width - btnW, btnY, btnW, btnH), KMHTextures.Post, KmhText.Get("KMH_PostListing_Post", "Post")))
            {
                Submit();
            }
        }

        // The label wraps and the row grows with it: "Expires in (hours, 0 = longest allowed)" does not fit 180px.
        private static float DrawTextRow(Rect rect, float y, string label, ref string value)
        {
            const float labelW = 180f;
            float labelH = DialogLayout.LabelWrapped(0f, y + 4f, labelW - 4f, label);
            value = Widgets.TextField(new Rect(labelW, y, rect.width - labelW, 26f), value ?? "");
            return y + Mathf.Max(30f, labelH + 8f);
        }

        private void Submit()
        {
            if (string.IsNullOrEmpty(_itemDefName))
            {
                Notifications.KmhNotifications.Rejected(KmhText.Get("KMH_PostListing_PickItemFirst", "Pick an item first"));
                return;
            }
            if (!int.TryParse((_qty ?? "").Trim(), out int qty) || qty <= 0)
            {
                Notifications.KmhNotifications.Rejected(KmhText.Get("KMH_PostListing_BadQty", "Quantity: enter a positive whole number"));
                return;
            }
            // Re-read: the vault can change between the draw and Post.
            _itemAvailable = CurrentAvailable();
            if (_itemAvailable <= 0)
            {
                Notifications.KmhNotifications.Rejected(KmhText.Get("KMH_PostListing_NoLongerHeld", "{0} is no longer in your treasury",
                    ItemKeys.LabelForKey(_itemDefName)));
                return;
            }
            if (qty > _itemAvailable)
            {
                Notifications.KmhNotifications.Rejected(KmhText.Get("KMH_PostListing_OnlyHave", "You only have {0} of that to list", _itemAvailable));
                return;
            }
            if (!TryParseMilli(_unitPriceSilver, out int priceMilli, out bool ambiguous))
            {
                Notifications.KmhNotifications.Rejected(ambiguous ? KmhNumberInput.AmbiguousText()
                    : KmhText.Get("KMH_PostListing_BadPrice", "Unit price: enter a positive amount (e.g. 0.55 or 12)"));
                return;
            }
            // Blank, "0" and omitted all mean the server maximum; a negative is rejected rather than silently zeroed.
            string expRaw = (_expiresHours ?? "").Trim();
            int expHours = 0;
            if (expRaw.Length > 0)
            {
                if (!int.TryParse(expRaw, out expHours) || expHours < 0)
                {
                    Notifications.KmhNotifications.Rejected(KmhText.Get("KMH_PostListing_BadExpires",
                        "Expires in: enter 0 (the longest allowed) or a positive whole number of hours"));
                    return;
                }
            }
            bool ok = string.IsNullOrEmpty(_fingerprint)
                ? MarketplaceHandler.TryPost(_itemDefName, qty, priceMilli, _visibility, expHours)
                : MarketplaceHandler.TryPostPayload(_fingerprint, qty, priceMilli, _visibility, expHours);
            if (ok) Close();
        }

        public override void PreOpen()
        {
            base.PreOpen();
            if (!_subscribed) { Treasury.TreasuryCache.Updated += OnTreasuryUpdated; _subscribed = true; }
        }

        public override void PostClose()
        {
            if (_subscribed) { Treasury.TreasuryCache.Updated -= OnTreasuryUpdated; _subscribed = false; }
            base.PostClose();
        }

        // A deposit, sale or compaction can land while the composer sits open - re-read what's about to be listed.
        private void OnTreasuryUpdated()
        {
            if (!string.IsNullOrEmpty(_itemDefName)) _itemAvailable = CurrentAvailable();
        }

        // The one answer to "how many right now", so the drawn number and the validated number can never differ.
        private int CurrentAvailable()
        {
            if (string.IsNullOrEmpty(_fingerprint)) return LiveCount(_itemDefName);

            List<KMHPatch.Items.KmhThingPayload> live = Treasury.TreasuryCache.Snapshot?.ItemPayloads;
            if (live == null) return _itemAvailable;   // no snapshot cached: keep what we have, never read null as "empty"
            foreach (KMHPatch.Items.KmhThingPayload p in live)
                if (p != null && p.Fingerprint == _fingerprint) return p.StackCount;
            return 0;                                  // that exact stack is gone (withdrawn, sold, or merged by compaction)
        }

        // The opening copy is a fallback only when no snapshot exists, so null is never mistaken for an empty vault.
        private int LiveCount(string defName)
        {
            Dictionary<string, int> live = Treasury.TreasuryCache.Snapshot?.Items;
            if (live != null) return live.TryGetValue(defName, out int n) ? n : 0;
            return _treasuryItems.TryGetValue(defName, out int copy) ? copy : 0;
        }

        // decimal, not double: 0.55 is exact in decimal, so the typed price cannot drift on the way to the wire.
        private static bool TryParseMilli(string s, out int milli) => TryParseMilli(s, out milli, out _);

        private static bool TryParseMilli(string s, out int milli, out bool ambiguous)
        {
            milli = 0;
            if (!KmhNumberInput.TryParse(s, out decimal silver, out ambiguous)) return false;
            return Extensibility.KmhSilver.TryToMilli(silver, out milli);
        }
    }
}
