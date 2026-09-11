using KMHPatch.Features.Sites.Dto;

namespace KMHPatch.Features.Sites
{
    // Deliberately NOT an Updated-raising cache: transient state for one open dialog, not shared server data.
    public static class SiteQuoteCache
    {
        public static SiteBuildQuote Latest { get; private set; }

        internal static void Apply(SiteBuildQuote q) => Latest = q;

        internal static void Clear() => Latest = null;

        // Guards against showing a price next to a different order once the player has typed on.
        public static bool Matches(string defName, int amount, string archetype)
        {
            SiteBuildQuote q = Latest;
            return q != null
                && q.Amount == amount
                && string.Equals(q.ItemDefName ?? "", defName ?? "", System.StringComparison.OrdinalIgnoreCase)
                && string.Equals(q.Archetype ?? "", archetype ?? "", System.StringComparison.OrdinalIgnoreCase);
        }
    }
}
