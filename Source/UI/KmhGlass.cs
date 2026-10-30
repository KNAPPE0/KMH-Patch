using UnityEngine;
using Verse;

namespace KMHPatch.UI
{
    // Borders fade less than fills, so a see-through window keeps its edge over a busy map.
    internal static class KmhGlass
    {
        private static readonly Color Edge = new Color(0.40f, 0.43f, 0.47f);

        internal static void Window(Rect rect, float alpha) => Fill(rect, Widgets.WindowBGFillColor, alpha);

        internal static void Section(Rect rect, float alpha)
        {
            if (alpha >= 0.995f) { Widgets.DrawMenuSection(rect); return; }
            Fill(rect, Widgets.MenuSectionBGFillColor, alpha);
        }

        private static void Fill(Rect rect, Color fill, float alpha)
        {
            Color old = GUI.color;
            GUI.color = new Color(fill.r, fill.g, fill.b, alpha);
            GUI.DrawTexture(rect, BaseContent.WhiteTex);
            GUI.color = new Color(Edge.r, Edge.g, Edge.b, Mathf.Clamp01(0.35f + alpha));
            Widgets.DrawBox(rect);
            GUI.color = old;
        }
    }
}
