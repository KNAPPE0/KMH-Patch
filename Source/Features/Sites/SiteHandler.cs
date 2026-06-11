using KMHPatch.Diagnostics;
using KMHPatch.Features.Sites.Dto;
using KMHPatch.Notifications;
using KMHPatch.SubProtocol;

namespace KMHPatch.Features.Sites
{
    // Registers the kmh.site.snapshot handler and exposes the request +
    // mutation methods the Sites dialog calls. Mutations send a small envelope;
    // the server applies the change, replies with a chat reason, and rebroadcasts a fresh snapshot - so cache
    // updates flow the same way a polled refresh does
    internal static class SiteHandler
    {
        public static void Register()
        {
            KmhDispatcher.RegisterHandler(KmhProtocol.Kind.SiteSnapshot, OnSnapshot);
        }

        public static bool RequestSnapshot() => KmhDispatcher.Send(KmhProtocol.Kind.SiteRequest, null);

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

        public static bool TryJoin(int tile, int baseSkillLevel)
            => Send(KmhProtocol.Kind.SiteJoin, new { tile, base_skill_level = baseSkillLevel });

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
    }
}
