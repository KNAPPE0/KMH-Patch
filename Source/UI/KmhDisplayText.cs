namespace KMHPatch.UI
{
    // Applied where remote text ENTERS a cache, so every renderer is covered without each one remembering to.
    internal static class KmhDisplayText
    {
        // Look-alike brackets rather than stripping, which would eat a player's "<3".
        public static string Inert(string s)
            => string.IsNullOrEmpty(s) ? s : s.Replace('<', '‹').Replace('>', '›');

        // Inert + length cap, for fields that also feed one-line previews.
        public static string Inert(string s, int maxLen)
        {
            if (string.IsNullOrEmpty(s)) return s;
            if (maxLen > 0 && s.Length > maxLen) s = s.Substring(0, maxLen) + "…";
            return Inert(s);
        }
    }
}
