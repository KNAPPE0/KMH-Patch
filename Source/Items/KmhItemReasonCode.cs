namespace KMHPatch.Items
{
    // Call sites branch on this code, never on the human string, which is free to be reworded.
    internal enum KmhItemReasonCode
    {
        Ok = 0,
        NullOrDestroyed,
        MissingDef,
        NotAnItem,
        Minified,
        Corpse,            // rot and pawn data cannot be restored
        Pawn,              // pawns use a dedicated transfer, not item storage
        NoLabel,
        BadStack,
        UnsupportedComp,   // carries comp data KMH would drop on restore
        ConfigBlocked,
        Unknown,           // unclassifiable, so blocked by default
    }
}
