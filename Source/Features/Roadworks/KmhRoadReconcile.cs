namespace KMHPatch.Features.Roadworks
{
    // Pure and RimWorld-free so the rules stay headlessly testable: never delete a road KMH did not write.
    internal static class KmhRoadReconcile
    {
        internal enum Action
        {
            None,       // already correct, or nothing here and nothing wanted
            Add,        // no road on this pair - write ours
            Upgrade,    // something lower-priority is here - write ours over it
            Defer,      // a road at least as good is already here and is not ours - leave it alone
            Remove,     // ours, nothing preceded it - delete the link
            Restore,    // ours, it replaced another road - put that one back
        }

        // What KMH wrote to a pair. No record = no claim.
        internal sealed class Applied
        {
            public string AppliedDefName;
            public string PreviousDefName;   // null when nothing was there before
        }

        // Verdict plus the record to keep, so bookkeeping cannot drift from the decision. Null = stop tracking.
        internal struct Verdict
        {
            public Action Action;
            public Applied Record;
            public string  WriteDefName;        // the def the executor should put on the tile pair (Add/Upgrade/Restore)
            public bool    ReleasedStaleClaim;  // the world stopped showing what we wrote, so the claim went
            public bool    Contested;           // and what replaced it was another road, not bare ground
        }

        // A segment that keeps coming back as somebody else's belongs to another producer; stop trading writes with it.
        internal const int MaxRebuilds = 3;

        internal static Verdict Decide(string wantTier, string wantDefName, int wantPriority, Applied applied,
                                       string existingDefName, int existingPriority, int rebuildsSoFar = 0)
        {
            // RWT regenerates the planet on join, so every claim restored from the save is stale; releasing one must not drop the segment the server still wants.
            bool stale = applied != null && !string.IsNullOrEmpty(applied.AppliedDefName)
                         && !string.Equals(existingDefName, applied.AppliedDefName);
            if (stale) applied = null;

            Verdict v = DecideFresh(wantTier, wantDefName, wantPriority, applied,
                                    existingDefName, existingPriority, rebuildsSoFar);
            v.ReleasedStaleClaim = stale;
            // A wiped road is not a rival: only another producer's road counts against the rebuild budget.
            v.Contested = stale && !string.IsNullOrEmpty(existingDefName);
            return v;
        }

        private static Verdict DecideFresh(string wantTier, string wantDefName, int wantPriority, Applied applied,
                                           string existingDefName, int existingPriority, int rebuildsSoFar)
        {
            bool weClaim = applied != null && !string.IsNullOrEmpty(applied.AppliedDefName);
            bool hasRoad = !string.IsNullOrEmpty(existingDefName);
            bool wanted  = !string.IsNullOrEmpty(wantTier) && !string.IsNullOrEmpty(wantDefName);

            if (!wanted)
            {
                if (!weClaim) return new Verdict { Action = Action.None, Record = null };
                return string.IsNullOrEmpty(applied.PreviousDefName)
                    ? new Verdict { Action = Action.Remove,  Record = null }
                    : new Verdict { Action = Action.Restore, Record = null, WriteDefName = applied.PreviousDefName };
            }

            if (!hasRoad)
                return new Verdict { Action = Action.Add, WriteDefName = wantDefName,
                                     Record = new Applied { AppliedDefName = wantDefName, PreviousDefName = null } };

            if (weClaim)
            {
                if (existingPriority >= wantPriority) return new Verdict { Action = Action.None, Record = applied };
                // Keep what we originally displaced, or we forget how to put it back.
                return new Verdict { Action = Action.Upgrade, WriteDefName = wantDefName,
                                     Record = new Applied { AppliedDefName = wantDefName, PreviousDefName = applied.PreviousDefName } };
            }

            // Paid for, so it gets laid over whatever is there and that road is remembered: vanilla's ancient roads outrank every KMH tier, and deferring left the route invisible.
            if (existingPriority >= wantPriority && rebuildsSoFar >= MaxRebuilds)
                return new Verdict { Action = Action.Defer, Record = null };
            return new Verdict { Action = Action.Upgrade, WriteDefName = wantDefName,
                                 Record = new Applied { AppliedDefName = wantDefName, PreviousDefName = existingDefName } };
        }
    }
}
