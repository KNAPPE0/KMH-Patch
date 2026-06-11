using System;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace KMHPatch.Patches
{
    // Draws a small, faint "KMH Patch vX.Y.Z" label in the bottom-right corner of the main menu so players can
    // confirm at a glance that the patch is loaded
    //
    // Same target method as Patch_MainMenuDrawer_MainMenuOnGUI (the welcome dialog patch). Harmony runs multiple
    // postfixes on the same target independently; ordering doesn't matter here because neither postfix touches
    // state the other depends on
    [HarmonyPatch(typeof(MainMenuDrawer), nameof(MainMenuDrawer.MainMenuOnGUI))]
    internal static class Patch_MainMenuDrawer_Badge
    {
        // Cached so we're not allocating + reflecting every frame.
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

            // Top-right corner. Tiny font, half-opacity so it doesn't compete with the menu's own UI
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
