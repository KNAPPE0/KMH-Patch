using System.Collections.Generic;
using KMHPatch.UI;
using RimWorld;
using UnityEngine;
using Verse;

namespace KMHPatch.Features.WantBoard
{
    // The server escrows price * qty from the treasury, so only a want the player can pay for is accepted.
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
        private bool   _allowComplex = false;
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
            float y = DialogLayout.DrawTitle(rect, KmhText.Get("KMH_PostWant_Title", "Post want - escrowed from your treasury"));
            DialogLayout.DrawSectionDivider(rect, ref y);

            string itemDisplay = string.IsNullOrEmpty(_itemDef)
                ? "<color=#A9AFB9>" + KmhText.Get("KMH_PostWant_PickAnItem", "(pick an item)") + "</color>"
                : _itemLabel;
            if (ButtonRow(rect, y, KmhText.Get("KMH_Market_ColItem", "Item"), itemDisplay))
            {
                KmhItemPickerService.Open(
                    title:           KmhText.Get("KMH_PostWant_PickTitle", "Pick item to buy"),
                    pickActionLabel: KmhText.Get("KMH_PostListing_Select", "Select"),
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

            y = DrawRequirements(rect, y);

            y = Row(rect, y, KmhText.Get("KMH_PostListing_Quantity", "Quantity"), ref _qty);
            y = Row(rect, y, Decimals ? KmhText.Get("KMH_PostWant_PricePerUnitDecimal", "Price per unit (silver, e.g. 0.55)")
                                      : KmhText.Get("KMH_PostWant_PricePerUnit", "Price per unit (silver)"), ref _unitPrice);
            y = Row(rect, y, KmhText.Get("KMH_PostAuction_Duration", "Duration (hours)"), ref _hours);

            string visPublic = KmhText.Get("KMH_PostListing_VisPublic", "Public (all servers)");
            string visGuild  = KmhText.Get("KMH_PostListing_VisGuild", "Guild + allies only");
            string visLabel = _visibility == "guild_only" ? visGuild : visPublic;
            if (ButtonRow(rect, y, KmhText.Get("KMH_PostListing_Visibility", "Visibility"), visLabel))
                Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption>
                {
                    new FloatMenuOption(visPublic, () => _visibility = "public"),
                    new FloatMenuOption(visGuild,  () => _visibility = "guild_only"),
                }));
            y += 34f;

            // Off by default, so a buyer is never handed junk gear.
            Widgets.CheckboxLabeled(new Rect(0f, y, rect.width, 24f),
                KmhText.Get("KMH_PostWant_AcceptUsed", "Accept used gear (weapons/apparel, full state)"), ref _allowComplex);
            y += 26f;
            if (_allowComplex)
            {
                // Indented by the rect, not leading spaces: a Keyed value's leading spaces are not reliably kept.
                Widgets.CheckboxLabeled(new Rect(32f, y, rect.width - 32f, 24f),
                    KmhText.Get("KMH_PostWant_EvenDamaged", "...even if damaged"), ref _allowDamaged);
                y += 26f;
                Widgets.CheckboxLabeled(new Rect(32f, y, rect.width - 32f, 24f),
                    KmhText.Get("KMH_PostWant_EvenTainted", "...even if tainted (worn by the dead)"), ref _allowTainted);
                y += 26f;
            }
            else { _allowDamaged = false; _allowTainted = false; }
            y += 6f;

            if (int.TryParse(T(_qty), out int q) && q > 0 && TryParsePrice(T(_unitPrice), Decimals, out int p, out _))
            {
                Color old = GUI.color; GUI.color = DialogLayout.MutedColor;
                DialogLayout.LabelTrunc(new Rect(0f, y, rect.width, 20f),
                    KmhText.Get("KMH_PostWant_EscrowOnPost", "Escrow on post: {0} silver",
                        "<color=yellow>" + SilverFmt.Format(EscrowFor(p, q)) + "</color>"));
                GUI.color = old;
            }

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

        private static bool ButtonRow(Rect rect, float y, string label, string value)
        {
            const float labelW = 190f;
            DialogLayout.LabelTrunc(new Rect(0f, y + 4f, labelW, 22f), label);
            return Widgets.ButtonText(new Rect(labelW, y, rect.width - labelW, 26f), value);
        }

