using System.Collections.Generic;
using KMHPatch.UI;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace KMHPatch.Features.Marketplace
{
    // single-form Post composer: item, qty, price, Public/Guild-only visibility
    //
    // Source for the target item is the player's selected caravan. We surface a 'Select a caravan first' rejection
    // if no selection (same UX as Treasury Deposit) rather than opening an empty picker
    public class Dialog_KMHPostListing : Window_KMHBase
    {
        public override Vector2 InitialSize => new Vector2(580f, 440f);

        // Treasury items snapshot taken at open time. Listings escrow from your TREASURY server-side
        // (MarketplaceStore.Post -> TreasuryStore.WithdrawItem), so you list what you've deposited to your vault,
        // not raw caravan items. Cancel + reopen to refresh
        private readonly Dictionary<string, int> _treasuryItems;

        private string _itemDefName    = "";
        private int    _itemAvailable  = 0;     // capped maxHint for qty prompt
        private string _qty            = "";
        private string _unitPriceSilver = "";
        private string _visibility     = MarketplaceHandler.VisibilityPublic;
        // Auto-cancel after this many hours; 0 / blank = never expires.
        private string _expiresHours   = "0";

        private Dialog_KMHPostListing(Dictionary<string, int> treasuryItems)
        {
            _treasuryItems = treasuryItems ?? new Dictionary<string, int>();

            doCloseX                = true;
            absorbInputAroundWindow = true;
            forcePause              = false;
            draggable               = true;
        }

        // Entry point - opens the composer when the treasury has items to list, surfaces a rejection otherwise.
        // Returns whether the composer opened
        public static bool Open()
        {
            Treasury.Dto.TreasurySnapshot snap = Treasury.TreasuryCache.Snapshot;
            if (snap?.Items == null || snap.Items.Count == 0)
            {
                Notifications.KmhNotifications.Rejected("Your treasury has no items to list - deposit some to your vault first");
                return false;
            }
            Find.WindowStack.Add(new Dialog_KMHPostListing(
                new Dictionary<string, int>(snap.Items, System.StringComparer.OrdinalIgnoreCase)));
            return true;
        }

        protected override void DrawContents(Rect rect)
        {
            float y = DialogLayout.DrawTitle(rect, "Post listing - from your treasury");
            DialogLayout.DrawSectionDivider(rect, ref y);

            const float labelW = 180f;

            // Item picker row
            DialogLayout.LabelTrunc(new Rect(0f, y + 4f, labelW, 22f), "Item");
            string itemDisplay = string.IsNullOrEmpty(_itemDefName)
                ? "<color=grey>(pick from treasury)</color>"
                : $"{ItemLabels.ResolveLabel(_itemDefName)}  <color=grey>(available x{_itemAvailable})</color>";
            if (Widgets.ButtonText(new Rect(labelW, y, rect.width - labelW, 26f), itemDisplay))
            {
                Find.WindowStack.Add(new Dialog_KMHItemPicker(
                    title:           "Pick item to list",
                    pickActionLabel: "Select",
                    source:          _treasuryItems,
                    onPick:          (defName, qty) =>
                    {
                        _itemDefName   = defName;
                        _itemAvailable = _treasuryItems.TryGetValue(defName, out int max) ? max : 0;
                        _qty           = qty.ToString();
                    }));
            }
            y += 30f;

            y = DrawTextRow(rect, y, "Quantity",          ref _qty);
            y = DrawTextRow(rect, y, "Unit price (silver)", ref _unitPriceSilver);
            y = DrawTextRow(rect, y, "Expires in (hours, 0=never)", ref _expiresHours);

            // Visibility toggle row.
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
            if (!int.TryParse((_unitPriceSilver ?? "").Trim(), out int price) || price <= 0)
            {
                Notifications.KmhNotifications.Rejected("Unit price: enter a positive whole number");
                return;
            }
            // Expiry: empty + "0" + omitted all mean "never expires". Reject negative explicitly to avoid silent
            // zero-out
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
            if (MarketplaceHandler.TryPost(_itemDefName, qty, price, _visibility, expHours))
            {
                Close();
            }
        }
    }
}
