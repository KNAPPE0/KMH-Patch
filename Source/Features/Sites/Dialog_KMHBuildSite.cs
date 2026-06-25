using System.Collections.Generic;
using KMHPatch.Features.Sites.Dto;
using KMHPatch.UI;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace KMHPatch.Features.Sites
{
    // Build a custom site at a player-chosen world tile. Picks an item to produce (its market value drives cost +
    // cycle time), an amount per cycle, access mode, owner tax, and reward destination. The tile is chosen on the
    // world map (Select on map) or from a selected caravan - never the home base. The server re-validates everything
    // (tile free of other KMH sites, cost affordable) and replies with a chat reason on rejection
    public class Dialog_KMHBuildSite : Window_KMHBase
    {
        public override Vector2 InitialSize => new Vector2(600f, 600f);

        private string _itemDef    = "";
        private float  _marketValue;
        private string _amount     = "10";
        private string _access     = SiteEntry.AccessGuildOnly;
        private string _tax        = "10";
        private string _dest       = SiteEntry.DestTreasury;
        private string _mktPrice   = "1";
        private int    _tile       = -1;   // chosen world tile; -1 = none picked yet (we never default to home)

        public Dialog_KMHBuildSite()
        {
            doCloseX = true; absorbInputAroundWindow = true; draggable = true;
            // Seed from a selected caravan so "visit the spot, then build" works out of the box. Otherwise the player
            // picks a tile on the world map. We deliberately do NOT default to the current/home tile - that was the
            // old bug that dropped every site on the colony.
            Caravan car = CaravanReader.GetSelectedCaravan();
            if (car != null) _tile = car.Tile.tileId;
        }

        protected override void DrawContents(Rect rect)
        {
            float y = DialogLayout.DrawTitle(rect, "Build a custom site");
            DialogLayout.DrawSectionDivider(rect, ref y);

            const float labelW = 170f;

            // Location row: the player picks an EMPTY world tile. "Select on map" opens the world targeter (no
            // dev-mode tile id needed - they click the spot); "Use caravan" drops it on a selected caravan's tile.
            DialogLayout.LabelTrunc(new Rect(0f, y + 4f, labelW, 22f), "Location");
            string locText = _tile >= 0
                ? (TileOk(_tile, out _) ? $"<color=grey>{TileLabel(_tile)}</color>"
                                        : $"<color=#ff8080>{TileLabel(_tile)} - not buildable here</color>")
                : "<color=#ff8080>(no tile chosen - use Select on map)</color>";
            DialogLayout.LabelTrunc(new Rect(labelW, y + 4f, rect.width - labelW, 22f), locText);
            y += 28f;

            if (Widgets.ButtonText(new Rect(labelW, y, 150f, 26f), "Select on map…"))
                OpenTileSelector();
            Caravan car = CaravanReader.GetSelectedCaravan();
            if (car != null && Widgets.ButtonText(new Rect(labelW + 158f, y, rect.width - labelW - 158f, 26f),
                    $"Use caravan ({car.LabelCap})"))
                _tile = car.Tile.tileId;
            y += 34f;

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
            if (IconButton.Draw(new Rect(rect.width - btnW, btnY, btnW, btnH), KMHTextures.Post, "Build")) Submit();
        }

        private void Submit()
        {
            if (!TileOk(_tile, out string tileReason))
            { Notifications.KmhNotifications.Rejected(_tile < 0 ? "Pick a tile first (Select on map)" : $"Can't build there - {tileReason}"); return; }
            if (string.IsNullOrEmpty(_itemDef)) { Notifications.KmhNotifications.Rejected("Pick an item to produce"); return; }
            if (!int.TryParse((_amount ?? "").Trim(), out int amount) || amount <= 0) { Notifications.KmhNotifications.Rejected("Amount per cycle must be > 0"); return; }
            int tax = 0;
            if (_access == SiteEntry.AccessPublic && (!int.TryParse((_tax ?? "").Trim(), out tax) || tax < 0 || tax > 50)) { Notifications.KmhNotifications.Rejected("Owner tax must be 0-50"); return; }
            int mkt = 1;
            if (_dest == SiteEntry.DestMarketplace && (!int.TryParse((_mktPrice ?? "").Trim(), out mkt) || mkt < 1)) { Notifications.KmhNotifications.Rejected("Marketplace price must be >= 1"); return; }

            if (SiteHandler.TryBuild(_tile, _itemDef, amount, Mathf.RoundToInt(_marketValue), _access, tax, _dest, mkt))
                Close();
        }

        // World-map tile picker: close this dialog, jump to the world, let the player click an empty tile, then
        // reopen with the chosen tile and all entered fields intact.
        private void OpenTileSelector()
        {
            // Close this dialog AND the parent Sites window first - both absorb input and would block world clicks
            // while targeting. The build dialog reopens with the chosen tile when the player clicks a valid spot.
            Find.WindowStack.TryRemove(typeof(Dialog_KMHSites), false);
            Close(false);
            CameraJumper.TryShowWorld();
            Find.WorldTargeter.BeginTargeting(OnTilePicked, true);
        }

        private bool OnTilePicked(GlobalTargetInfo target)
        {
            int tile = target.Tile.tileId;
            if (!TileOk(tile, out string reason))
            {
                Messages.Message($"KMH: can't build a site here - {reason}.", MessageTypeDefOf.RejectInput, false);
                return false; // invalid - keep targeting
            }
            _tile = tile;
            Find.WindowStack.Add(this); // reopen the build dialog (same instance keeps every field)
            return true;                // done targeting
        }

        // A tile is buildable if it's passable land with no base/site on it. A caravan sitting on the tile does NOT
        // block (so "visit then build" works) - only MapParents (settlements, sites) and our own site markers do.
        private static bool TileOk(int tile, out string reason)
        {
            reason = "";
            if (tile < 0) { reason = "no tile selected"; return false; }
            try
            {
                if (Find.World.Impassable(tile)) { reason = "impassable terrain (ocean / mountain)"; return false; }
                foreach (WorldObject wo in Find.WorldObjects.ObjectsAt(tile))
                    if (wo is MapParent || wo is KMHSiteWorldObject) { reason = "the tile already has a base or site"; return false; }
            }
            catch (System.Exception ex) { reason = ex.Message; return false; }
            return true;
        }

        private static string TileLabel(int tile) => tile < 0 ? "(none)" : $"tile {tile}";

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
