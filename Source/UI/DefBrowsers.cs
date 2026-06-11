using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace KMHPatch.UI
{
    // Sources for the def pickers used by the Quest composer (hunt target, build structure). Cached - DefDatabase
    // is fixed after load. Each entry carries the defName the server/auto-verify match on plus a friendly label +
    // icon for display, so players never type raw defNames
    internal static class DefBrowsers
    {
        private static List<Dialog_KMHDefPicker.Entry> _animals;
        private static List<Dialog_KMHDefPicker.Entry> _pawnKinds;
        private static List<Dialog_KMHDefPicker.Entry> _buildings;

        // Animal races (defName = race ThingDef). Hunt auto-verify tallies a kill by the dead pawn's race defName,
        // so this lines up
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

        // Pawn kinds (defName = PawnKindDef). Hunt auto-verify also tallies by the dead pawn's kindDef defName
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

        // Player-buildable structures (defName = building ThingDef). Build auto-verify counts spawned things of
        // this def on the player's maps
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
