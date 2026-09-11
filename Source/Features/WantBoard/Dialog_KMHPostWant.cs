using System.Collections.Generic;
using KMHPatch.UI;
using RimWorld;
using UnityEngine;
using Verse;

namespace KMHPatch.Features.WantBoard
{
    // The full ask (price * qty) is escrowed from your treasury server-side, so you can only post wants you can pay for.
    public class Dialog_KMHPostWant : Window_KMHBase
    {
        public override Vector2 InitialSize => KMHPatch.UI.DialogLayout.FitToScreen(580f, 560f);

        private readonly Dictionary<string, int> _catalog;

        private string _itemDef   = "";
        private string _itemLabel = "";
        private string _qty       = "";
        private string _unitPrice = "";
        private string _hours     = "72";
        private string _visibility = "public";
        private bool   _allowComplex = false; // accept full-state gear (weapons/apparel), else clean simple items only
        private bool   _allowDamaged = false;
        private bool   _allowTainted = false;

        // Unset posts exactly what earlier versions did, and each requirement is only offered for an item that can carry it.
        private int    _minQuality = 0;    // 0 = any, else 1..7 (Awful..Legendary)
        private string _stuffDef   = "";   // "" = any material
        private string _stuffLabel = "";

        private Dialog_KMHPostWant(Dictionary<string, int> catalog)
        {
            _catalog = catalog ?? new Dictionary<string, int>();
            doCloseX                = true;
            absorbInputAroundWindow = true;
            forcePause              = false;
            draggable               = true;
        }

        public static void Open()
            => Find.WindowStack.Add(new Dialog_KMHPostWant(KmhItemPickerService.AllPickableItems()));

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
                KmhItemPickerService.Open(
                    title:           "Pick item to buy",
                    pickActionLabel: "Select",
                    source:          _catalog,
                    onPick:          (key, qty) =>
                    {
                        _itemDef   = key;
                        _itemLabel = ItemLabels.ResolveLabel(key);
                        if (qty > 0) _qty = qty.ToString();
                        // A requirement is only meaningful for the item it was chosen for.
                        _minQuality = 0; _stuffDef = ""; _stuffLabel = "";
                    },
                    closeOnPick:     true);
            }
            y += 30f;

            y = DrawRequirements(rect, y, labelW);

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

            // Match constraints. Off by default = clean simple items only, so a buyer never gets handed junk gear.
            Widgets.CheckboxLabeled(new Rect(0f, y, rect.width, 24f), "Accept used gear (weapons/apparel, full state)", ref _allowComplex);
            y += 26f;
            if (_allowComplex)
            {
                Widgets.CheckboxLabeled(new Rect(24f, y, rect.width - 24f, 24f), "  ...even if damaged", ref _allowDamaged);
                y += 26f;
                Widgets.CheckboxLabeled(new Rect(24f, y, rect.width - 24f, 24f), "  ...even if tainted (worn by the dead)", ref _allowTainted);
                y += 26f;
            }
            else { _allowDamaged = false; _allowTainted = false; }
            y += 6f;

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

        private float DrawRequirements(Rect rect, float y, float labelW)
        {
            ThingDef td = SelectedDef();
            if (td == null) return y;

            if (td.HasComp(typeof(CompQuality)))
            {
                DialogLayout.LabelTrunc(new Rect(0f, y + 4f, labelW, 22f), "Minimum quality");
                string label = _minQuality > 0 ? $"{ItemKeys.QualityName(_minQuality)} or better" : "Any";
                if (Widgets.ButtonText(new Rect(labelW, y, rect.width - labelW, 26f), label))
                {
                    List<FloatMenuOption> opts = new List<FloatMenuOption> { new FloatMenuOption("Any", () => _minQuality = 0) };
                    for (int q = 1; q <= 7; q++)
                    {
                        int pick = q;
                        opts.Add(new FloatMenuOption($"{ItemKeys.QualityName(q)} or better", () => _minQuality = pick));
                    }
                    Find.WindowStack.Add(new FloatMenu(opts));
                }
                y += 30f;
            }
            else _minQuality = 0;

            if (td.MadeFromStuff)
            {
                DialogLayout.LabelTrunc(new Rect(0f, y + 4f, labelW, 22f), "Required material");
                if (Widgets.ButtonText(new Rect(labelW, y, rect.width - labelW, 26f),
                        string.IsNullOrEmpty(_stuffDef) ? "Any" : _stuffLabel))
                    Find.WindowStack.Add(new FloatMenu(StuffOptions(td)));
                y += 30f;
            }
            else { _stuffDef = ""; _stuffLabel = ""; }

            return y;
        }

        // Only materials RimWorld itself allows for this def, so a want can never require an impossible material.
        private List<FloatMenuOption> StuffOptions(ThingDef td)
        {
            List<FloatMenuOption> opts = new List<FloatMenuOption>
            { new FloatMenuOption("Any", () => { _stuffDef = ""; _stuffLabel = ""; }) };

            List<ThingDef> stuffs = new List<ThingDef>();
            try { foreach (ThingDef s in GenStuff.AllowedStuffsFor(td)) if (s != null) stuffs.Add(s); }
            catch { return opts; }

            stuffs.Sort((a, b) => string.Compare(a.LabelCap.ToString(), b.LabelCap.ToString(), System.StringComparison.OrdinalIgnoreCase));
            foreach (ThingDef s in stuffs)
            {
                ThingDef pick = s;
                opts.Add(new FloatMenuOption(pick.LabelCap.ToString(),
                    () => { _stuffDef = pick.defName; _stuffLabel = pick.LabelCap.ToString(); }));
            }
            return opts;
        }

        private ThingDef SelectedDef()
        {
            if (string.IsNullOrEmpty(_itemDef)) return null;
            ItemKeys.Split(_itemDef, out string defName, out _, out _);
            try { return DefDatabase<ThingDef>.GetNamedSilentFail(defName); } catch { return null; }
        }

        private void Submit()
        {
            if (string.IsNullOrEmpty(_itemDef)) { Reject("Pick an item first"); return; }
            if (!int.TryParse(T(_qty), out int qty) || qty <= 0) { Reject("Quantity: positive whole number"); return; }
            if (!int.TryParse(T(_unitPrice), out int price) || price <= 0) { Reject("Price per unit: positive whole number"); return; }
            if (!int.TryParse(T(_hours), out int hours) || hours <= 0) { Reject("Duration: positive whole number of hours"); return; }

            if (WantHandler.TryPost(_itemDef, qty, price, hours, _visibility,
                    minQuality: _minQuality, requiredStuff: _stuffDef,
                    allowComplex: _allowComplex, allowTainted: _allowTainted, allowDamaged: _allowDamaged))
                Close();
        }

        private static string T(string s) => (s ?? "").Trim();
        private static void Reject(string msg) => Notifications.KmhNotifications.Rejected(msg);
    }
}
