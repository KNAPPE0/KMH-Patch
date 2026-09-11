using System;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace KMHPatch.Patches
{
    // Shares a target method with the welcome-dialog patch; the postfixes are independent and share no state.
    [HarmonyPatch(typeof(MainMenuDrawer), nameof(MainMenuDrawer.MainMenuOnGUI))]
    internal static class Patch_MainMenuDrawer_Badge
    {
        private static readonly string BadgeText = BuildBadgeText();

        private static string BuildBadgeText()
        {
            Version v = typeof(Patch_MainMenuDrawer_Badge).Assembly.GetName().Version;
            return $"{Constants.DisplayName} v{v.Major}.{v.Minor}.{v.Build}";
        }

        [HarmonyPostfix]
        private static void Postfix()
        {
            const float width   = 250f;
            const float height  = 22f;
            const float padding = 8f;

            Rect r = new Rect(
                Verse.UI.screenWidth - width - padding,
                padding,
                width,
                height);

            GameFont   prevFont   = Text.Font;
            TextAnchor prevAnchor = Text.Anchor;
            Color      prevColor  = GUI.color;

            Text.Font   = GameFont.Tiny;
            Text.Anchor = TextAnchor.UpperRight;
            GUI.color   = new Color(1f, 1f, 1f, 0.5f);

            Widgets.Label(r, BadgeText);

            // Always restore IMGUI globals or we corrupt the next thing that draws.
            GUI.color   = prevColor;
            Text.Anchor = prevAnchor;
            Text.Font   = prevFont;
        }
    }
}
