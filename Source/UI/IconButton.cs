using UnityEngine;
using Verse;

namespace KMHPatch.UI
{
    // Icons are an enhancement, not a requirement: a missing PNG returns null and the button still works.
    internal static class IconButton
    {
        private const float IconSize    = 22f;
        private const float IconPadding = 6f;
        // Matches Listing_Standard.ButtonText, or an icon button sits off-line from plain ones beside it.
        public const float ListingLineHeight = 30f;

        // Toolbars size from this rather than a guessed constant, which truncates longer labels.
        public static float WidthFor(string label, bool withIcon = true)
        {
            float text = 0f;
            try { text = Text.CalcSize(label ?? "").x; } catch { }
            return Mathf.Ceil(text) + IconPadding * 2f + (withIcon ? IconSize + IconPadding : 0f) + 6f;
        }

        // Square and capped, so a button growing wide or tall never stretches the picture with it.
        public static Rect IconRectFor(Rect rect)
        {
            float size = Mathf.Min(IconSize, Mathf.Max(0f, rect.height - 4f));
            return new Rect(rect.x + IconPadding, rect.y + (rect.height - size) / 2f, size, size);
        }

        public static bool DrawListingButton(Listing_Standard listing, Texture2D icon, string label, string tooltip = null)
        {
            Rect rect = listing.GetRect(ListingLineHeight);
            return Draw(rect, icon, label, tooltip);
        }

        // The label rect EXCLUDES the icon: padding with spaces re-centres a long label over the icon as it narrows.
        public static bool Draw(Rect rect, Texture2D icon, string label, string tooltip = null)
        {
            bool clicked = Widgets.ButtonText(rect, string.Empty);   // frame, hover and click only - no text

            // The icon always draws; a button silently losing its picture reads as a bug rather than a tight fit.
            float labelW = 0f;
            try { labelW = Text.CalcSize(label ?? "").x; } catch { }

            float textX = rect.x + IconPadding;
            if (icon != null)
            {
                Rect iconRect = IconRectFor(rect);
                // ScaleToFit, because a non-square source would otherwise be distorted rather than letterboxed.
                GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit);
                textX = iconRect.xMax + IconPadding;
            }

            float textW = Mathf.Max(0f, rect.xMax - IconPadding - textX);
            if (textW > 0f)
                DialogLayout.LabelTrunc(new Rect(textX, rect.y, textW, rect.height), label, TextAnchor.MiddleCenter);

            // Any button whose label still had to be shortened says the whole thing on hover.
            if (string.IsNullOrEmpty(tooltip) && labelW > textW) tooltip = label;
            if (!string.IsNullOrEmpty(tooltip))
            {
                TooltipHandler.TipRegion(rect, tooltip);
            }
            return clicked;
        }
    }
}
