using System;
using KMHPatch.Features.Sites.Dto;

namespace KMHPatch.Features.Sites
{
    // Client cache for the server's curated Site output catalog.
    public static class SiteCatalogCache
    {
        public static SiteCatalogSnapshot Catalog { get; private set; }
        public static bool HasCatalog => Catalog != null;

        public static event Action Updated;

        internal static void Apply(SiteCatalogSnapshot cat)
        {
            Catalog = cat;
            try { Updated?.Invoke(); } catch (Exception ex) { Diagnostics.KmhLog.Warn($"Catalog subscriber threw: {ex.Message}"); }
        }

        internal static void Clear() => Catalog = null;
    }
}
