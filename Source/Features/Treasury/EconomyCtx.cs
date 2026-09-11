using System.Collections.Generic;
using KMHPatch.UI;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace KMHPatch.Features.Treasury
{
    // Game state the standalone server can't see; every probe fails open rather than locking the player out.
    internal static class EconomyCtx
    {
        // Adds the ctx_* fields to an envelope's data dict.
        public static void AddContext(Dictionary<string, object> data)
        {
            if (data == null) return;
            data["ctx_known"]              = true;
            data["ctx_has_colony"]         = HasColony();
            data["ctx_has_caravan"]        = CaravanReader.GetSelectedCaravan() != null;
            data["ctx_in_raid"]            = InRaid();
            data["ctx_hostile_event"]      = HostileEvent();
            data["ctx_near_guild_hall"]    = NearGuildHall();
            data["ctx_caravan_near_hall"]  = CaravanNearGuildHall();
            // No site grants treasury access yet, but TreasurySiteRequired must see the field, not infer from absence.
            data["ctx_near_treasury_site"] = false;
        }

        // Starts a data dict from the action fields and stamps context onto it.
        public static Dictionary<string, object> With(Dictionary<string, object> fields)
        {
            Dictionary<string, object> d = fields ?? new Dictionary<string, object>();
            AddContext(d);
            return d;
        }

        private static bool HasColony()
        {
            try { return Find.AnyPlayerHomeMap != null; } catch { return false; }
        }

        // "Here" for hall placement: selected caravan, else the home colony, else -1.
        public static int CurrentTile()
        {
            try
            {
                Caravan car = CaravanReader.GetSelectedCaravan();
                if (car != null) return car.Tile.tileId;
                Map m = Find.AnyPlayerHomeMap;
                return m != null ? m.Tile.tileId : -1;
            }
            catch { return -1; }
        }

        // TRUE when the guild has no hall - nothing to be near isn't a restriction, so pre-P8 guilds keep working.
        private static bool NearGuildHall()
        {
            try
            {
                Features.Guilds.Dto.GuildHallDto hall = Features.Guilds.GuildCache.Guild?.Hall;
                if (hall == null || !hall.HasHall || hall.Tile < 0) return true;   // no hall -> unrestricted
                RimWorld.Planet.WorldGrid grid = Find.WorldGrid;
                if (grid == null) return true;
                int radius = System.Math.Max(0, hall.RadiusTiles);

                RimWorld.Planet.Caravan car = CaravanReader.GetSelectedCaravan();
                if (car != null && Within(grid, car.Tile.tileId, hall.Tile, radius)) return true;

                System.Collections.Generic.List<Map> maps = Find.Maps;
                if (maps != null)
                    foreach (Map m in maps)
                        if (m != null && m.IsPlayerHome && Within(grid, m.Tile.tileId, hall.Tile, radius)) return true;
                return false;
            }
            catch { return true; }   // fail open (don't lock the player out on an API hiccup)
        }

        // Caravan only - NearGuildHall also counts a colony, which satisfied this with the caravan a world away.
        private static bool CaravanNearGuildHall()
        {
            try
            {
                Features.Guilds.Dto.GuildHallDto hall = Features.Guilds.GuildCache.Guild?.Hall;
                if (hall == null || !hall.HasHall || hall.Tile < 0) return true;   // no hall -> unrestricted
                RimWorld.Planet.WorldGrid grid = Find.WorldGrid;
                if (grid == null) return true;
                RimWorld.Planet.Caravan car = CaravanReader.GetSelectedCaravan();
                return car != null && Within(grid, car.Tile.tileId, hall.Tile, System.Math.Max(0, hall.RadiusTiles));
            }
            catch { return true; }
        }

        private static bool Within(RimWorld.Planet.WorldGrid grid, int a, int b, int radius)
        {
            if (a < 0 || b < 0) return false;
            if (a == b) return true;
            try { return grid.ApproxDistanceInTiles(a, b) <= radius; } catch { return false; }
        }

        private static bool InRaid()
        {
            try
            {
                List<Map> maps = Find.Maps;
                if (maps == null) return false;
                foreach (Map m in maps)
                    if (m != null && m.IsPlayerHome && GenHostility.AnyHostileActiveThreatToPlayer(m)) return true;
            }
            catch { }
            return false;
        }

        // GameConditionDef has no "isBad" flag, so match by name; benign events (aurora, eclipse) stay off this list.
        private static readonly string[] AdverseConditionMarkers =
        {
            "toxic", "volcanic", "flashstorm", "heatwave", "coldsnap", "noxious", "deadlife", "smokecloud",
            "psychicdrone", "psychicsuppression", "darkness",
        };

        private static bool HostileEvent()
        {
            try
            {
                List<Map> maps = Find.Maps;
                if (maps == null) return false;
                foreach (Map m in maps)
                {
                    if (m == null || !m.IsPlayerHome || m.gameConditionManager == null) continue;
                    foreach (GameCondition c in m.gameConditionManager.ActiveConditions)
                    {
                        string d = c?.def?.defName?.ToLowerInvariant();
                        if (string.IsNullOrEmpty(d)) continue;
                        foreach (string k in AdverseConditionMarkers) if (d.Contains(k)) return true;
                    }
                }
            }
            catch { }
            return false;
        }
    }
}
