using System.Collections.Generic;
using KMHPatch.Features.Sites.Dto;
using KMHPatch.UI;
using RimWorld;
using UnityEngine;
using Verse;

namespace KMHPatch.Features.Sites
{
    // Build a custom site at the player's current world tile. Picks an item to produce (its market value drives
    // cost + cycle time), an amount per cycle, access mode, owner tax, and reward destination. The server
    // re-validates everything and replies with a chat reason on rejection
    public class Dialog_KMHBuildSite : Window_KMHBase
    {
        public override Vector2 InitialSize => new Vector2(600f, 540f);

        private string _itemDef    = "";
        private float  _marketValue;
        private string _amount     = "10";
        private string _access     = SiteEntry.AccessGuildOnly;
        private string _tax        = "10";
        private string _dest       = SiteEntry.DestTreasury;
        private string _mktPrice   = "1";

        public Dialog_KMHBuildSite()
        {
            doCloseX = true; absorbInputAroundWindow = true; draggable = true;
        }

        protected override void DrawContents(Rect rect)
        {
            float y = DialogLayout.DrawTitle(rect, "Build a custom site");
            DialogLayout.DrawSectionDivider(rect, ref y);

            int tile = Find.CurrentMap?.Tile ?? -1;
            const float labelW = 170f;
            DialogLayout.LabelTrunc(new Rect(0f, y + 4f, rect.width, 20f),
                tile >= 0 ? $"<color=grey>Builds at your current tile ({tile}).</color>"
                          : "<color=#ff8080>No current map tile - enter the world first.</color>");
            y += 26f;

            // Item picker (market value comes from the def).
            DialogLayout.LabelTrunc(new Rect(0f, y + 4f, labelW, 22f), "Produces");
            string itemDisplay = string.IsNullOrEmpty(_itemDef) ? "<color=grey>(pick an item)</color>" : ItemLabels.ResolveLabel(_itemDef);
            if (Widgets.ButtonText(new Rect(labelW, y, rect.width - labelW, 26f), itemDisplay))
            {
                Find.WindowStack.Add(new Dialog_KMHItemPicker(
                    title: "Pick item to produce", pickActionLabel: "Select",
                    source: ItemDefBrowser.AllPickableItems(),
                    onPick: (defName, qty) =>
                    {
                        _itemDef = defName;
                        _amount  = Mathf.Max(1, qty).ToString();
                        ThingDef td = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
                        _marketValue = td?.BaseMarketValue ?? 0f;
                    }));
            }
            y += 30f;

            if (!string.IsNullOrEmpty(_itemDef))
            {
                DialogLayout.LabelTrunc(new Rect(0f, y, rect.width, 18f),
                    $"<color=grey>Market value ~{_marketValue:0} silver/unit · skill: {SiteEntry_RelevantSkill(_itemDef)}</color>");
                y += 22f;
            }

            y = Row(rect, y, "Amount per cycle", ref _amount);

            // Access mode.
            DialogLayout.LabelTrunc(new Rect(0f, y + 4f, labelW, 22f), "Access");
            if (Widgets.ButtonText(new Rect(labelW, y, rect.width - labelW, 26f), AccessLabel(_access)))
                Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption>
                {
                    new FloatMenuOption(AccessLabel(SiteEntry.AccessGuildOnly), () => _access = SiteEntry.AccessGuildOnly),
                    new FloatMenuOption(AccessLabel(SiteEntry.AccessPublic),    () => _access = SiteEntry.AccessPublic),
                    new FloatMenuOption(AccessLabel(SiteEntry.AccessPrivate),   () => _access = SiteEntry.AccessPrivate),
                }));
            y += 30f;

            if (_access == SiteEntry.AccessPublic)
                y = Row(rect, y, "Owner tax % (0-50)", ref _tax);

