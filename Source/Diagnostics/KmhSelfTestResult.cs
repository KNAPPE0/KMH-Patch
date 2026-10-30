namespace KMHPatch.Diagnostics
{
    // A skip keeps ok=false (the tuple has no third state), so a marker-unaware harness reports failure, not coverage.
    internal static class KmhSelfTestResult
    {
        public const string SkipPrefix = "SKIP: ";

        public static (string, bool, string) Skipped(string name, string why)
            => (name, false, SkipPrefix + (string.IsNullOrEmpty(why) ? "prerequisite unavailable" : why));

        public static bool IsSkip(string detail)
            => detail != null && detail.StartsWith(SkipPrefix, System.StringComparison.Ordinal);
    }
}
