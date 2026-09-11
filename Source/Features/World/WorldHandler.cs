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

        // Nothing leaves the colony: the server pays from the treasury, the only stock it can prove you own, so this asks and it withdraws or refuses.
        public static bool TryDeliver(long questId, string targetDefName, int qty)
        {
            if (qty <= 0) { KmhNotifications.Rejected("Quantity must be greater than 0"); return false; }
            ThingDef def = ColonyGoods.Def(targetDefName);
            if (def == null) { KmhNotifications.Rejected($"Unknown item '{targetDefName}'"); return false; }

            bool sent = KmhDispatcher.Send(KmhProtocol.Kind.WorldDeliver,
                new { quest_id = questId, item_def_name = targetDefName, qty },
                KmhOpId.For($"world.deliver|{questId}|{targetDefName}|{qty}"));
            if (!sent) KmhNotifications.Rejected("Not connected - nothing was delivered");
            return sent;
        }

        // What the treasury can cover, so the dialog can offer a number the server will actually accept.
        public static int TreasuryStockOf(string targetDefName)
            => Features.Treasury.TreasuryCache.CompactCountOf(targetDefName);

        private static void OnSnapshot(KmhEnvelope env)
        {
            WorldSnapshot snap = env?.DataAs<WorldSnapshot>();
            if (snap == null) { KmhLog.Warn("World snapshot had no parseable payload, ignoring"); return; }
            // Basic stats so a bad payload is diagnosable without a full dump (types/titles only at debug level).
            int evc = snap.Events?.Count ?? 0, qc = snap.ServerQuests?.Count ?? 0;
            KmhLog.Debug($"World snapshot: {evc} event(s), {qc} server quest(s)"
                + (evc > 0 ? " [" + string.Join(", ", (snap.Events ?? new System.Collections.Generic.List<WorldEventDto>()).ConvertAll(e => e == null ? "null" : $"{e.Type}#{e.Id}")) + "]" : ""));
            WorldCache.Apply(snap);
        }
    }
}
