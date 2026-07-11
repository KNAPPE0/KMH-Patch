using KMHPatch.Diagnostics;
using KMHPatch.UI;
using KMHPatch.Features.Marketplace.Dto;
using KMHPatch.Notifications;
using KMHPatch.SubProtocol;

namespace KMHPatch.Features.Marketplace
{
    // Marketplace sub-protocol: request + mutation (post/buy/cancel) methods. Mutations send a minimal envelope; the
    // server applies and broadcasts a fresh snapshot, so no per-call response handler is needed.
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
            // the picker hands us a composed treasury key - send the parts so the listing keeps material + quality
            UI.ItemKeys.Split(itemDefName, out string pureDef, out string stuffDef, out int qualityIdx);
            bool sent = KmhDispatcher.Send(KmhProtocol.Kind.MarketplacePost, new
            {
                item_def_name     = pureDef,
                stuff_def_name    = stuffDef,
                quality_index     = qualityIdx,
                qty               = qty,
                unit_price_silver = unitPriceSilver,
                visibility        = visibility ?? VisibilityPublic,
                expires_hours     = expiresInHours < 0 ? 0 : expiresInHours,
            });
            if (sent) KmhNotifications.Neutral($"Posting listing ×{qty}…");
            else      KmhNotifications.Rejected("Not connected to a KMH server");
            return sent;
        }

        // Post a full-state item (complex) by its treasury payload fingerprint - state is preserved through escrow.
        public static bool TryPostPayload(string fingerprint, int qty, int unitPriceSilver,
                                          string visibility = VisibilityPublic, int expiresInHours = 0)
        {
            if (string.IsNullOrEmpty(fingerprint)) { KmhNotifications.Rejected("Item is missing"); return false; }
            if (qty <= 0) { KmhNotifications.Rejected("Quantity must be greater than 0"); return false; }
            if (unitPriceSilver <= 0) { KmhNotifications.Rejected("Unit price must be greater than 0"); return false; }
            bool sent = KmhDispatcher.Send(KmhProtocol.Kind.MarketplacePost, new
            {
                fingerprint       = fingerprint,
                qty               = qty,
                unit_price_silver = unitPriceSilver,
                visibility        = visibility ?? VisibilityPublic,
                expires_hours     = expiresInHours < 0 ? 0 : expiresInHours,
            });
            if (sent) KmhNotifications.Neutral($"Posting listing ×{qty} (full state)…");
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
