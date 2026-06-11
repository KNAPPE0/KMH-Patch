using UnityEngine;
using Verse;

namespace KMHPatch.UI
{
    // Helpers for drawing buttons with leading icon textures.
    //
    // Gracefully falls back to plain text-button behavior when the icon texture is null - every KMHTextures entry
    // returns null when its backing PNG file is missing, so the mod works without any shipped icon assets. Bigger
    // picture: icons are an enhancement, not a requirement, and the patch is fully functional on a clean install
    internal static class IconButton
    {
        private const float IconSize    = 22f;
        private const float IconPadding = 6f;
        // Matches Listing_Standard.ButtonText's default rect height so an icon button lines up vertically with bare
        // text buttons in the same list
        public const float ListingLineHeight = 30f;

        // Listing-friendly: takes a Listing_Standard and grows the listing's cursor by one button row. Returns true
        // on click. Icon (if any) is left-aligned; label centers in remaining space
        public static bool DrawListingButton(Listing_Standard listing, Texture2D icon, string label, string tooltip = null)
        {
            Rect rect = listing.GetRect(ListingLineHeight);
            return Draw(rect, icon, label, tooltip);
        }

        // Standalone-rect variant for dialogs that manually lay out their button bars. The text gets a
        // leading-spaces prefix when an icon is present so the label doesn't collide with the texture
        public static bool Draw(Rect rect, Texture2D icon, string label, string tooltip = null)
        {
            // Leading spaces shift the centered label rightward so it doesn't overlap the icon. Four spaces matches
            // ~24px in RimWorld's default font - close enough that the icon's left padding doesn't crowd it
            string drawLabel = icon != null ? "    " + label : label;
            bool   clicked   = Widgets.ButtonText(rect, drawLabel);

            if (icon != null)
            {
                float size = Mathf.Min(IconSize, rect.height - 4f);
                Rect iconRect = new Rect(
                    rect.x + IconPadding,
                    rect.y + (rect.height - size) / 2f,
                    size,
                    size);
                // ScaleToFit preserves aspect ratio if the source isn't perfectly square. RimWorld convention is
                // square UI icons, but we don't enforce it
                GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit);
            }

            if (!string.IsNullOrEmpty(tooltip))
            {
                TooltipHandler.TipRegion(rect, tooltip);
            }
            return clicked;
        }
    }
}
