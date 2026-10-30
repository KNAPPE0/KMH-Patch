using System.Collections.Generic;
using KMHPatch.UI;
using UnityEngine;
using Verse;

namespace KMHPatch.Features.Auctions
{
    // The server escrows from the treasury, so a player can only auction what they already deposited.
    public class Dialog_KMHPostAuction : Window_KMHBase
    {
        public override Vector2 InitialSize => KMHPatch.UI.DialogLayout.FitToScreen(580f, 470f);

        // Only the vault as it looked when the form opened - read Live* for anything shown or used as a quantity bound.
        private readonly Dictionary<string, int> _treasuryItems;
        private readonly List<KMHPatch.Items.KmhThingPayload> _payloads;
        private bool _subscribed;

        private string _itemKey       = "";
        private string _fingerprint   = "";
        private int    _itemAvailable = 0;
        private string _qty           = "";
        private string _startingBid   = "";
        private string _minIncrement  = "1";
        private string _buyout        = "0";
        private string _hours         = "24";
        private string _visibility    = "public";

        private Dialog_KMHPostAuction(Dictionary<string, int> treasuryItems, List<KMHPatch.Items.KmhThingPayload> payloads)
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
                    pending == 1 ? KmhText.Get("KMH_PostAuction_PendingOne", "You have 1 item pending in your treasury - save your game to finalize it before auctioning")
                  : pending > 1  ? KmhText.Get("KMH_PostAuction_PendingMany", "You have {0} items pending in your treasury - save your game to finalize them before auctioning", pending)
                  : KmhText.Get("KMH_PostAuction_NoItems", "Your treasury has no items to auction - deposit some to your vault first"));
                return false;
            }
            Find.WindowStack.Add(new Dialog_KMHPostAuction(
                hasSimple ? new Dictionary<string, int>(snap.Items, System.StringComparer.OrdinalIgnoreCase) : null,
                hasPayload ? new List<KMHPatch.Items.KmhThingPayload>(snap.ItemPayloads) : null));
            return true;
        }

        protected override void DrawContents(Rect rect)
        {
            float y = DialogLayout.DrawTitle(rect, KmhText.Get("KMH_PostAuction_Title", "Post auction - from your treasury"));
            DialogLayout.DrawSectionDivider(rect, ref y);

            const float labelW = 190f;

            DialogLayout.LabelTrunc(new Rect(0f, y + 4f, labelW, 22f), KmhText.Get("KMH_Market_ColItem", "Item"));
            string itemDisplay = string.IsNullOrEmpty(_itemKey)
                ? "<color=#A9AFB9>" + KmhText.Get("KMH_PostListing_PickFromTreasury", "(pick from treasury)") + "</color>"
                : ItemKeys.LabelForKey(_itemKey) + "  <color=#A9AFB9>"
                  + KmhText.Get("KMH_PostListing_Available", "(available x{0})", _itemAvailable) + "</color>";
            if (Widgets.ButtonText(new Rect(labelW, y, rect.width - labelW, 26f), itemDisplay))
            {
                UI.KmhItemPickerService.Open(
                    title:           KmhText.Get("KMH_PostAuction_PickTitle", "Pick item to auction"),
                    pickActionLabel: KmhText.Get("KMH_PostListing_Select", "Select"),
                    source:          _treasuryItems,
                    onPick:          (key, qty) =>
                    {
                        _itemKey       = key;
                        _fingerprint   = "";
                        _itemAvailable = LiveCount(key);
                        _qty           = qty.ToString();
                    },
                    refreshSource:   () => Treasury.TreasuryCache.Snapshot?.Items,
                    payloads:        _payloads,
                    onPickPayload:   (pl, qty) =>
                    {
                        _itemKey       = pl.DisplayLabel;
                        _fingerprint   = pl.Fingerprint;
                        _itemAvailable = pl.StackCount;
                        _qty           = qty.ToString();
                    },
                    refreshPayloads: () => Treasury.TreasuryCache.Snapshot?.ItemPayloads,
                    closeOnPick:     true);
            }
            y += 30f;

            y = Row(rect, y, KmhText.Get("KMH_PostListing_Quantity", "Quantity"), ref _qty);
            y = Row(rect, y, KmhText.Get("KMH_PostAuction_StartingBid", "Starting bid (silver)"), ref _startingBid);
            y = Row(rect, y, KmhText.Get("KMH_PostAuction_MinRaise", "Min raise (silver)"), ref _minIncrement);
            y = Row(rect, y, KmhText.Get("KMH_PostAuction_Buyout", "Buyout (silver, 0=none)"), ref _buyout);
            y = Row(rect, y, KmhText.Get("KMH_PostAuction_Duration", "Duration (hours)"), ref _hours);

            DialogLayout.LabelTrunc(new Rect(0f, y + 4f, labelW, 22f), KmhText.Get("KMH_PostListing_Visibility", "Visibility"));
            string visPublic = KmhText.Get("KMH_PostListing_VisPublic", "Public (all servers)");
            string visGuild  = KmhText.Get("KMH_PostListing_VisGuild", "Guild + allies only");
            string visLabel = _visibility == "guild_only" ? visGuild : visPublic;
            if (Widgets.ButtonText(new Rect(labelW, y, rect.width - labelW, 26f), visLabel))
                Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption>
                {
                    new FloatMenuOption(visPublic, () => _visibility = "public"),
                    new FloatMenuOption(visGuild,  () => _visibility = "guild_only"),
                }));
            y += 30f;

            const float btnW = 120f, btnH = 32f;
            float btnY = rect.height - btnH - 4f;
            if (IconButton.Draw(new Rect(0f, btnY, btnW, btnH), KMHTextures.Cancel, KmhText.Get("KMH_Common_Cancel", "Cancel"))) Close();
            if (IconButton.Draw(new Rect(rect.width - btnW, btnY, btnW, btnH), KMHTextures.Post, KmhText.Get("KMH_PostListing_Post", "Post"))) Submit();
        }

        private static float Row(Rect rect, float y, string label, ref string value)
        {
            const float labelW = 190f;
            DialogLayout.LabelTrunc(new Rect(0f, y + 4f, labelW, 22f), label);
            value = Widgets.TextField(new Rect(labelW, y, rect.width - labelW, 26f), value ?? "");
            return y + 30f;
        }

        private void Submit()
        {
            if (string.IsNullOrEmpty(_itemKey)) { Reject(KmhText.Get("KMH_PostListing_PickItemFirst", "Pick an item first")); return; }
            if (!int.TryParse(T(_qty), out int qty) || qty <= 0)
            { Reject(KmhText.Get("KMH_PostAuction_BadQty", "Quantity: positive whole number")); return; }

            // Re-read: the vault can change between the draw and Submit.
            _itemAvailable = CurrentAvailable();
            if (_itemAvailable <= 0)
            { Reject(KmhText.Get("KMH_PostListing_NoLongerHeld", "{0} is no longer in your treasury", ItemKeys.LabelForKey(_itemKey))); return; }
            if (qty > _itemAvailable)
            { Reject(KmhText.Get("KMH_PostAuction_OnlyHave", "You only have {0} of that to auction", _itemAvailable)); return; }

            if (!int.TryParse(T(_startingBid), out int start) || start <= 0)
            { Reject(KmhText.Get("KMH_PostAuction_BadStart", "Starting bid: positive whole number")); return; }
            if (!int.TryParse(T(_minIncrement), out int inc) || inc <= 0)
            { Reject(KmhText.Get("KMH_PostAuction_BadRaise", "Min raise: positive whole number")); return; }
            int buyout = 0;
            if (T(_buyout).Length > 0 && (!int.TryParse(T(_buyout), out buyout) || buyout < 0))
            { Reject(KmhText.Get("KMH_PostAuction_BadBuyout", "Buyout: 0 (none) or a positive whole number")); return; }
            if (buyout > 0 && buyout < start)
            { Reject(KmhText.Get("KMH_PostAuction_BuyoutBelowStart", "Buyout must be at least the starting bid")); return; }
            if (!int.TryParse(T(_hours), out int hours) || hours <= 0)
            { Reject(KmhText.Get("KMH_PostAuction_BadDuration", "Duration: positive whole number of hours")); return; }

            bool ok = string.IsNullOrEmpty(_fingerprint)
                ? AuctionHandler.TryPost(_itemKey, "", 0, qty, start, inc, buyout, hours, _visibility)
                : AuctionHandler.TryPostPayload(_fingerprint, qty, start, inc, buyout, hours, _visibility);
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

        // A deposit, sale or compaction can land while the form sits open - re-read what's about to be auctioned.
        private void OnTreasuryUpdated()
        {
            if (!string.IsNullOrEmpty(_itemKey)) _itemAvailable = CurrentAvailable();
        }

        // The one answer to "how many right now", so the drawn number and the validated number can never differ.
        private int CurrentAvailable()
        {
            if (string.IsNullOrEmpty(_fingerprint)) return LiveCount(_itemKey);

            List<KMHPatch.Items.KmhThingPayload> live = Treasury.TreasuryCache.Snapshot?.ItemPayloads;
            if (live == null) return _itemAvailable;   // no snapshot cached: keep what we have, never read null as "empty"
            foreach (KMHPatch.Items.KmhThingPayload p in live)
                if (p != null && p.Fingerprint == _fingerprint) return p.StackCount;
            return 0;                                  // that exact stack is gone (withdrawn, sold, or merged by compaction)
        }

        // The opening copy is a fallback only when no snapshot exists, so null is never mistaken for an empty vault.
        private int LiveCount(string key)
        {
            Dictionary<string, int> live = Treasury.TreasuryCache.Snapshot?.Items;
            if (live != null) return live.TryGetValue(key, out int n) ? n : 0;
            return _treasuryItems.TryGetValue(key, out int copy) ? copy : 0;
        }

        private static string T(string s) => (s ?? "").Trim();
        private static void Reject(string msg) => Notifications.KmhNotifications.Rejected(msg);
    }
}
