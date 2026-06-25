using KMHPatch.Diagnostics;
using KMHPatch.Features.World.Dto;
using KMHPatch.Notifications;
using KMHPatch.SubProtocol;
using KMHPatch.UI;
using RimWorld.Planet;
using Verse;

namespace KMHPatch.Features.World
{
    // Receives kmh.world.snapshot pushes into WorldCache (pushed on handshake + every change, no polling).
    internal static class WorldHandler
    {
        public static void Register()
        {
            KmhDispatcher.RegisterHandler(KmhProtocol.Kind.WorldSnapshot, OnSnapshot);
        }

        public static bool RequestSnapshot() => KmhDispatcher.Send(KmhProtocol.Kind.WorldRequest, null);

        // Report our cumulative contribution; the server keeps the max per user, so resends are harmless.
        public static bool SendContribution(long questId, int total)
            => KmhDispatcher.Send(KmhProtocol.Kind.WorldContribute, new { quest_id = questId, total = total });

        // Remove goods before reporting, restoring on send failure; source is caravan or colony stockpiles.
        public static bool TryDeliver(long questId, string targetDefName, int qty)
        {
            if (qty <= 0) { KmhNotifications.Rejected("Quantity must be greater than 0"); return false; }
            ThingDef def = ColonyGoods.Def(targetDefName);
            if (def == null) { KmhNotifications.Rejected($"Unknown item '{targetDefName}'"); return false; }

            Caravan caravan = CaravanReader.GetSelectedCaravan();
            Map     map     = caravan == null ? ColonyGoods.DepositMap() : null;
            string  src     = caravan != null ? "caravan" : "colony";

            int have = caravan != null ? ColonyGoods.Count(caravan, def) : ColonyGoods.CountOnMap(map, def);
            if (have < qty) { KmhNotifications.Rejected($"Your {src} only has {have} {def.label}"); return false; }

            bool removed = caravan != null
                ? ColonyGoods.TryRemove(caravan, def, qty)
                : ColonyGoods.TryRemoveOnMap(map, def, qty);
            if (!removed) { KmhNotifications.Rejected($"Could not take the items from your {src}"); return false; }

            bool sent = KmhDispatcher.Send(KmhProtocol.Kind.WorldDeliver,
                new { quest_id = questId, item_def_name = targetDefName, qty });
            if (sent) KmhNotifications.Positive($"Delivered ×{qty} {def.label}");
            else { ColonyGoods.Deliver(def, qty); KmhNotifications.Rejected("Not connected - items returned"); }
            return sent;
        }

        private static void OnSnapshot(KmhEnvelope env)
        {
            WorldSnapshot snap = env?.DataAs<WorldSnapshot>();
            if (snap == null) { KmhLog.Warn("World snapshot had no parseable payload, ignoring"); return; }
            WorldCache.Apply(snap);
        }
    }
}
