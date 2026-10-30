namespace KMHPatch.Features.Roadworks
{
    // Pure and RimWorld-free so the rules stay headlessly testable: never delete a road KMH did not write.
    internal static class KmhRoadReconcile
    {
        internal enum Action
        {
            None,
            Add,
            Upgrade,
            Defer,
            Remove,
            Restore,
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
            public string  WriteDefName;
            public bool    ReleasedStaleClaim;
            public bool    Contested;
        }

        // A segment that keeps coming back as somebody else's belongs to another producer; stop trading writes with it.
        internal const int MaxRebuilds = 3;

        internal static Verdict Decide(string wantTier, string wantDefName, int wantPriority, Applied applied,
                                       string existingDefName, int existingPriority, int rebuildsSoFar = 0)
        {
            // RWT regenerates the planet on join, so restored claims go stale; releasing one must not drop a wanted segment.
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

            // Paid for, so laid over whatever is there (and remembered): vanilla's ancient roads outrank every KMH tier.
            if (existingPriority >= wantPriority && rebuildsSoFar >= MaxRebuilds)
                return new Verdict { Action = Action.Defer, Record = null };
            return new Verdict { Action = Action.Upgrade, WriteDefName = wantDefName,
                                 Record = new Applied { AppliedDefName = wantDefName, PreviousDefName = existingDefName } };
        }
    }
}
