namespace KMHPatch.Items
{
    // What a Thing is about to be used for. Some contexts are stricter than others, but the unsafe-type blocks in
    // KmhItemSafety apply to ALL of them - KMH must never move an item it can't preserve, display, and restore exactly.
    internal enum KmhItemContext { Store, Trade, Reward, Generate, Display, Restore }
}
