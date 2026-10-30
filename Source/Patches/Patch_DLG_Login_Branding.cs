using HarmonyLib;
using UnityEngine;

namespace KMHPatch.Patches
{
    [HarmonyPatch(typeof(DLG_Login), nameof(DLG_Login.DoWindowContents))]
    internal static class Patch_DLG_Login_Branding
    {
        [HarmonyPostfix]
        private static void Postfix(Rect rect)
        {
            const float width  = 220f;
            const float height = 18f;

            // Bottom centre: Confirm and Cancel own the corners.
            Rect labelRect = new Rect(
                (rect.width - width) / 2f,
                rect.height - height - 2f,
                width,
                height);

            KmhRwtDialogFooter.Draw(labelRect, TextAnchor.LowerCenter);
        }
    }
}
