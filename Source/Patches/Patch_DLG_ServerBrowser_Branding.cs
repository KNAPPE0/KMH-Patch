using HarmonyLib;
using UnityEngine;
using Verse;

namespace KMHPatch.Patches
{
    [HarmonyPatch(typeof(DLG_ServerBrowser), nameof(DLG_ServerBrowser.DoWindowContents))]
    internal static class Patch_DLG_ServerBrowser_Branding
    {
        private static readonly string FooterText = BuildFooter();

        private static string BuildFooter()
        {
            System.Version v = typeof(Patch_DLG_ServerBrowser_Branding).Assembly.GetName().Version;
            return $"{Constants.DisplayName} v{v.Major}.{v.Minor}.{v.Build}";
        }

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

            GameFont   prevFont   = Text.Font;
            TextAnchor prevAnchor = Text.Anchor;
            Color      prevColor  = GUI.color;

            Text.Font   = GameFont.Tiny;
            Text.Anchor = TextAnchor.LowerLeft;
            GUI.color   = new Color(1f, 1f, 1f, 0.55f);

            Widgets.Label(labelRect, FooterText);

            GUI.color   = prevColor;
            Text.Anchor = prevAnchor;
            Text.Font   = prevFont;
        }
    }
}
