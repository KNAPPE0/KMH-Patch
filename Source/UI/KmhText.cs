using Verse;

namespace KMHPatch.UI
{
    // English is passed in, not looked up: RimWorld returns the raw key for a missing entry rather than falling back.
    internal static class KmhText
    {
        // Set by the self-test's suppression scope: its checks compare with English, whatever the player's language.
        internal static bool EnglishOnlyForTest;

        public static string Get(string key, string english)
        {
            if (string.IsNullOrEmpty(key) || EnglishOnlyForTest) return english ?? "";
            try { if (Translator.CanTranslate(key)) return Translator.TranslateSimple(key); }
            catch { }   // asking before the language is loaded must not take a window down
            return english ?? "";
        }

        // Substituted after translation, so word order stays the translator's rather than baked into concatenation.
        public static string Get(string key, string english, params object[] args)
        {
            string text = Get(key, english);
            if (args == null || args.Length == 0) return text;

            // Fewer placeholders than arguments means a stale translation, and Format would show the wrong value silently.
            if (!Accepts(text, args.Length)) text = english ?? "";

            try { return string.Format(text, args); }
            catch { return text; }   // a translator's stray brace must not throw out of a draw call
        }

        internal static bool Accepts(string text, int argCount)
        {
            if (string.IsNullOrEmpty(text)) return argCount == 0;
            for (int i = 0; i < argCount; i++)
                if (!HasPlaceholder(text, i)) return false;
            return true;
        }

        // Scans in place: this runs for every formatted label drawn, so it must not allocate.
        private static bool HasPlaceholder(string text, int index)
        {
            for (int at = text.IndexOf('{'); at >= 0; at = text.IndexOf('{', at + 1))
            {
                int i = at + 1, n = 0, digits = 0;
                while (i < text.Length && text[i] >= '0' && text[i] <= '9') { n = n * 10 + (text[i] - '0'); i++; digits++; }
                // "{1" also starts "{10}", so only a closer or an alignment/format spec counts.
                if (digits > 0 && n == index && i < text.Length && (text[i] == '}' || text[i] == ',' || text[i] == ':')) return true;
            }
            return false;
        }
    }
}
