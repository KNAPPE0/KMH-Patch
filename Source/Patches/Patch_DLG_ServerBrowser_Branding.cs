using HarmonyLib;
using UnityEngine;
using Verse;

namespace KMHPatch.Patches
{
    // Brand RWT's stock server-browser dialog with a small KMH footer so players see at a glance that the patch is
    // loaded while they're picking a server. Non-intrusive, no behavior change
    //
    // We Postfix DoWindowContents so RWT draws its full UI first and our label lands on top of an already-stable
    // layout. The label goes in the bottom-LEFT corner so it doesn't overlap RWT's bottom-center Close button
    //
    // (Once the sub-protocol round-trips with the server, this same patch is the natural place to add per-row KMH
    // badges next to detected KMH-enabled servers. For now it's pure branding.)
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

            // Bottom-LEFT corner of the dialog content rect.
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
