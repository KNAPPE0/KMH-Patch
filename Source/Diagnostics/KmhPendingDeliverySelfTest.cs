using System;
using System.Collections.Generic;
using KMHPatch.Features.Delivery;

namespace KMHPatch.Diagnostics
{
    // A delivery replayed on every reconnect must be held once, or every copy lands together.
    internal static class KmhPendingDeliverySelfTest
    {
        public static List<(string, bool, string)> Run()
        {
            var r = new List<(string, bool, string)>();
            KmhPendingDelivery.ClearForTest();
            try
            {
                for (int replay = 0; replay < 4; replay++) KmhPendingDelivery.HoldSilver(250, "250 silver", "inst:41");
                int one = KmhPendingDelivery.CountForTest;
                KmhPendingDelivery.HoldItemKey("Steel", 30, "30 steel", "inst:42");
                KmhPendingDelivery.HoldItemKey("Steel", 30, "30 steel", "inst:42");
                int two = KmhPendingDelivery.CountForTest;
                // The same amount under another id is another delivery, not a replay.
                KmhPendingDelivery.HoldSilver(250, "250 silver", "inst:43");
                int three = KmhPendingDelivery.CountForTest;
                // A grant with no id cannot be told apart from another one, so each is held.
                KmhPendingDelivery.HoldSilver(5, "5 silver");
                KmhPendingDelivery.HoldSilver(5, "5 silver");
                int five = KmhPendingDelivery.CountForTest;
                r.Add(("delivery: a replayed delivery is held once however often it is replayed",
                       one == 1 && two == 2 && three == 3 && five == 5,
                       $"after 4 replays {one}, a second delivery {two}, a third of the same amount {three}, two unnamed grants {five}"));
            }
            catch (Exception ex) { r.Add(("delivery: a replayed delivery is held once however often it is replayed", false, ex.Message)); }
            finally { KmhPendingDelivery.ClearForTest(); }

            // A held copy must not land in whatever game loads next, another server's included, while its owner replays it.
            Func<int> was = KmhPendingDelivery.GenerationSource;
            try
            {
                int gen = 1;
                KmhPendingDelivery.GenerationSource = () => gen;
                KmhPendingDelivery.HoldSilver(250, "250 silver", "inst:51");
                KmhPendingDelivery.HoldSilver(5, "5 silver");
                gen = 2;
                KmhPendingDelivery.HoldItemKey("Steel", 30, "30 steel", "inst:52");
                int afterNewSession = KmhPendingDelivery.CountForTest;
                KmhPendingDelivery.HoldSilver(250, "250 silver", "inst:51");
                int afterReplay = KmhPendingDelivery.CountForTest;
                r.Add(("delivery: a held delivery from an earlier session is dropped, and its replay held again",
                       afterNewSession == 2 && afterReplay == 3,
                       $"after a new session {afterNewSession} (want 2: the unnamed grant and the new one), after the replay {afterReplay} (want 3)"));
            }
            catch (Exception ex) { r.Add(("delivery: a held delivery from an earlier session is dropped, and its replay held again", false, ex.Message)); }
            finally { KmhPendingDelivery.GenerationSource = was; KmhPendingDelivery.ClearForTest(); }

            try
            {
                Verse.ThingDef pawn = null, building = null;
                foreach (Verse.ThingDef d in Verse.DefDatabase<Verse.ThingDef>.AllDefs)
                {
                    if (pawn == null && d.category == Verse.ThingCategory.Pawn) pawn = d;
                    if (building == null && d.category == Verse.ThingCategory.Building && !d.Minifiable && !d.MadeFromStuff) building = d;
                }
                string pawnWhy = UI.ColonyGoods.WhyCannotDeliverKey(pawn?.defName);
                string buildingWhy = UI.ColonyGoods.WhyCannotDeliverKey(building?.defName);
                string steelWhy = UI.ColonyGoods.WhyCannotDeliverKey("Steel");
                Verse.Thing restored = Items.KmhThingCapture.Restore(new Items.KmhThingPayload { DefName = pawn?.defName, StackCount = 1 });
                r.Add(("delivery: a grant naming a pawn or a building is refused, not spawned",
                       pawn != null && building != null && pawnWhy != null && buildingWhy != null && steelWhy == null && restored == null,
                       $"pawn {pawn?.defName}: {(pawnWhy != null ? "refused" : "ALLOWED")}, {building?.defName}: {(buildingWhy != null ? "refused" : "ALLOWED")}, "
                       + $"Steel: {(steelWhy == null ? "allowed" : steelWhy)}, restore of the pawn: {(restored == null ? "refused" : "BUILT")}"));
            }
            catch (Exception ex) { r.Add(("delivery: a grant naming a pawn or a building is refused, not spawned", false, ex.Message)); }

            // A withdrawal aimed at a roofed stockpile must not punch through the roof and damage the room.
            try
            {
                var cells = new List<Verse.IntVec3> { new Verse.IntVec3(1, 0, 1), new Verse.IntVec3(2, 0, 1), new Verse.IntVec3(3, 0, 1), new Verse.IntVec3(4, 0, 1) };
                bool Valid(Verse.IntVec3 c) => c.x != 1;
                bool foundA = UI.ColonyGoods.PickDropCell(cells, Valid, c => true, out Verse.IntVec3 underRoof, out bool roofedA);
                bool foundB = UI.ColonyGoods.PickDropCell(cells, Valid, c => c.x != 4, out Verse.IntVec3 inOpen, out bool roofedB);
                bool open = foundA && foundB && inOpen.x == 4 && !roofedB && underRoof.x == 2 && roofedA;
                bool none = !UI.ColonyGoods.PickDropCell(cells, c => false, c => false, out _, out _);
                r.Add(("delivery: a pod aimed at a stockpile takes an open cell of it, and a fully roofed one is reported as landing beside it",
                       open && none, $"open cell {inOpen} (roofed={roofedB}), all roofed {underRoof} (roofed={roofedA}), no valid cell found={!none}"));
            }
            catch (Exception ex)
            {
                r.Add(("delivery: a pod aimed at a stockpile takes an open cell of it, and a fully roofed one is reported as landing beside it",
                       false, ex.Message));
            }
            return r;
        }
    }
}
