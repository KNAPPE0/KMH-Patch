using HarmonyLib;
using RimWorld;
using UnityEngine;

namespace KMHPatch.Patches
{
    [HarmonyPatch(typeof(UIRoot_Play), nameof(UIRoot_Play.UIRootOnGUI))]
    internal static class Patch_UIRoot_KmhPerfOverlay
    {
        private static bool _reported;

        [HarmonyPostfix]
        private static void Postfix()
        {
            if (KMHPatchMod.Settings?.ShowPerfOverlay != true || Event.current == null || Event.current.type != EventType.Repaint) return;
            try { UI.KmhPerfOverlay.Draw(); }
            catch (System.Exception ex)
            {
                if (_reported) return;
                _reported = true;
                Diagnostics.KmhLog.Warn($"KMH performance overlay could not draw and is reported once: {ex.Message}");
            }
        }
    }
}
