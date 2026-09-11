using System.Collections.Generic;
using KMHPatch.Features.Sites.Dto;
using KMHPatch.UI;
using RimWorld;
using UnityEngine;
using Verse;

namespace KMHPatch.Features.Sites
{
    // Buildings on one site; slots are scarce, so placed buildings and slot cost lead the layout.
    public class Dialog_KMHSiteBuildings : Window_KMHBase
    {
        public override Vector2 InitialSize => KMHPatch.UI.DialogLayout.FitToScreen(620f, 500f);

        private readonly int _tile;
        private Vector2 _scroll;

        public Dialog_KMHSiteBuildings(int tile)
        {
            _tile = tile;
            doCloseX = true; absorbInputAroundWindow = true; draggable = true;
            EnableAutoRefresh(() => SiteHandler.RequestSnapshot());
        }

        private SiteEntry Site()
        {
            List<SiteEntry> sites = SiteCache.Snapshot?.Sites;
            if (sites == null) return null;
            foreach (SiteEntry s in sites) if (s.Tile == _tile) return s;
            return null;
        }

        protected override void DrawContents(Rect rect)
        {
            float y = DialogLayout.DrawTitle(rect, "Site buildings");
            SiteEntry site = Site();
            if (site == null)
            {
                DialogLayout.LabelTrunc(new Rect(0f, y, rect.width, 24f), "That site is no longer visible to you.");
                return;
            }

            int slots = SlotsForTier(site.OutputTier);
            int used  = site.Buildings?.Count ?? 0;
            DialogLayout.LabelTrunc(new Rect(0f, y, rect.width, 22f),
                $"<color=grey>Tile {site.Tile} · Tier {site.OutputTier} · {used}/{slots} slot(s) used</color>");
            y += 26f;

            if (site.Stability < 100)
            {
                int repair = Mathf.Max(0, 100 - site.Stability) * Econ.RepairCostPerPoint;
                DialogLayout.LabelTrunc(new Rect(0f, y, rect.width - 130f, 22f),
                    $"<color=#ffcf59>Condition: {site.Stability}% - output is reduced.</color> "
                    + $"<color=grey>Repair costs {repair} silver ({Econ.RepairCostPerPoint}/point).</color>");
                if (Widgets.ButtonText(new Rect(rect.width - 126f, y - 2f, 126f, 24f), $"Repair {repair}s"))
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                        $"Repair this site from {site.Stability}% to full condition for {repair} silver?",
                        () => SiteHandler.RepairSite(_tile)));
                y += 26f;
            }

            int cap  = StorageCapacity(site);
            int held = StoredUnits(site);
            // Storage only fills when the destination is Storage; nothing else on screen would say a warehouse is inert.
            bool storesHere = site.OwnerRewardDestination == SiteEntry.DestStorage;
            string storageNote = cap == 0
                ? "  <color=grey>- none built, output is delivered every cycle</color>"
                : (storesHere ? "" : "  <color=#ffcf59>- unused: this site's reward destination is not Storage</color>");
            DialogLayout.LabelTrunc(new Rect(0f, y, rect.width - 130f, 22f),
                $"<color=#79b8ff>Storage:</color> {held}/{cap} held{storageNote}");
            if (held > 0 && Widgets.ButtonText(new Rect(rect.width - 126f, y - 2f, 126f, 24f), "Collect all"))
                SiteHandler.CollectStorage(_tile);
            y += 22f;

            int housing = CountOf(site, SiteBuilding.KindHousing) * Econ.WorkersPerHousing;
            if (housing > Econ.MaxHousingBonus) housing = Econ.MaxHousingBonus;
            DialogLayout.LabelTrunc(new Rect(0f, y, rect.width, 22f),
                $"<color=#79b8ff>Workers:</color> {site.Workers?.Count ?? 0}/{site.MaxWorkers} working"
                + (housing > 0 ? $"  <color=grey>(+{housing} of that from housing)</color>" : ""));
            y += 22f;

            int outBonus = HasProduction(site) ? Mathf.Min(Econ.MaxProductionPct, Econ.ProductionBonusPct) : 0;
            DialogLayout.LabelTrunc(new Rect(0f, y, rect.width, 22f),
                $"<color=#79b8ff>Output:</color> "
                + (outBonus > 0 ? $"+{outBonus}% from the production building" : "<color=grey>no production building built</color>"));
            y += 24f;
            DialogLayout.DrawSectionDivider(rect, ref y);

            if (used == 0)
                DialogLayout.LabelTrunc(new Rect(0f, y, rect.width, 22f), "<color=grey>Nothing built here yet.</color>");

