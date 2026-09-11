namespace KMHPatch.Items
{
    // Call sites branch on this code, never on the human string, which is free to be reworded.
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
