namespace KMHPatch.Items
{
    // Context can tighten a rule but never loosen one: the unsafe-type blocks apply to every context.
    internal enum KmhItemContext { Store, Trade, Reward, Generate, Display, Restore }
}
