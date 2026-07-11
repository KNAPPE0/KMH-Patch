using System.Collections.Generic;
using KMHPatch.UI;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace KMHPatch.Features.Treasury
{
    // Builds the client game-state context the server uses to enforce EconomyMode / treasury access modes. The
    // server can't see the game, so we report: is there a colony map, a selected caravan, an active raid, or a hostile
    // map event. Guild-hall / treasury-site proximity are future systems (always false for now). All best-effort +
    // guarded - if anything throws, the field is simply omitted/false and the server falls back to permissive.
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
            data["ctx_near_treasury_site"] = false;   // future (Treasury Sites not implemented)
        }

        // Convenience: start a data dict from action fields and stamp context onto it.
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

        // The world tile to use as a "here" location (selected caravan, else the home colony). -1 if none. Used to
        // place a Guild Hall at the player's current spot.
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

        // Is the player near their guild's hall? TRUE when the guild has no hall (nothing to be near -> not restricted,
        // so pre-P8 guilds keep working). Otherwise: is a colony or the selected caravan within the hall's radius?
        // Client-computed because the standalone server can't do world-tile math; the server owns the hall + radius.
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

        // GameConditionDef has no "isBad" flag, so match adverse conditions by name (toxic/volcanic/heat/cold/etc.).
        // Benign events (aurora, eclipse) are intentionally NOT treated as hostile.
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
