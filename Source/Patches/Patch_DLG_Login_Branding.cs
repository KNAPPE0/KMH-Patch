using GameClient.Dialogs;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace KMHPatch.Patches
{
    // half-opacity KMH footer on RWT's Direct Connect dialog, bottom-center (Confirm/Cancel own the corners), IMGUI
    // globals restored after
    [HarmonyPatch(typeof(DLG_Login), nameof(DLG_Login.DoWindowContents))]
    internal static class Patch_DLG_Login_Branding
    {
        private static readonly string FooterText = BuildFooter();

        private static string BuildFooter()
        {
            System.Version v = typeof(Patch_DLG_Login_Branding).Assembly.GetName().Version;
            return $"{Constants.DisplayName} v{v.Major}.{v.Minor}.{v.Build}";
        }

        [HarmonyPostfix]
        private static void Postfix(Rect rect)
        {
            const float width  = 220f;
            const float height = 18f;

            Rect labelRect = new Rect(
                (rect.width - width) / 2f,
                rect.height - height - 2f,
                width,
                height);

            GameFont   prevFont   = Text.Font;
            TextAnchor prevAnchor = Text.Anchor;
            Color      prevColor  = GUI.color;

            Text.Font   = GameFont.Tiny;
            Text.Anchor = TextAnchor.LowerCenter;
            GUI.color   = new Color(1f, 1f, 1f, 0.55f);

            Widgets.Label(labelRect, FooterText);

            GUI.color   = prevColor;
            Text.Anchor = prevAnchor;
            Text.Font   = prevFont;
        }
    }
}
