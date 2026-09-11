using KMHPatch.Diagnostics;
using KMHPatch.Features.Sites.Dto;
using KMHPatch.Notifications;
using KMHPatch.SubProtocol;

namespace KMHPatch.Features.Sites
{
    // Every mutation is a small envelope; the server applies it, replies with a chat reason, and rebroadcasts.
    internal static class SiteHandler
    {
        public static void Register()
        {
            KmhDispatcher.RegisterHandler(KmhProtocol.Kind.SiteSnapshot, OnSnapshot);
            KmhDispatcher.RegisterHandler(KmhProtocol.Kind.SiteCatalog,  OnCatalog);
            KmhDispatcher.RegisterHandler(KmhProtocol.Kind.SiteQuote,    OnQuote);
        }

        public static bool RequestSnapshot() => KmhDispatcher.Send(KmhProtocol.Kind.SiteRequest, null);

        // Priced through the same server path that charges, so the number shown is the number paid. Caller debounces.
        public static bool RequestQuote(int tile, string itemDefName, int amount, int marketValue, string archetype)
            => KmhDispatcher.Send(KmhProtocol.Kind.SiteQuoteRequest, new
            {
                tile,
                item_def_name = itemDefName ?? "",
                base_amount   = amount,
                market_value  = marketValue,
                archetype     = archetype ?? "",
            });

        // Ask the server for the curated output catalog (server classifies; the picker just renders it).
        public static bool RequestCatalog(bool includeBlocked = false)
            => KmhDispatcher.Send(KmhProtocol.Kind.SiteCatalogRequest, new { include_blocked = includeBlocked });

        public static bool AddBuilding(int tile, string kind)
        {
            bool sent = KmhDispatcher.Send(KmhProtocol.Kind.SiteBuildingAdd, new { tile, kind = kind ?? "" },
                KmhOpId.For($"site.building_add|{tile}|{kind}"));
            if (!sent) KmhNotifications.NotConnected();
            return sent;
        }

        // kind/state pin what the player saw, so a slot that shifted under the confirmation prompt is refused.
        public static bool RemoveBuilding(int tile, int index, string kind, string state)
        {
            bool sent = KmhDispatcher.Send(KmhProtocol.Kind.SiteBuildingRemove,
                                           new { tile, index, kind = kind ?? "", state = state ?? "" });
            if (!sent) KmhNotifications.NotConnected();
            return sent;
        }

        public static bool RepairSite(int tile)
        {
            bool sent = KmhDispatcher.Send(KmhProtocol.Kind.SiteRepair, new { tile });
            if (!sent) KmhNotifications.NotConnected();
            return sent;
        }

        public static bool ClaimOutpost(int tile, bool forGuild)
        {
            bool sent = KmhDispatcher.Send(KmhProtocol.Kind.SiteClaim, new { tile, for_guild = forGuild });
            if (!sent) KmhNotifications.NotConnected();
            return sent;
        }

        public static bool CollectStorage(int tile)
        {
            bool sent = KmhDispatcher.Send(KmhProtocol.Kind.SiteStorageCollect, new { tile });
            if (!sent) KmhNotifications.NotConnected();
            return sent;
        }

        public static bool TryBuild(int tile, string itemDefName, int baseAmount, int marketValue,
            string accessMode, int ownerTaxPercent, string ownerDestination, int marketplaceUnitPrice,
            string archetype)
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
                archetype              = archetype ?? SiteEntry.ArchetypeCustom,
            });
            if (!sent) KmhNotifications.NotConnected();
            return sent;
        }

        // Same fields as a build; the server decides what is allowed and refuses an outpost already set up.
        public static bool TrySetup(int tile, string itemDefName, int baseAmount, int marketValue,
            string accessMode, int ownerTaxPercent, string ownerDestination, int marketplaceUnitPrice,
            string archetype)
        {
            bool sent = KmhDispatcher.Send(KmhProtocol.Kind.SiteSetup, new
            {
                tile                   = tile,
                item_def_name          = itemDefName ?? "",
                base_amount            = baseAmount,
                market_value           = marketValue,
                access_mode            = accessMode ?? SiteEntry.AccessGuildOnly,
                owner_tax_percent      = ownerTaxPercent,
                owner_destination      = ownerDestination ?? SiteEntry.DestTreasury,
                marketplace_unit_price = marketplaceUnitPrice,
                archetype              = archetype ?? SiteEntry.ArchetypeCustom,
            });
            if (!sent) KmhNotifications.NotConnected();
            return sent;
        }

        // present=false re-validates an existing assignment as "pawn away from site" (server pauses that worker).
        public static bool TryJoin(int tile, int baseSkillLevel, string pawnName = "", int pawnLoadId = -1, bool present = true)
            => Send(KmhProtocol.Kind.SiteJoin, new { tile, base_skill_level = baseSkillLevel, pawn_name = pawnName ?? "", pawn_load_id = pawnLoadId, present });

        public static bool TryLeave(int tile)
            => Send(KmhProtocol.Kind.SiteLeave, new { tile });

        // scope: "mine" for the caller's own share, "site" for the site's own output, empty for the server's default.
        public static bool TrySetDestination(int tile, string destination, string scope = "")
            => Send(KmhProtocol.Kind.SiteSetDestination,
                    new { tile, destination = destination ?? "", scope = scope ?? "" });

        public static bool TryCancel(int tile)
            => Send(KmhProtocol.Kind.SiteCancel, new { tile });

        private static bool Send(string kind, object data)
        {
            bool sent = KmhDispatcher.Send(kind, data);
            if (!sent) KmhNotifications.NotConnected();
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
            // The server naming what it actually adopted is the only acceptance signal there is.
            Catalog.SiteMetadataSender.OnServerCatalog(cat.CatalogFingerprint);
        }

        private static void OnQuote(KmhEnvelope env)
        {
            SiteBuildQuote q = env?.DataAs<SiteBuildQuote>();
            if (q == null) { KmhLog.Warn("Site quote had no parseable payload, ignoring"); return; }
            SiteQuoteCache.Apply(q);
        }
    }
}
