using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace KMHPatch.Patches
{
    // IWindowDrawing gets only a rect, so the window being drawn is noted here; without either member windows stay solid.
    [HarmonyPatch]
    internal static class Patch_Window_KmhGlassTarget
    {
        internal static Window Drawing;

        private static MethodBase TargetMethod() => AccessTools.Method(typeof(Window), "InnerWindowOnGUI");
        private static bool Prepare() => TargetMethod() != null && Patch_WindowDrawing_KmhGlass.Target() != null;

        [HarmonyPrefix] private static void Prefix(Window __instance) => Drawing = __instance;
        [HarmonyFinalizer] private static Exception Finalizer(Exception __exception) { Drawing = null; return __exception; }
    }

    [HarmonyPatch]
    internal static class Patch_WindowDrawing_KmhGlass
    {
        internal static MethodBase Target() => AccessTools.Method("Verse.DefaultWindowDrawing:DoWindowBackground");

        private static MethodBase TargetMethod() => Target();
        private static bool Prepare() => Target() != null;

        [HarmonyPrefix]
        private static bool Prefix(Rect rect)
        {
            if (!(Patch_Window_KmhGlassTarget.Drawing is Window_KMHBase w)) return true;
            float a = w.BackgroundOpacity;
            if (a >= 0.995f) return true;
            UI.KmhGlass.Window(rect, a);
            return false;
        }
    }
}
