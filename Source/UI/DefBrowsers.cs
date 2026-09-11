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
        public static List<Dialog_KMHDefPicker.Entry> Animals()
        {
            if (_animals != null) return _animals;
            List<Dialog_KMHDefPicker.Entry> list = new List<Dialog_KMHDefPicker.Entry>();
            try
            {
                foreach (ThingDef td in DefDatabase<ThingDef>.AllDefsListForReading)
                {
                    if (td?.race == null || !td.race.Animal || string.IsNullOrEmpty(td.defName)) continue;
                    list.Add(new Dialog_KMHDefPicker.Entry { DefName = td.defName, Label = td.label ?? td.defName, Icon = td });
                }
            }
            catch { /* pre-load access can fail; return partial */ }
            _animals = list;
            return _animals;
        }

        // Kind defName, the other key hunt auto-verify tallies by.
        public static List<Dialog_KMHDefPicker.Entry> PawnKinds()
        {
            if (_pawnKinds != null) return _pawnKinds;
            List<Dialog_KMHDefPicker.Entry> list = new List<Dialog_KMHDefPicker.Entry>();
            try
            {
                foreach (PawnKindDef pk in DefDatabase<PawnKindDef>.AllDefsListForReading)
                {
                    if (pk == null || string.IsNullOrEmpty(pk.defName)) continue;
                    list.Add(new Dialog_KMHDefPicker.Entry { DefName = pk.defName, Label = pk.label ?? pk.defName, Icon = pk.race });
                }
            }
            catch { }
            _pawnKinds = list;
            return _pawnKinds;
        }

        // Building defName, which build auto-verify counts on the player's maps.
        public static List<Dialog_KMHDefPicker.Entry> Buildings()
        {
            if (_buildings != null) return _buildings;
            List<Dialog_KMHDefPicker.Entry> list = new List<Dialog_KMHDefPicker.Entry>();
            try
            {
                foreach (ThingDef td in DefDatabase<ThingDef>.AllDefsListForReading)
                {
                    if (td == null || string.IsNullOrEmpty(td.defName))        continue;
                    if (td.category != ThingCategory.Building)                 continue;
                    if (td.designationCategory == null && !td.BuildableByPlayer) continue;
                    list.Add(new Dialog_KMHDefPicker.Entry { DefName = td.defName, Label = td.label ?? td.defName, Icon = td });
                }
            }
            catch { }
            _buildings = list;
            return _buildings;
        }
    }
}
