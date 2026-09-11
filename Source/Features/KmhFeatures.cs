namespace KMHPatch.Features
{
    // Backed by last-known-good KmhDashboardState so a hello that omits the field can't wipe capabilities.
    internal static class KmhFeatures
    {
        // csv may be null when the hello omitted the field; null -> keep last-known-good (merge-not-replace).
        public static void SetDisabled(string csv) => UI.KmhDashboardState.ApplyDisabled(csv, present: csv != null);

        public static void Clear() => UI.KmhDashboardState.ResetForNewConnection();

        public static bool IsEnabled(string feature) => UI.KmhDashboardState.IsEnabled(feature);
    }
}
