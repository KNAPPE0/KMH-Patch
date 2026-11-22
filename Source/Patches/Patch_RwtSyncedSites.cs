using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using Verse;

namespace KMHPatch.Patches
{
    // RWT shows other players' quest sites on this world as copies; vanilla rules that count every site treat them as the player's own.
    internal static class RwtSyncedSites
    {
        internal static bool InRwtSession()
        {
            try { return SessionHandler.CurrentNetworkState == ClientNetworkState.Connected; }
            catch { return false; }
        }

        // The player's own quest tags its site; RWT builds its copies with SiteMaker and no quest tags.
        internal static bool IsOwn(Site site) => site != null && !site.questTags.NullOrEmpty();

        internal static bool IsAncientComplex(WorldObject wo)
            => wo is Site site && site.parts != null && site.parts.Any(p => p.def == SitePartDefOf.AncientComplex);

        internal static bool IsWorkSite(Site site) => site?.MainSitePartDef?.tags != null && site.MainSitePartDef.tags.Contains("WorkSite");

        // Vanilla's check, counting only the player's own complexes.
        internal static AcceptanceReport AncientComplexAllowed(IEnumerable<WorldObject> objects, IEnumerable<Quest> quests, Func<bool> canRun)
        {
            if (objects.Any(o => IsAncientComplex(o) && IsOwn((Site)o)))
                return "RitualAttachedOutcomeCantApply_AncientComplexIsPresent".Translate();
            if (quests.Any(q => q.root == QuestScriptDefOf.AncientComplex_Mission && !q.Historical))
                return "RitualAttachedOutcomeCantApply_AncientComplexIsPresent".Translate();
            if (!canRun()) return "RitualAttachedOutcomeCantApply_NotAvailableRightNow".Translate();
            return true;
        }

        private static SimpleCurve _campCurve;
        private static bool _campCurveRead;

        internal static float WorkSiteScale(int all, int own)
        {
            if (!_campCurveRead)
            {
                _campCurveRead = true;
                _campCurve = AccessTools.Field(typeof(QuestNode_Root_WorkSite), "ExistingCampsAppearanceFrequencyMultiplier")?.GetValue(null) as SimpleCurve;
            }
            if (_campCurve == null || own == all) return 1f;
            float a = _campCurve.Evaluate(all);
            return a > 0f ? _campCurve.Evaluate(own) / a : 1f;
        }
    }

    [HarmonyPatch(typeof(RitualAttachableOutcomeEffectWorker_DiscoverAncientComplex), nameof(RitualAttachableOutcomeEffectWorker_DiscoverAncientComplex.CanApplyNow))]
    internal static class Patch_DiscoverAncientComplex_OwnSites
    {
        // Vanilla allows one complex in the whole world, so another player's complex blocked every other player's ritual.
        [HarmonyPrefix]
        private static bool Prefix(Map map, ref AcceptanceReport __result)
        {
            if (!UsesOwnRule(RwtSyncedSites.InRwtSession(), Find.WorldObjects.AllWorldObjects)) return true;
            __result = RwtSyncedSites.AncientComplexAllowed(Find.WorldObjects.AllWorldObjects, Find.QuestManager.QuestsListForReading,
                () => QuestScriptDefOf.OpportunitySite_AncientComplex.CanRun(StorytellerUtility.DefaultThreatPointsNow(map), map));
            return false;
        }

        internal static bool UsesOwnRule(bool inRwtSession, IEnumerable<WorldObject> objects)
            => inRwtSession && objects.Any(o => RwtSyncedSites.IsAncientComplex(o) && !RwtSyncedSites.IsOwn((Site)o));
    }

    [HarmonyPatch(typeof(QuestNode_Root_WorkSite), nameof(QuestNode_Root_WorkSite.AppearanceFrequency))]
    internal static class Patch_WorkSiteFrequency_OwnSites
    {
        // Vanilla makes work-site quests rarer as work sites pile up in the world; other players' copies counted too.
        [HarmonyPostfix]
        private static void Postfix(ref float __result)
        {
            if (__result <= 0f || !RwtSyncedSites.InRwtSession()) return;
            int all = 0, own = 0;
            foreach (Site site in Find.WorldObjects.Sites)
            {
                if (!RwtSyncedSites.IsWorkSite(site)) continue;
                all++;
                if (RwtSyncedSites.IsOwn(site)) own++;
            }
            __result *= RwtSyncedSites.WorkSiteScale(all, own);
        }
    }
}