            const float rowH = 34f;
            Rect view = new Rect(0f, y, rect.width, Mathf.Max(rowH, rect.height - y - 96f));
            Rect content = new Rect(0f, 0f, view.width - 16f, Mathf.Max(1, used) * rowH);
            Widgets.BeginScrollView(view, ref _scroll, content);
            for (int i = 0; i < used; i++)
            {
                SiteBuilding b = site.Buildings[i];
                Rect r = new Rect(0f, i * rowH, content.width, rowH - 4f);
                // Nothing upgrades a building yet, so a printed "level 1" reads as a missing feature.
                int lvl = Mathf.Max(1, b?.Level ?? 1);
                string lvlTag = lvl > 1 ? $" · level {lvl}" : "";
                DialogLayout.LabelTrunc(new Rect(r.x, r.y + 4f, r.width - 110f, 22f),
                    $"{KindLabel(b?.Kind)} <color=grey>· {EffectOf(b?.Kind)}{lvlTag} · {StateLabel(b?.State)}</color>");
                int idx = i;
                string bKind = b?.Kind, bState = b?.State;
                if (Widgets.ButtonText(new Rect(r.xMax - 104f, r.y, 104f, 24f), "Demolish"))
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                        $"Demolish this {KindLabel(b?.Kind).ToLowerInvariant()}? The {Econ.BuildingCostSilver} silver it cost is not refunded.",
                        () => SiteHandler.RemoveBuilding(_tile, idx, bKind, bState)));
            }
            Widgets.EndScrollView();

            // Split in two: as one sentence it overran the window and truncated away the no-refund warning.
            float by = rect.height - 88f;
            DialogLayout.LabelTrunc(new Rect(0f, by, rect.width, 20f), SlotHint(slots, used));
            by += 20f;
            DialogLayout.LabelTrunc(new Rect(0f, by, rect.width, 20f),
                $"<color=grey>Each costs {Econ.BuildingCostSilver} silver from your treasury and takes one slot. Demolishing refunds nothing.</color>");
            by += 24f;

            float gap = 6f;
            float bw  = Mathf.Min(196f, (rect.width - gap * 2f) / 3f);
            float x   = 0f;
            foreach (string kind in new[] { SiteBuilding.KindProduction, SiteBuilding.KindHousing, SiteBuilding.KindStorage })
            {
                bool room = used < slots && !(kind == SiteBuilding.KindProduction && HasProduction(site));
                Rect br = new Rect(x, by, bw, 30f);
                if (Widgets.ButtonText(br, $"{KindLabel(kind)}  {Econ.BuildingCostSilver}s", active: room))
                    SiteHandler.AddBuilding(_tile, kind);
                TooltipHandler.TipRegion(br, TipFor(kind, room, used >= slots));
                x += bw + gap;
            }
        }

        private static string SlotHint(int slots, int used)
            => used >= slots
                ? "<color=#ffce4d>Every building slot on this site is used. A higher site tier grants more.</color>"
                : $"<color=grey>{slots - used} free slot(s). A tier 1 site has 2, tier 2 has 3, tier 3 has 4.</color>";

        // What the building does, in the units the server actually applies.
        private static string EffectOf(string kind)
        {
            switch (kind)
            {
                case SiteBuilding.KindHousing: return $"+{Econ.WorkersPerHousing} worker cap";
                case SiteBuilding.KindStorage: return $"+{Econ.StoragePerBuilding} storage";
                case SiteBuilding.KindProduction: return $"+{Econ.ProductionBonusPct}% output";
                default: return "no effect yet";
            }
        }

        private static string TipFor(string kind, bool room, bool full)
        {
            string body;
            switch (kind)
            {
                case SiteBuilding.KindHousing:
                    body = $"Raises how many colonists may work this site by {Econ.WorkersPerHousing}, "
                         + $"up to +{Econ.MaxHousingBonus} from housing in total. More workers means more output per cycle.";
                    break;
                case SiteBuilding.KindStorage:
                    body = $"Lets the site hold {Econ.StoragePerBuilding} more items instead of delivering every cycle, "
                         + $"up to {Econ.MaxStorageUnits} total. Set the reward destination to Storage to use it, then collect here.";
                    break;
                default:
                    body = $"Raises this site's output by {Econ.ProductionBonusPct}%, to a maximum of {Econ.MaxProductionPct}%. "
                         + "One production building per site.";
                    break;
            }
            if (full) return body + "\n\nNo free slots on this site.";
            if (!room) return body + "\n\nThis site already has a production building.";
            return body + $"\n\nCost: {Econ.BuildingCostSilver} silver.";
        }

        // The server's own numbers, sent with the snapshot. Defaults only apply before the first snapshot lands.
        private static SiteSnapshot Econ => SiteCache.Snapshot ?? new SiteSnapshot();

        private static int CountOf(SiteEntry s, string kind)
        {
            int n = 0;
            if (s?.Buildings == null) return 0;
            foreach (SiteBuilding b in s.Buildings)
                if (b != null && b.Kind == kind && b.State == SiteBuilding.StateOperational) n++;
            return n;
        }

        private static bool HasProduction(SiteEntry s)
        {
            if (s?.Buildings == null) return false;
            foreach (SiteBuilding b in s.Buildings)
                if (b != null && b.Kind == SiteBuilding.KindProduction) return true;
            return false;
        }

        // Mirrors the server's SiteBuildings.SlotsForTier; the server refuses anyway if this ever drifts.
        private static int SlotsForTier(int tier) => tier <= 1 ? 2 : (tier == 2 ? 3 : 4);

        private static int StorageCapacity(SiteEntry s)
            => Mathf.Min(Econ.MaxStorageUnits, CountOf(s, SiteBuilding.KindStorage) * Econ.StoragePerBuilding);

        private static int StoredUnits(SiteEntry s)
        {
            int n = 0;
            if (s?.StoredItems == null) return 0;
            foreach (KeyValuePair<string, int> kv in s.StoredItems) if (kv.Value > 0) n += kv.Value;
            return n;
        }

        private static string KindLabel(string kind)
        {
            switch (kind)
            {
                case SiteBuilding.KindHousing:   return "Housing";
                case SiteBuilding.KindStorage:   return "Storage";
                case SiteBuilding.KindLogistics: return "Logistics";
                case SiteBuilding.KindDefense:   return "Defense";
                default:                         return "Production";
            }
        }

        private static string StateLabel(string state)
        {
            switch (state)
            {
                case SiteBuilding.StateDamaged: return "<color=#ffcf59>damaged</color>";
                case SiteBuilding.StateRuined:  return "<color=#ff7676>ruined</color>";
                default:                        return "operational";
            }
        }
    }
}
