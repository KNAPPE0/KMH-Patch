using UnityEngine;
using Verse;

namespace KMHPatch.Patches
{
    internal static class KmhRwtDialogFooter
    {
        private static readonly string FooterText = BuildFooter();

        private static string BuildFooter()
        {
            System.Version v = typeof(KmhRwtDialogFooter).Assembly.GetName().Version;
            return $"{Constants.DisplayName} v{v.Major}.{v.Minor}.{v.Build}";
        }

        internal static void Draw(Rect labelRect, TextAnchor anchor)
        {
            GameFont   prevFont   = Text.Font;
            TextAnchor prevAnchor = Text.Anchor;
            Color      prevColor  = GUI.color;

            Text.Font   = GameFont.Tiny;
            Text.Anchor = anchor;
            GUI.color   = new Color(1f, 1f, 1f, 0.55f);

            Widgets.Label(labelRect, FooterText);

            GUI.color   = prevColor;
            Text.Anchor = prevAnchor;
            Text.Font   = prevFont;
        }
    }
}
