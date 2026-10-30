using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace KMHPatch.Items
{
    // Blocks by default: an item is allowed only once full round-trip support is proven, never assumed.
    internal static class KmhItemSafety
    {
        public static bool IsMinifiedDef(ThingDef def)
            => def != null && typeof(MinifiedThing).IsAssignableFrom(def.thingClass);

        // def+count rebuilds an empty crate, so a minified building may travel only via the deep-serialized instance path.
        public static KmhItemDecision EvaluateDef(ThingDef def)
        {
            if (def == null) return KmhItemDecision.Block(KmhItemReasonCode.MissingDef, "unknown item (def not loaded)");
            if (def.category != ThingCategory.Item) return KmhItemDecision.Block(KmhItemReasonCode.NotAnItem, "not a carryable item");
            if (def.IsCorpse) return KmhItemDecision.Block(KmhItemReasonCode.Corpse, "corpses aren't supported by Colony Nexus yet (rot/pawn data can't be restored safely)");
            if (IsMinifiedDef(def))
                return KmhItemDecision.Block(KmhItemReasonCode.Minified, "a minified building can only be moved as a whole item, not by name");
            if (typeof(Pawn).IsAssignableFrom(def.thingClass))
                return KmhItemDecision.Block(KmhItemReasonCode.Pawn, "pawns/animals go through dedicated transfer, not item storage");
            if (string.IsNullOrEmpty(def.label) && string.IsNullOrEmpty(def.defName))
                return KmhItemDecision.Block(KmhItemReasonCode.NoLabel, "item has no label");
            return KmhItemDecision.Allow(iconFallback: IconMissing(def));
        }

        // Capture refuses non-items, so a pawn, building or corpse delivery came from a modified client.
        internal static bool IsDeliverable(ThingDef def)
            => def == null || (def.category == ThingCategory.Item && !def.IsCorpse && !typeof(Pawn).IsAssignableFrom(def.thingClass));

        // Everything EvaluateDef checks, plus the instance state a defName cannot show.
        public static KmhItemDecision Evaluate(Thing thing, KmhItemContext context)
        {
            if (thing == null) return KmhItemDecision.Block(KmhItemReasonCode.NullOrDestroyed, "item no longer exists");
            if (thing.Destroyed) return KmhItemDecision.Block(KmhItemReasonCode.NullOrDestroyed, "item was already destroyed");
            if (thing.def == null || DefDatabase<ThingDef>.GetNamedSilentFail(thing.def.defName) == null)
                return KmhItemDecision.Block(KmhItemReasonCode.MissingDef, "item has a missing/unloaded def");
            if (thing is Corpse) return KmhItemDecision.Block(KmhItemReasonCode.Corpse, "corpses aren't supported yet (rot/pawn data can't be restored safely)");
            if (thing is Pawn) return KmhItemDecision.Block(KmhItemReasonCode.Pawn, "pawns/animals go through dedicated transfer, not item storage");

            // Rides the deep-scribe blob; capture refuses without it, as the metadata fallback would return an empty crate.
            if (thing is MinifiedThing)
            {
                if (((MinifiedThing)thing).InnerThing == null)
                    return KmhItemDecision.Block(KmhItemReasonCode.Minified, "this minified object has nothing inside it");
                return KmhItemDecision.Allow(iconFallback: IconMissing(thing.def));
            }

            KmhItemDecision defDecision = EvaluateDef(thing.def);
            if (!defDecision.Allowed) return defDecision;
            if (string.IsNullOrEmpty(SafeLabel(thing))) return KmhItemDecision.Block(KmhItemReasonCode.NoLabel, "item has no readable label");
            if (thing.stackCount < 1) return KmhItemDecision.Block(KmhItemReasonCode.BadStack, "invalid stack count");
            return KmhItemDecision.Allow(iconFallback: IconMissing(thing.def));
        }


        public static bool IsSafeDef(ThingDef def, out string reason)
        { KmhItemDecision d = EvaluateDef(def); reason = d.Reason; return d.Allowed; }

        public static bool CanKmhHandleThing(Thing thing, KmhItemContext context, out string reason)
        { KmhItemDecision d = Evaluate(thing, context); reason = d.Reason; return d.Allowed; }

        public static bool CanKmhStoreThing(Thing t, out string r) => CanKmhHandleThing(t, KmhItemContext.Store, out r);
        public static bool CanKmhTradeThing(Thing t, out string r) => CanKmhHandleThing(t, KmhItemContext.Trade, out r);

        // A missing icon is a visual failure, not a reason to lose an item, so fall back to the category icon.
        public static Texture2D SafeIcon(ThingDef def)
        {
            try
            {
                if (def == null) return BaseContent.BadTex;
                Texture2D icon = def.uiIcon;
                if (icon != null && icon != BaseContent.BadTex) return icon;
                ThingCategoryDef cat = def.FirstThingCategory;
                if (cat?.icon != null && cat.icon != BaseContent.BadTex) return (Texture2D)cat.icon;
            }
            catch { }
            return BaseContent.BadTex;
        }

        public static bool IconMissing(ThingDef def)
        {
            try { return def == null || def.uiIcon == null || def.uiIcon == BaseContent.BadTex; }
            catch { return true; }
        }

        private static string SafeLabel(Thing t)
        {
            try { string l = t.LabelCapNoCount; return string.IsNullOrEmpty(l) ? (t.def?.label ?? "") : l; }
            catch { return t.def?.label ?? ""; }
        }
    }
}
