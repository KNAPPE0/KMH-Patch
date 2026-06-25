using System.Collections.Generic;
using KMHPatch.UI;
using UnityEngine;
using Verse;

namespace KMHPatch.Features.WantBoard
{
    // Post composer for a want-to-buy: pick the item from the full catalog, set quantity, per-unit price, duration,
    // visibility. The full ask (price * qty) is escrowed from your treasury server-side, so you can only post wants you can pay for.
    public class Dialog_KMHPostWant : Window_KMHBase
    {
        public override Vector2 InitialSize => new Vector2(580f, 400f);

        private readonly Dictionary<string, int> _catalog;

        private string _itemDef   = "";
        private string _itemLabel = "";
        private string _qty       = "";
        private string _unitPrice = "";
        private string _hours     = "72";
        private string _visibility = "public";

        private Dialog_KMHPostWant(Dictionary<string, int> catalog)
        {
            _catalog = catalog ?? new Dictionary<string, int>();
            doCloseX                = true;
            absorbInputAroundWindow = true;
            forcePause              = false;
            draggable               = true;
        }

        public static void Open()
            => Find.WindowStack.Add(new Dialog_KMHPostWant(ItemDefBrowser.AllPickableItems()));

        protected override void DrawContents(Rect rect)
        {
            float y = DialogLayout.DrawTitle(rect, "Post want - escrowed from your treasury");
            DialogLayout.DrawSectionDivider(rect, ref y);

            const float labelW = 190f;

            DialogLayout.LabelTrunc(new Rect(0f, y + 4f, labelW, 22f), "Item");
            string itemDisplay = string.IsNullOrEmpty(_itemDef)
                ? "<color=grey>(pick an item)</color>"
                : _itemLabel;
            if (Widgets.ButtonText(new Rect(labelW, y, rect.width - labelW, 26f), itemDisplay))
            {
                Find.WindowStack.Add(new Dialog_KMHItemPicker(
                    title:           "Pick item to buy",
                    pickActionLabel: "Select",
                    source:          _catalog,
                    onPick:          (key, qty) =>
                    {
                        _itemDef   = key;
                        _itemLabel = ItemLabels.ResolveLabel(key);
                        if (qty > 0) _qty = qty.ToString();
                    }));
            }
            y += 30f;

            y = Row(rect, y, "Quantity",              ref _qty);
            y = Row(rect, y, "Price per unit (silver)", ref _unitPrice);
            y = Row(rect, y, "Duration (hours)",      ref _hours);

            DialogLayout.LabelTrunc(new Rect(0f, y + 4f, labelW, 22f), "Visibility");
            string visLabel = _visibility == "guild_only" ? "Guild + allies only" : "Public (all servers)";
            if (Widgets.ButtonText(new Rect(labelW, y, rect.width - labelW, 26f), visLabel))
                Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption>
                {
                    new FloatMenuOption("Public (all servers)", () => _visibility = "public"),
                    new FloatMenuOption("Guild + allies only",  () => _visibility = "guild_only"),
                }));
            y += 34f;

            // Live total so the buyer sees what will be escrowed.
            if (int.TryParse(T(_qty), out int q) && q > 0 && int.TryParse(T(_unitPrice), out int p) && p > 0)
            {
                Color old = GUI.color; GUI.color = DialogLayout.MutedColor;
                DialogLayout.LabelTrunc(new Rect(0f, y, rect.width, 20f),
                    $"Escrow on post: <color=yellow>{SilverFmt.Format((long)q * p)}</color> silver");
                GUI.color = old;
            }

            const float btnW = 120f, btnH = 32f;
            float btnY = rect.height - btnH - 4f;
            if (IconButton.Draw(new Rect(0f, btnY, btnW, btnH), KMHTextures.Cancel, "Cancel")) Close();
            if (IconButton.Draw(new Rect(rect.width - btnW, btnY, btnW, btnH), KMHTextures.Post, "Post")) Submit();
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
            if (string.IsNullOrEmpty(_itemDef)) { Reject("Pick an item first"); return; }
            if (!int.TryParse(T(_qty), out int qty) || qty <= 0) { Reject("Quantity: positive whole number"); return; }
            if (!int.TryParse(T(_unitPrice), out int price) || price <= 0) { Reject("Price per unit: positive whole number"); return; }
            if (!int.TryParse(T(_hours), out int hours) || hours <= 0) { Reject("Duration: positive whole number of hours"); return; }

            if (WantHandler.TryPost(_itemDef, qty, price, hours, _visibility))
                Close();
        }

        private static string T(string s) => (s ?? "").Trim();
        private static void Reject(string msg) => Notifications.KmhNotifications.Rejected(msg);
    }
}
