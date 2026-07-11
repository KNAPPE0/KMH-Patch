namespace KMHPatch.Items
{
    // Stable, machine-readable reason an item was allowed or blocked by the shared safety layer. The human string is
    // for tooltips; this code is what call sites branch on (and what a server hook / log can compare against).
    internal enum KmhItemReasonCode
    {
        Ok = 0,
        NullOrDestroyed,   // no live thing / already destroyed
        MissingDef,        // def unloaded or not in the DefDatabase
        NotAnItem,         // category isn't a carryable item
        Minified,          // MinifiedThing wrapper (inner item can't be preserved yet)
        Corpse,            // corpse (rot/pawn data not restorable yet)
        Pawn,              // pawn/animal (dedicated transfer, not item storage)
        NoLabel,           // no readable label
        BadStack,          // invalid stack count
        UnsupportedComp,   // carries comp/data KMH would drop on restore
        ConfigBlocked,     // blocked by server/owner config
        Unknown,           // couldn't be classified - blocked by default
    }
}
