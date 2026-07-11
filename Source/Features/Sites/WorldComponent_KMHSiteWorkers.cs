using System;
using System.Collections.Generic;
using KMHPatch.Diagnostics;
using KMHPatch.Features.Sites.Dto;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace KMHPatch.Features.Sites
{
    // Holds an assigned colonist inside a KMH site. Records are restored by pawn id/name after reload because
    // cross-refs to pawns inside a ThingOwner can resolve late or fail with heavy modpacks.
    public class WorldComponent_KMHSiteWorkers : WorldComponent, IThingHolder
    {
        private const int UnconfirmedSyncLimit = 4;   // sync passes (~40s each) before an unconfirmed join is recalled

        private ThingOwner<Pawn> _held;
        private List<HeldWorker>  _records = new List<HeldWorker>();

        public WorldComponent_KMHSiteWorkers(RimWorld.Planet.World world) : base(world)
        {
            _held = new ThingOwner<Pawn>(this, oneStackOnly: false);
        }

        public static WorldComponent_KMHSiteWorkers Instance => Find.World?.GetComponent<WorldComponent_KMHSiteWorkers>();

        private sealed class HeldWorker : IExposable
        {
            public Pawn   Pawn;
            public int    SiteTile = -1;
            public string Username = "";
            public int    PawnLoadId = -1;
            public string PawnName = "";
            public bool   Confirmed;
            public int    UnconfirmedSyncs;

            public void ExposeData()
            {
                Scribe_References.Look(ref Pawn, "pawn");
                Scribe_Values.Look(ref SiteTile, "siteTile", -1);
                Scribe_Values.Look(ref Username, "username", "");
                Scribe_Values.Look(ref PawnLoadId, "pawnLoadId", -1);
                Scribe_Values.Look(ref PawnName, "pawnName", "");
                Scribe_Values.Look(ref Confirmed, "confirmed", false);
                Scribe_Values.Look(ref UnconfirmedSyncs, "unconfirmedSyncs", 0);
            }
        }

        public bool IsHolding(int siteTile, string username)
        {
            ReattachRecordPawns();
            return FindRecord(siteTile, username) != null;
        }

        public bool IsHoldingAtTile(int siteTile)
        {
            ReattachRecordPawns();
            return FindRecordByTile(siteTile) != null;
        }

        public Pawn HeldPawnAtTile(int siteTile)
        {
            ReattachRecordPawns();
            return FindRecordByTile(siteTile)?.Pawn;
        }

        public Pawn HeldPawnFor(int siteTile, string username)
        {
            ReattachRecordPawns();
            return FindRecord(siteTile, username)?.Pawn ?? HeldPawnAtTile(siteTile);
        }

        public bool HasLiveHeldColonist
        {
            get
            {
                ReattachRecordPawns();
                foreach (HeldWorker r in _records)
                    if (r?.Pawn != null && !r.Pawn.Destroyed && !r.Pawn.Dead) return true;
                return false;
            }
        }

        public bool Hold(Pawn pawn, int siteTile, string username)
        {
            if (pawn == null || pawn.Dead || pawn.Destroyed || siteTile < 0) return false;
            if (FindRecord(siteTile, username) != null) return true;
            try
            {
                ThingOwner from = pawn.holdingOwner;
                if (from == null) return false;
                Caravan source = pawn.GetCaravan();

                if (from.TryTransferToContainer(pawn, _held, 1, out _, canMergeWithExistingStacks: false) <= 0)
                    return false;

                _records.Add(new HeldWorker
                {
                    Pawn = pawn,
                    SiteTile = siteTile,
                    Username = username ?? "",
                    PawnLoadId = pawn.thingIDNumber,
                    PawnName = PawnNameOf(pawn)
                });

                if (source != null && !source.Destroyed && source.PawnsListForReading.Count == 0)
                    source.Destroy();

                KmhLog.Debug($"[KMH Sites] Held {pawn.LabelShortCap} inside site tile {siteTile}.");
                return true;
            }
            catch (Exception ex) { KmhLog.Warn($"[KMH Sites] Hold failed for {pawn?.LabelShortCap}: {ex.Message}"); return false; }
        }

        public bool RecallAtTile(int siteTile, out string where)
        {
            where = "";
            ReattachRecordPawns();
            HeldWorker rec = FindRecordByTile(siteTile);
            return rec != null && Recall(siteTile, rec.Username, out where);
        }

        public bool Recall(int siteTile, string username, out string where)
        {
            where = "";
            ReattachRecordPawns();
            HeldWorker rec = FindRecord(siteTile, username);
            if (rec == null) return false;
            Pawn pawn = rec.Pawn;

            if (pawn == null || pawn.Destroyed)
            {
                _records.Remove(rec);
                KmhLog.Warn($"[KMH Sites] Recall: no valid pawn for site {siteTile} - stuck record dropped.");
                return true;
            }

            try
            {
                Caravan car = PlayerCaravanAt(siteTile);
                if (car != null)
                {
                    if (_held.TryTransferToContainer(pawn, car.pawns, 1, out _, canMergeWithExistingStacks: false) <= 0)
                    {
                        KmhLog.Warn($"[KMH Sites] Recall: caravan transfer failed for {pawn.LabelShortCap}; kept inside for retry.");
                        return false;
                    }
                    where = "your caravan at the site";
                }
                else
                {
                    _held.Remove(pawn);
                    CaravanMaker.MakeCaravan(new List<Pawn> { pawn }, Faction.OfPlayer, siteTile, addToWorldPawnsIfNotAlready: true);
                    where = "a new caravan at the site tile";
                }
                _records.Remove(rec);
                KmhLog.Debug($"[KMH Sites] Recalled {pawn.LabelShortCap} from site tile {siteTile} to {where}.");
                return true;
            }
            catch (Exception ex)
            {
                KmhLog.Warn($"[KMH Sites] Recall failed for site {siteTile}: {ex.Message}");
                try
                {
                    if (!pawn.Destroyed)
                    {
                        if (pawn.holdingOwner == _held) _held.Remove(pawn);
                        DropToAnyHome(pawn);
                        _records.Remove(rec);
                        where = "your colony";
                        return true;
                    }
                }
                catch { }
                return false;
            }
        }

        public void SyncClaims(Func<int, string, bool> siteExists, Func<int, string, bool> listedAsWorker)
        {
            ReconcileIntegrity(returnUnrecordedOrphans: false);
            if (_records.Count == 0) return;
            foreach (HeldWorker rec in new List<HeldWorker>(_records))
            {
                string reason = null;
                try
                {
                    if (!siteExists(rec.SiteTile, rec.Username)) reason = "the site is no longer active";
                    else if (listedAsWorker(rec.SiteTile, rec.Username)) { rec.Confirmed = true; rec.UnconfirmedSyncs = 0; }
                    else if (++rec.UnconfirmedSyncs >= UnconfirmedSyncLimit)
                        reason = rec.Confirmed ? "your worker claim ended" : "the server never confirmed the assignment";
                }
                catch { }
                if (reason != null && Recall(rec.SiteTile, rec.Username, out string where))
                    Notifications.KmhNotifications.Neutral($"Your site worker returned to {where} ({reason}).");
            }
        }

        // Rebuild missing records from the server snapshot after save/load. This protects against cross-ref loss where
        // the pawn deep-loads inside _held, but the HeldWorker.Pawn reference resolves null.
        public void AdoptOrphansFromSnapshot(IEnumerable<SiteEntry> sites, string localUsername)
        {
            ReattachRecordPawns();
            if (_held == null || _held.Count == 0 || sites == null) return;

            foreach (Pawn pawn in new List<Pawn>(_held))
            {
                if (pawn == null || pawn.Destroyed || FindRecordByPawn(pawn) != null) continue;

                HeldWorker adopted = TryBuildRecordFromSnapshot(pawn, sites, localUsername);
                if (adopted == null) continue;

                _records.Add(adopted);
                KmhLog.Warn($"[KMH Sites] Reattached held pawn {pawn.LabelShortCap} to site tile {adopted.SiteTile} after reload.");
            }
        }

        public override void WorldComponentTick()
        {
            if (Find.TickManager.TicksGame % 2500 != 0) return;
            if (_held.Count == 0 && _records.Count == 0) return;

            bool canReturnUnknownOrphans = SiteCache.HasSnapshot;
            if (canReturnUnknownOrphans)
                AdoptOrphansFromSnapshot(SiteCache.Snapshot?.Sites, null);

            ReconcileIntegrity(returnUnrecordedOrphans: canReturnUnknownOrphans);
        }

        private void ReconcileIntegrity(bool returnUnrecordedOrphans)
        {
            try
            {
                ReattachRecordPawns();
                _records.RemoveAll(r => r == null || r.Pawn == null || r.Pawn.Destroyed || r.Pawn.holdingOwner != _held);

                for (int i = _held.Count - 1; i >= 0; i--)
                {
                    Pawn p = _held[i];
                    if (p == null) continue;
                    if (p.Destroyed) { _held.Remove(p); continue; }
                    if (FindRecordByPawn(p) != null) continue;

                    if (!returnUnrecordedOrphans)
                    {
                        KmhLog.Warn($"[KMH Sites] Held pawn {p.LabelShortCap} has no worker record yet - keeping safely inside until KMH snapshot recovery.");
                        continue;
                    }

                    KmhLog.Warn($"[KMH Sites] Held pawn {p.LabelShortCap} had no recoverable worker record - returning it to the colony.");
                    _held.Remove(p);
                    DropToAnyHome(p);
                }
            }
            catch (Exception ex) { KmhLog.Warn($"[KMH Sites] Integrity sweep failed: {ex.Message}"); }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Deep.Look(ref _held, "kmhHeldSiteWorkers", this);
            Scribe_Collections.Look(ref _records, "kmhHeldSiteWorkerRecords", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                _held ??= new ThingOwner<Pawn>(this, oneStackOnly: false);
                _records ??= new List<HeldWorker>();
                ReattachRecordPawns();
                _records.RemoveAll(r => r == null || r.Pawn == null || r.Pawn.Destroyed);
            }
        }

        public IThingHolder ParentHolder => null;
        public ThingOwner GetDirectlyHeldThings() => _held;
        public void GetChildHolders(List<IThingHolder> outChildren)
            => ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, GetDirectlyHeldThings());

        private void ReattachRecordPawns()
        {
            if (_held == null || _records == null || _records.Count == 0) return;
            foreach (HeldWorker rec in _records)
            {
                if (rec == null) continue;
                if (rec.Pawn != null && !rec.Pawn.Destroyed && rec.Pawn.holdingOwner == _held)
                {
                    if (rec.PawnLoadId <= 0) rec.PawnLoadId = rec.Pawn.thingIDNumber;
                    if (string.IsNullOrEmpty(rec.PawnName)) rec.PawnName = PawnNameOf(rec.Pawn);
                    continue;
                }

                Pawn match = FindHeldPawn(rec.PawnLoadId, rec.PawnName);
                if (match != null)
                {
                    rec.Pawn = match;
                    rec.PawnLoadId = match.thingIDNumber;
                    if (string.IsNullOrEmpty(rec.PawnName)) rec.PawnName = PawnNameOf(match);
                }
            }
        }

        private Pawn FindHeldPawn(int loadId, string pawnName)
        {
            if (_held == null) return null;
            foreach (Pawn p in _held)
            {
                if (p == null || p.Destroyed) continue;
                if (loadId > 0 && p.thingIDNumber == loadId) return p;
            }
            if (string.IsNullOrEmpty(pawnName)) return null;
            foreach (Pawn p in _held)
                if (p != null && !p.Destroyed && string.Equals(PawnNameOf(p), pawnName, StringComparison.OrdinalIgnoreCase)) return p;
            return null;
        }

        private HeldWorker TryBuildRecordFromSnapshot(Pawn pawn, IEnumerable<SiteEntry> sites, string localUsername)
        {
            int loadId = pawn.thingIDNumber;
            string pawnName = PawnNameOf(pawn);
            foreach (SiteEntry s in sites)
            {
                if (s == null || s.Tile < 0 || s.WorkerProgress == null) continue;
                foreach (KeyValuePair<string, WorkerProgressDto> kv in s.WorkerProgress)
                {
                    string user = kv.Key ?? "";
                    WorkerProgressDto wp = kv.Value;
                    if (!string.IsNullOrEmpty(localUsername) && !string.Equals(user, localUsername, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (wp == null) continue;
                    bool idMatch = wp.PawnLoadId > 0 && wp.PawnLoadId == loadId;
                    bool nameMatch = !string.IsNullOrEmpty(wp.PawnName) && string.Equals(wp.PawnName, pawnName, StringComparison.OrdinalIgnoreCase);
                    if (!idMatch && !nameMatch) continue;

                    return new HeldWorker
                    {
                        Pawn = pawn,
                        SiteTile = s.Tile,
                        Username = user,
                        PawnLoadId = loadId,
                        PawnName = pawnName,
                        Confirmed = true,
                        UnconfirmedSyncs = 0
                    };
                }
            }
            return null;
        }

        private HeldWorker FindRecordByPawn(Pawn pawn)
        {
            if (pawn == null) return null;
            foreach (HeldWorker r in _records)
                if (r != null && r.Pawn == pawn) return r;
            return null;
        }

        private HeldWorker FindRecord(int siteTile, string username)
        {
            foreach (HeldWorker r in _records)
                if (r != null && r.SiteTile == siteTile
                    && string.Equals(r.Username ?? "", username ?? "", StringComparison.OrdinalIgnoreCase))
                    return r;
            return null;
        }

        private HeldWorker FindRecordByTile(int siteTile)
        {
            foreach (HeldWorker r in _records)
                if (r != null && r.SiteTile == siteTile && r.Pawn != null && !r.Pawn.Destroyed) return r;
            return null;
        }

        private static string PawnNameOf(Pawn pawn)
            => pawn?.Name?.ToStringShort ?? pawn?.LabelShortCap ?? "";

        private static Caravan PlayerCaravanAt(int tile)
        {
            foreach (Caravan c in Find.WorldObjects.Caravans)
                if (c != null && c.IsPlayerControlled && c.Tile == tile) return c;
            return null;
        }

        private static void DropToAnyHome(Pawn pawn)
        {
            Map map = Find.AnyPlayerHomeMap;
            if (map == null) { Find.WorldPawns.PassToWorld(pawn, PawnDiscardDecideMode.KeepForever); return; }
            IntVec3 cell = DropCellFinder.TradeDropSpot(map);
            DropPodUtility.DropThingsNear(cell, map, new List<Thing> { pawn }, forbid: false);
        }
    }
}

