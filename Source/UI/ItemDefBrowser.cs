using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace KMHPatch.UI
{
    internal static class ItemDefBrowser
    {
        // DefDatabase is stable after load, so one scan serves every picker.
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
                    if (td.destroyOnDrop)                               continue;
                    if (td.tradeability == Tradeability.None)           continue;
                    // Shared safety gate: keeps minified wrappers, corpses, pawns and non-items out of every picker.
                    if (!Items.KmhItemSafety.IsSafeDef(td, out _))      continue;
                    // No market-value filter: placeholder defs report 0, and the server validates anyway.
                    result[td.defName] = -1; // -1 = unlimited (no count display)
                }
            }
            catch
            {
                // DefDatabase can fail before game load, so return partial picker data instead of crashing.
            }

            _cache = result;
            return _cache;
        }
    }
}
