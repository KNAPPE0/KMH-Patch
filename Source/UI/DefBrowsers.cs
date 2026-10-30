using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace KMHPatch.UI
{
    // Cached because DefDatabase is fixed after load.
    internal static class DefBrowsers
    {
        private static List<Dialog_KMHDefPicker.Entry> _animals;
        private static List<Dialog_KMHDefPicker.Entry> _pawnKinds;
        private static List<Dialog_KMHDefPicker.Entry> _buildings;

        // Race defName, because hunt auto-verify tallies a kill by the dead pawn's race.
        public static List<Dialog_KMHDefPicker.Entry> Animals() => Cached(ref _animals, list =>
        {
            foreach (ThingDef td in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (td?.race == null || !td.race.Animal || string.IsNullOrEmpty(td.defName)) continue;
                list.Add(new Dialog_KMHDefPicker.Entry { DefName = td.defName, Label = td.label ?? td.defName, Icon = td });
            }
        });

        // Kind defName, the other key hunt auto-verify tallies by.
        public static List<Dialog_KMHDefPicker.Entry> PawnKinds() => Cached(ref _pawnKinds, list =>
        {
            foreach (PawnKindDef pk in DefDatabase<PawnKindDef>.AllDefsListForReading)
            {
                if (pk == null || string.IsNullOrEmpty(pk.defName)) continue;
                list.Add(new Dialog_KMHDefPicker.Entry { DefName = pk.defName, Label = pk.label ?? pk.defName, Icon = pk.race });
            }
        });

        // Building defName, which build auto-verify counts on the player's maps.
        public static List<Dialog_KMHDefPicker.Entry> Buildings() => Cached(ref _buildings, list =>
        {
            foreach (ThingDef td in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (td == null || string.IsNullOrEmpty(td.defName))        continue;
                if (td.category != ThingCategory.Building)                 continue;
                if (td.designationCategory == null && !td.BuildableByPlayer) continue;
                list.Add(new Dialog_KMHDefPicker.Entry { DefName = td.defName, Label = td.label ?? td.defName, Icon = td });
            }
        });

        private static List<Dialog_KMHDefPicker.Entry> Cached(ref List<Dialog_KMHDefPicker.Entry> cache,
                                                              Action<List<Dialog_KMHDefPicker.Entry>> fill)
        {
            if (cache != null) return cache;
            List<Dialog_KMHDefPicker.Entry> list = new List<Dialog_KMHDefPicker.Entry>();
            try { fill(list); }
            catch { /* pre-load access can fail; return partial */ }
            cache = list;
            return cache;
        }
    }
}
