using KMHPatch.Diagnostics;
using KMHPatch.Features.WantBoard.Dto;
using KMHPatch.Notifications;
using KMHPatch.SubProtocol;

namespace KMHPatch.Features.WantBoard
{
    // Fulfilment draws on the treasury server-side, so nothing is removed client-side.
    internal static class WantHandler
    {
        public static void Register() => KmhDispatcher.RegisterHandler(KmhProtocol.Kind.WantSnapshot, OnSnapshot);

        public static bool RequestSnapshot() => KmhDispatcher.Send(KmhProtocol.Kind.WantRequest, null);

        public static bool TryPost(string itemDefName, int qty, int unitPriceMilli, int hours, string visibility,
            int minQuality = 0, string requiredStuff = "", bool allowComplex = false, bool allowTainted = false, bool allowDamaged = false)
        {
            bool sent = KmhDispatcher.Send(KmhProtocol.Kind.WantPost, new
            {
                item_def_name     = itemDefName ?? "",
                qty,
                unit_price_silver = System.Math.Max(1, (int)System.Math.Round(unitPriceMilli / 1000.0)),   // old-server fallback
                unit_price_milli  = unitPriceMilli,
                hours,
                visibility        = visibility ?? "public",
                min_quality       = minQuality,
                required_stuff    = requiredStuff ?? "",
                allow_complex     = allowComplex,
                allow_tainted     = allowTainted,
                allow_damaged     = allowDamaged,
            }, KmhOpId.For($"want.post|{itemDefName}|{qty}|{unitPriceMilli}"));
            if (sent) KmhNotifications.Neutral("Posting want…");
            else      KmhNotifications.NotConnected();
            return sent;
        }

        public static bool TryFulfill(long wantId, int qty)
        {
            bool sent = KmhDispatcher.Send(KmhProtocol.Kind.WantFulfill, new { want_id = wantId, qty },
                KmhOpId.For($"want.fulfill|{wantId}|{qty}"));
            if (sent) KmhNotifications.Neutral($"Delivering {qty}…");
            else      KmhNotifications.NotConnected();
            return sent;
        }

        public static bool TryCancel(long wantId)
        {
            bool sent = KmhDispatcher.Send(KmhProtocol.Kind.WantCancel, new { want_id = wantId });
            if (sent) KmhNotifications.Positive("Cancel sent");
            else      KmhNotifications.NotConnected();
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
