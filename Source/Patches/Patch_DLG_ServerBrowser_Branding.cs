using HarmonyLib;
using UnityEngine;

namespace KMHPatch.Patches
{
    [HarmonyPatch(typeof(DLG_ServerBrowser), nameof(DLG_ServerBrowser.DoWindowContents))]
    internal static class Patch_DLG_ServerBrowser_Branding
    {
        [HarmonyPostfix]
        private static void Postfix(Rect rect)
        {
            const float width   = 220f;
            const float height  = 22f;
            const float padding = 6f;

            // Bottom left: RWT's Close button owns the bottom centre.
            Rect labelRect = new Rect(
                padding,
                rect.height - height - padding,
                width,
                height);

            KmhRwtDialogFooter.Draw(labelRect, TextAnchor.LowerLeft);
        }
    }
}