            // Reward destination.
            DialogLayout.LabelTrunc(new Rect(0f, y + 4f, labelW, 22f), "Reward to");
            if (Widgets.ButtonText(new Rect(labelW, y, rect.width - labelW, 26f), DestLabel(_dest)))
                Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption>
                {
                    new FloatMenuOption(DestLabel(SiteEntry.DestTreasury),    () => _dest = SiteEntry.DestTreasury),
                    new FloatMenuOption(DestLabel(SiteEntry.DestCaravan),     () => _dest = SiteEntry.DestCaravan),
                    new FloatMenuOption(DestLabel(SiteEntry.DestMarketplace), () => _dest = SiteEntry.DestMarketplace),
                }));
            y += 30f;

            if (_dest == SiteEntry.DestMarketplace)
                y = Row(rect, y, "Marketplace price/unit", ref _mktPrice);

            const float btnW = 120f, btnH = 32f;
            float btnY = rect.height - btnH - 4f;
            if (IconButton.Draw(new Rect(0f, btnY, btnW, btnH), KMHTextures.Cancel, "Cancel")) Close();
            if (IconButton.Draw(new Rect(rect.width - btnW, btnY, btnW, btnH), KMHTextures.Post, "Build")) Submit(tile);
        }

        private void Submit(int tile)
        {
            if (tile < 0) { Notifications.KmhNotifications.Rejected("No current tile to build on"); return; }
            if (string.IsNullOrEmpty(_itemDef)) { Notifications.KmhNotifications.Rejected("Pick an item to produce"); return; }
            if (!int.TryParse((_amount ?? "").Trim(), out int amount) || amount <= 0) { Notifications.KmhNotifications.Rejected("Amount per cycle must be > 0"); return; }
            int tax = 0;
            if (_access == SiteEntry.AccessPublic && (!int.TryParse((_tax ?? "").Trim(), out tax) || tax < 0 || tax > 50)) { Notifications.KmhNotifications.Rejected("Owner tax must be 0-50"); return; }
            int mkt = 1;
            if (_dest == SiteEntry.DestMarketplace && (!int.TryParse((_mktPrice ?? "").Trim(), out mkt) || mkt < 1)) { Notifications.KmhNotifications.Rejected("Marketplace price must be >= 1"); return; }

            if (SiteHandler.TryBuild(tile, _itemDef, amount, Mathf.RoundToInt(_marketValue), _access, tax, _dest, mkt))
                Close();
        }

        private static float Row(Rect rect, float y, string label, ref string val)
        {
            const float labelW = 170f;
            DialogLayout.LabelTrunc(new Rect(0f, y + 4f, labelW, 22f), label);
            val = Widgets.TextField(new Rect(labelW, y, rect.width - labelW, 26f), val ?? "");
            return y + 30f;
        }

        private static string AccessLabel(string a)
            => a == SiteEntry.AccessPublic ? "Public (owner takes a cut)"
             : a == SiteEntry.AccessPrivate ? "Private (no workers)"
             : "Guild + allies";

        private static string DestLabel(string d)
            => d == SiteEntry.DestMarketplace ? "Marketplace (auto-list)"
             : d == SiteEntry.DestCaravan ? "Colony (delivered in-game)"
             : "Treasury";

        // Local mirror of the server's item->skill hint, for display only.
        private static string SiteEntry_RelevantSkill(string itemDefName)
        {
            string l = (itemDefName ?? "").ToLowerInvariant();
            if (l.Contains("steel") || l.Contains("plasteel") || l.Contains("blocks") || l.Contains("gold") || l.Contains("silver")) return "Mining";
            if (l.Contains("raw") || l.Contains("wood") || l.Contains("cotton") || l.Contains("smokeleaf")) return "Plants";
            if (l.Contains("meal") || l.Contains("food") || l.Contains("nutrient")) return "Cooking";
            if (l.Contains("medicine") || l.Contains("herbal")) return "Medicine";
            if (l.Contains("meat") || l.Contains("leather") || l.Contains("wool")) return "Animals";
            if (l.Contains("component") || l.Contains("spacer")) return "Intellectual";
            return "Crafting";
        }
    }
}
