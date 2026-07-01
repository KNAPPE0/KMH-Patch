namespace KMHPatch.Dialogs
{
    // What's-new shows every launch and takes the single popup slot; the welcome patch yields to this so they never stack.
    internal static class KmhMainMenuPopups
    {
        public static bool WhatsNewPending => true;
    }
}
