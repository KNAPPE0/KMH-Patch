using System.Collections.Generic;
using KMHPatch.UI;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace KMHPatch.Features.Marketplace
{
    // Post composer: item, qty, unit price, visibility, expiry. Lists from your treasury, not the caravan.
    public class Dialog_KMHPostListing : Window_KMHBase
    {
        public override Vector2 InitialSize => KMHPatch.UI.DialogLayout.FitToScreen(580f, 440f);

        // The vault as it looked at open only, so anything shown or bounding a quantity reads Live* instead.
        private readonly Dictionary<string, int> _treasuryItems;
        private readonly List<KMHPatch.Items.KmhThingPayload> _payloads;
        private bool _subscribed;

        private string _itemDefName    = "";
        private string _fingerprint    = "";     // non-empty => full-state payload listing
        private int    _itemAvailable  = 0;     // capped maxHint for qty prompt
        private string _qty            = "";
        private string _unitPriceSilver = "";
        private string _visibility     = MarketplaceHandler.VisibilityPublic;
        // Auto-cancel after this many hours; 0 / blank = never expires.
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
                Notifications.KmhNotifications.Rejected(Treasury.TreasuryCache.HasPendingItems()
                    ? $"You have {Treasury.TreasuryCache.PendingItemUnits()} item(s) pending in your treasury - save your game to finalize them before listing"
                    : "Your treasury has no items to list - deposit some to your vault first");
                return false;
            }
            Find.WindowStack.Add(new Dialog_KMHPostListing(
                hasSimple ? new Dictionary<string, int>(snap.Items, System.StringComparer.OrdinalIgnoreCase) : null,
                hasPayload ? new List<KMHPatch.Items.KmhThingPayload>(snap.ItemPayloads) : null));
            return true;
        }

        protected override void DrawContents(Rect rect)
        {
            float y = DialogLayout.DrawTitle(rect, "Post listing - from your treasury");
            DialogLayout.DrawSectionDivider(rect, ref y);

            const float labelW = 180f;

            DialogLayout.LabelTrunc(new Rect(0f, y + 4f, labelW, 22f), "Item");
            string itemDisplay = string.IsNullOrEmpty(_itemDefName)
                ? "<color=grey>(pick from treasury)</color>"
                : $"{ItemKeys.LabelForKey(_itemDefName)}  <color=grey>(available x{_itemAvailable})</color>";
            if (Widgets.ButtonText(new Rect(labelW, y, rect.width - labelW, 26f), itemDisplay))
            {
                // Same picker the Treasury uses; full-state stacks keep their exact state through escrow.
                UI.KmhItemPickerService.Open(
                    title:           "Pick item to list",
                    pickActionLabel: "Select",
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

            y = DrawTextRow(rect, y, "Quantity",          ref _qty);
            y = DrawTextRow(rect, y, "Unit price (silver, e.g. 0.55)", ref _unitPriceSilver);
            y = DrawTextRow(rect, y, "Expires in (hours, 0=never)", ref _expiresHours);

            DialogLayout.LabelTrunc(new Rect(0f, y + 4f, labelW, 22f), "Visibility");
            string visLabel = _visibility == MarketplaceHandler.VisibilityGuildOnly
                ? "Guild + allies only"
                : "Public (all servers)";
            if (Widgets.ButtonText(new Rect(labelW, y, rect.width - labelW, 26f), visLabel))
            {
                List<FloatMenuOption> opts = new List<FloatMenuOption>
                {
                    new FloatMenuOption("Public (all servers)",
                        () => _visibility = MarketplaceHandler.VisibilityPublic),
                    new FloatMenuOption("Guild + allies only",
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
                    $"<color=grey>If it fully sells: buyer pays <b>{SilverFmt.Format(gross)}</b>, you receive ~<b>{SilverFmt.Format(net)}</b> after the {pct}% server tax.</color>");
                y += 24f;
            }

            const float btnW = 120f;
            const float btnH = 32f;
            float btnY = rect.height - btnH - 4f;

            if (IconButton.Draw(new Rect(0f, btnY, btnW, btnH), KMHTextures.Cancel, "Cancel"))
            {
                Close();
            }
            if (IconButton.Draw(new Rect(rect.width - btnW, btnY, btnW, btnH), KMHTextures.Post, "Post"))
            {
                Submit();
            }
        }

        private static float DrawTextRow(Rect rect, float y, string label, ref string value)
        {
            const float labelW = 180f;
            DialogLayout.LabelTrunc(new Rect(0f, y + 4f, labelW, 22f), label);
            value = Widgets.TextField(new Rect(labelW, y, rect.width - labelW, 26f), value ?? "");
            return y + 30f;
        }

        private void Submit()
        {
            if (string.IsNullOrEmpty(_itemDefName))
            {
                Notifications.KmhNotifications.Rejected("Pick an item first");
                return;
            }
            if (!int.TryParse((_qty ?? "").Trim(), out int qty) || qty <= 0)
            {
                Notifications.KmhNotifications.Rejected("Quantity: enter a positive whole number");
                return;
            }
            // Re-read: the vault can change between the draw and Post.
            _itemAvailable = CurrentAvailable();
            if (_itemAvailable <= 0)
            {
                Notifications.KmhNotifications.Rejected($"{ItemKeys.LabelForKey(_itemDefName)} is no longer in your treasury");
                return;
            }
            if (qty > _itemAvailable)
            {
                Notifications.KmhNotifications.Rejected($"You only have {_itemAvailable} of that to list");
                return;
            }
            if (!TryParseMilli(_unitPriceSilver, out int priceMilli))
            {
                Notifications.KmhNotifications.Rejected("Unit price: enter a positive amount (e.g. 0.55 or 12)");
                return;
            }
            // Blank, "0" and omitted all mean never; a negative is rejected rather than silently zeroed.
            string expRaw = (_expiresHours ?? "").Trim();
            int expHours = 0;
            if (expRaw.Length > 0)
            {
                if (!int.TryParse(expRaw, out expHours) || expHours < 0)
                {
                    Notifications.KmhNotifications.Rejected("Expires in: enter 0 (never) or a positive whole number of hours");
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
        private static bool TryParseMilli(string s, out int milli)
        {
            milli = 0;
            string t = (s ?? "").Trim().Replace(',', '.');
            if (!decimal.TryParse(t, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out decimal silver))
                return false;
            return Extensibility.KmhSilver.TryToMilli(silver, out milli);
        }
    }
}
