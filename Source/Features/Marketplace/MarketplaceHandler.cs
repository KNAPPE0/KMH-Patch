using KMHPatch.Diagnostics;
using KMHPatch.UI;
using KMHPatch.Features.Marketplace.Dto;
using KMHPatch.Notifications;
using KMHPatch.SubProtocol;

namespace KMHPatch.Features.Marketplace
{
    // Registers the marketplace sub-protocol handler + exposes the public request and mutation methods dialogs use
    //
    // Post (creating a new listing) is wired end-to-end via TryPost + Dialog_KMHPostListing's caravan item picker.
    // Buy + Cancel send the minimal mutation envelope; server applies the change and broadcasts a fresh marketplace
    // snapshot, so a per-call response handler isn't needed
    internal static class MarketplaceHandler
    {
        public static void Register()
        {
            KmhDispatcher.RegisterHandler(KmhProtocol.Kind.MarketplaceSnapshot, OnSnapshot);
        }

        public static bool RequestSnapshot()
        {
            return KmhDispatcher.Send(KmhProtocol.Kind.MarketplaceRequest, null);
        }

        public static bool TryBuy(long listingId, int qty)
        {
            if (qty <= 0)
            {
                KmhNotifications.Rejected("Quantity must be greater than 0");
                return false;
            }
            bool sent = KmhDispatcher.Send(KmhProtocol.Kind.MarketplaceBuy,
                new { listing_id = listingId, qty = qty });
            if (sent) KmhNotifications.Positive($"Buying ×{qty} (request sent)");
            else      KmhNotifications.Rejected("Not connected to a KMH server");
            return sent;
        }

        public static bool TryCancel(long listingId)
        {
            bool sent = KmhDispatcher.Send(KmhProtocol.Kind.MarketplaceCancel,
                new { listing_id = listingId });
            if (sent) KmhNotifications.Positive("Cancel request sent");
            else      KmhNotifications.Rejected("Not connected to a KMH server");
            return sent;
        }

        // Visibility values for the visibility field on Post envelopes (snake_case wire strings).
        public const string VisibilityPublic    = "public";
        public const string VisibilityGuildOnly = "guild_only";

        // expiresInHours: 0 = never expires (server's default); > 0 = auto- cancel + refund after that many hours.
        // Negative clamped to 0
        public static bool TryPost(string itemDefName, int qty, int unitPriceSilver,
                                   string visibility = VisibilityPublic,
                                   int    expiresInHours = 0)
        {
            if (string.IsNullOrEmpty(itemDefName))
            {
                KmhNotifications.Rejected("Item is missing");
                return false;
            }
            if (qty <= 0)
            {
                KmhNotifications.Rejected("Quantity must be greater than 0");
                return false;
            }
            if (unitPriceSilver <= 0)
            {
                KmhNotifications.Rejected("Unit price must be greater than 0");
                return false;
            }
            bool sent = KmhDispatcher.Send(KmhProtocol.Kind.MarketplacePost, new
            {
                item_def_name     = itemDefName,
                qty               = qty,
                unit_price_silver = unitPriceSilver,
                visibility        = visibility ?? VisibilityPublic,
                expires_hours     = expiresInHours < 0 ? 0 : expiresInHours,
            });
            if (sent) KmhNotifications.Neutral($"Posting listing ×{qty}…");
            else      KmhNotifications.Rejected("Not connected to a KMH server");
            return sent;
        }

        private static void OnSnapshot(KmhEnvelope env)
        {
            MarketplaceSnapshot snapshot = env?.DataAs<MarketplaceSnapshot>();
            if (snapshot == null)
            {
                KmhLog.Warn("Marketplace snapshot envelope had no parseable payload, ignoring");
                return;
            }
            MarketplaceCache.Apply(snapshot);
        }
    }
}