        private float DrawRequirements(Rect rect, float y)
        {
            ThingDef td = SelectedDef();
            if (td == null) return y;

            if (td.HasComp(typeof(CompQuality)))
            {
                string label = _minQuality > 0 ? Dialog_KMHWantBoard.QualityOrBetter(_minQuality) : KmhText.Get("KMH_PostWant_Any", "Any");
                if (ButtonRow(rect, y, KmhText.Get("KMH_PostWant_MinQuality", "Minimum quality"), label))
                {
                    List<FloatMenuOption> opts = new List<FloatMenuOption>
                        { new FloatMenuOption(KmhText.Get("KMH_PostWant_Any", "Any"), () => _minQuality = 0) };
                    for (int q = 1; q <= 7; q++)
                    {
                        int pick = q;
                        opts.Add(new FloatMenuOption(Dialog_KMHWantBoard.QualityOrBetter(q), () => _minQuality = pick));
                    }
                    Find.WindowStack.Add(new FloatMenu(opts));
                }
                y += 30f;
            }
            else _minQuality = 0;

            if (td.MadeFromStuff)
            {
                if (ButtonRow(rect, y, KmhText.Get("KMH_PostWant_RequiredMaterial", "Required material"),
                        string.IsNullOrEmpty(_stuffDef) ? KmhText.Get("KMH_PostWant_Any", "Any") : _stuffLabel))
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
            { new FloatMenuOption(KmhText.Get("KMH_PostWant_Any", "Any"), () => { _stuffDef = ""; _stuffLabel = ""; }) };

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
            if (string.IsNullOrEmpty(_itemDef)) { Reject(KmhText.Get("KMH_PostListing_PickItemFirst", "Pick an item first")); return; }
            if (!int.TryParse(T(_qty), out int qty) || qty <= 0)
            { Reject(KmhText.Get("KMH_PostAuction_BadQty", "Quantity: positive whole number")); return; }
            if (!TryParsePrice(T(_unitPrice), Decimals, out int price, out string why)) { Reject(why); return; }
            if (!int.TryParse(T(_hours), out int hours) || hours <= 0)
            { Reject(KmhText.Get("KMH_PostAuction_BadDuration", "Duration: positive whole number of hours")); return; }

            if (WantHandler.TryPost(_itemDef, qty, price, hours, _visibility,
                    minQuality: _minQuality, requiredStuff: _stuffDef,
                    allowComplex: _allowComplex, allowTainted: _allowTainted, allowDamaged: _allowDamaged))
                Close();
        }

        internal static bool Decimals => SubProtocol.KmhCapabilities.Has(SubProtocol.KmhCapabilities.WantDecimalPrices);

        // As a listing's price where the server takes one; whole silver otherwise, which is all an older server reads.
        internal static bool TryParsePrice(string s, bool decimals, out int milli, out string why)
        {
            why = null;
            if (!KmhNumberInput.TryParse(s, out decimal silver, out bool ambiguous) || !Extensibility.KmhSilver.TryToMilli(silver, out milli))
            {
                milli = 0;
                why = ambiguous ? KmhNumberInput.AmbiguousText()
                    : decimals ? KmhText.Get("KMH_PostWant_BadPriceDecimal", "Price per unit: enter a positive amount (e.g. 0.55 or 12)")
                               : KmhText.Get("KMH_PostWant_BadPrice", "Price per unit: positive whole number");
                return false;
            }
            if (!decimals && milli % 1000 != 0)
            {
                why = KmhText.Get("KMH_PostWant_WholeOnly", "This server takes whole silver for wants.");
                return false;
            }
            return true;
        }

        // What the server escrows: the total rounded up to whole silver, as its WantStore.CostUpTo does.
        internal static long EscrowFor(int milli, int qty) => milli <= 0 || qty <= 0 ? 0 : ((long)milli * qty + 999) / 1000;

        private static string T(string s) => (s ?? "").Trim();
        private static void Reject(string msg) => Notifications.KmhNotifications.Rejected(msg);
    }
}
