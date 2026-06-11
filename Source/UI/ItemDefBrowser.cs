using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace KMHPatch.UI
{
    // Source for "pick from all known item defs" pickers (Quest Post target, future Bounty composer, etc.). Returns
    // a Dictionary<defName, int> shaped for Dialog_KMHItemPicker, with value = -1 to signal "unlimited"
    // - picker hides the count and the qty prompt skips the Available line.
    //
    // Filter: only ThingDef with category == Item that are tradeable and have a non-zero market value. Drops
    // chunks, raw filth, structures, pawns, plants, terrain, etc. - the things a player would actually want to
    // specify as a quest delivery target
    internal static class ItemDefBrowser
    {
        // Cache the result - DefDatabase doesn't change after load, and we want to avoid rescanning ~1000 defs on
        // every picker open
        private static Dictionary<string, int> _cache;

        public static Dictionary<string, int> AllPickableItems()
        {
            if (_cache != null) return _cache;

            Dictionary<string, int> result = new Dictionary<string, int>(
                StringComparer.OrdinalIgnoreCase);

            try
            {
                foreach (ThingDef td in DefDatabase<ThingDef>.AllDefsListForReading)
                {
                    if (td == null || string.IsNullOrEmpty(td.defName)) continue;
                    if (td.category != ThingCategory.Item)              continue;
                    if (td.destroyOnDrop)                               continue;
                    if (td.IsCorpse)                                    continue;
                    // Skip non-tradeable / debug-only items.
                    if (td.tradeability == Tradeability.None)           continue;
                    // BaseMarketValue can be 0 for placeholder defs; allow 0 - server may still validate
                    result[td.defName] = -1; // -1 = unlimited (no count display)
                }
            }
            catch
            {
                // DefDatabase access can fail pre-game-load. Return whatever we built so the picker shows partial
                // data rather than crashing
            }

            _cache = result;
            return _cache;
        }
    }
}
