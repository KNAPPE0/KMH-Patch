using KMHPatch.Diagnostics;
using KMHPatch.Features.Sites.Dto;
using KMHPatch.Notifications;
using KMHPatch.SubProtocol;

namespace KMHPatch.Features.Sites
{
    // Registers the kmh.site.snapshot handler and the request + mutation methods the Sites dialog calls. Each
    // mutation sends a small envelope; the server applies it, replies with a chat reason, and rebroadcasts a snapshot.
    internal static class SiteHandler
    {
        public static void Register()
        {
            KmhDispatcher.RegisterHandler(KmhProtocol.Kind.SiteSnapshot, OnSnapshot);
            KmhDispatcher.RegisterHandler(KmhProtocol.Kind.SiteCatalog,  OnCatalog);
        }

        public static bool RequestSnapshot() => KmhDispatcher.Send(KmhProtocol.Kind.SiteRequest, null);

        // Ask the server for the curated output catalog (server classifies; the picker just renders it).
        public static bool RequestCatalog(bool includeBlocked = false)
            => KmhDispatcher.Send(KmhProtocol.Kind.SiteCatalogRequest, new { include_blocked = includeBlocked });

        public static bool TryBuild(int tile, string itemDefName, int baseAmount, int marketValue,
            string accessMode, int ownerTaxPercent, string ownerDestination, int marketplaceUnitPrice)
        {
            bool sent = KmhDispatcher.Send(KmhProtocol.Kind.SiteBuild, new
            {
                tile                   = tile,
                item_def_name          = itemDefName ?? "",
                base_amount            = baseAmount,
                market_value           = marketValue,
                access_mode            = accessMode ?? SiteEntry.AccessGuildOnly,
                owner_tax_percent      = ownerTaxPercent,
                owner_destination      = ownerDestination ?? SiteEntry.DestTreasury,
                marketplace_unit_price = marketplaceUnitPrice,
            });
            if (!sent) KmhNotifications.Rejected("Not connected to a KMH server");
            return sent;
        }

        // present=false re-validates an existing assignment as "pawn away from site" (server pauses that worker).
        public static bool TryJoin(int tile, int baseSkillLevel, string pawnName = "", int pawnLoadId = -1, bool present = true)
            => Send(KmhProtocol.Kind.SiteJoin, new { tile, base_skill_level = baseSkillLevel, pawn_name = pawnName ?? "", pawn_load_id = pawnLoadId, present });

        public static bool TryLeave(int tile)
            => Send(KmhProtocol.Kind.SiteLeave, new { tile });

        public static bool TrySetDestination(int tile, string destination)
            => Send(KmhProtocol.Kind.SiteSetDestination, new { tile, destination = destination ?? "" });

        public static bool TryCancel(int tile)
            => Send(KmhProtocol.Kind.SiteCancel, new { tile });

        private static bool Send(string kind, object data)
        {
            bool sent = KmhDispatcher.Send(kind, data);
            if (!sent) KmhNotifications.Rejected("Not connected to a KMH server");
            return sent;
        }

        private static void OnSnapshot(KmhEnvelope env)
        {
            SiteSnapshot snap = env?.DataAs<SiteSnapshot>();
            if (snap == null) { KmhLog.Warn("Site snapshot had no parseable payload, ignoring"); return; }
            SiteCache.Apply(snap);
        }

        private static void OnCatalog(KmhEnvelope env)
        {
            SiteCatalogSnapshot cat = env?.DataAs<SiteCatalogSnapshot>();
            if (cat == null) { KmhLog.Warn("Site catalog had no parseable payload, ignoring"); return; }
            SiteCatalogCache.Apply(cat);
        }
    }
}
