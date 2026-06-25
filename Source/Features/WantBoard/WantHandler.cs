using KMHPatch.Diagnostics;
using KMHPatch.Features.WantBoard.Dto;
using KMHPatch.Notifications;
using KMHPatch.SubProtocol;

namespace KMHPatch.Features.WantBoard
{
    // Receives kmh.want.snapshot into WantCache + the post/fulfill/cancel sends. Fulfilling delivers items from your
    // KMH treasury server-side, so there's nothing to remove client-side - just send the quantity.
    internal static class WantHandler
    {
        public static void Register() => KmhDispatcher.RegisterHandler(KmhProtocol.Kind.WantSnapshot, OnSnapshot);

        public static bool RequestSnapshot() => KmhDispatcher.Send(KmhProtocol.Kind.WantRequest, null);

        public static bool TryPost(string itemDefName, int qty, int unitPriceSilver, int hours, string visibility)
        {
            bool sent = KmhDispatcher.Send(KmhProtocol.Kind.WantPost, new
            {
                item_def_name     = itemDefName ?? "",
                qty,
                unit_price_silver = unitPriceSilver,
                hours,
                visibility        = visibility ?? "public",
            });
            if (sent) KmhNotifications.Neutral("Posting want…");
            else      KmhNotifications.Rejected("Not connected to a KMH server");
            return sent;
        }

        public static bool TryFulfill(long wantId, int qty)
        {
            bool sent = KmhDispatcher.Send(KmhProtocol.Kind.WantFulfill, new { want_id = wantId, qty });
            if (sent) KmhNotifications.Neutral($"Delivering {qty}…");
            else      KmhNotifications.Rejected("Not connected to a KMH server");
            return sent;
        }

        public static bool TryCancel(long wantId)
        {
            bool sent = KmhDispatcher.Send(KmhProtocol.Kind.WantCancel, new { want_id = wantId });
            if (sent) KmhNotifications.Positive("Cancel sent");
            else      KmhNotifications.Rejected("Not connected to a KMH server");
            return sent;
        }

        private static void OnSnapshot(KmhEnvelope env)
        {
            WantSnapshot snap = env?.DataAs<WantSnapshot>();
            if (snap == null) { KmhLog.Warn("Want snapshot had no parseable payload, ignoring"); return; }
            WantCache.Apply(snap);
        }
    }
}
