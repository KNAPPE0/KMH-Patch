using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld.Planet;
using Verse;

namespace KMHPatch.Features.Sites
{
    // KMH site markers are transient - keep them out of the save (they'd pollute RWT's shared world and the KMHSite
    // def won't resolve on a stock client). Pull them from the holder during save, put them back after.
    [HarmonyPatch(typeof(WorldObjectsHolder), nameof(WorldObjectsHolder.ExposeData))]
    internal static class Patch_WorldObjectsHolder_KmhTransientMarkers
    {
        private static readonly FieldInfo ListField = ResolveListField();
        private static readonly List<WorldObject> _pulled = new List<WorldObject>();

        // backing list is "worldObjects"; fall back by type if renamed
        private static FieldInfo ResolveListField()
        {
            FieldInfo f = AccessTools.Field(typeof(WorldObjectsHolder), "worldObjects");
            if (f != null && typeof(List<WorldObject>).IsAssignableFrom(f.FieldType)) return f;
            foreach (FieldInfo fi in AccessTools.GetDeclaredFields(typeof(WorldObjectsHolder)))
                if (typeof(List<WorldObject>).IsAssignableFrom(fi.FieldType)) return fi;
            return null;
        }

        [HarmonyPrefix]
        private static void Prefix(WorldObjectsHolder __instance)
        {
            _pulled.Clear();
            if (Scribe.mode != LoadSaveMode.Saving || ListField == null) return;
            List<WorldObject> list = ListField.GetValue(__instance) as List<WorldObject>;
            if (list == null) return;
            for (int i = list.Count - 1; i >= 0; i--)
                if (list[i] is KMHSiteWorldObject) { _pulled.Add(list[i]); list.RemoveAt(i); }
        }

        // Finalizer so markers go back even if the save throws
        [HarmonyFinalizer]
        private static void Finalizer(WorldObjectsHolder __instance)
        {
            if (_pulled.Count == 0 || ListField == null) return;
            List<WorldObject> list = ListField.GetValue(__instance) as List<WorldObject>;
            if (list != null) list.AddRange(_pulled);
            _pulled.Clear();
        }
    }
}
