using System.Globalization;

namespace KMHPatch.UI
{
    // "1,5" and "1.5" both mean 1.5; "1,000" and "1.000" are refused, since English and German read them differently.
    internal static class KmhNumberInput
    {
        internal static bool TryParse(string s, out decimal value, out bool ambiguous)
        {
            value = 0m;
            ambiguous = false;
            string t = (s ?? "").Trim().Replace(" ", "").Replace(" ", "");
            if (t.Length == 0) return false;

            int seps = 0, at = -1;
            for (int i = 0; i < t.Length; i++)
                if (t[i] == ',' || t[i] == '.') { seps++; at = i; }
            if (seps > 1) { ambiguous = true; return false; }
            if (seps == 1)
            {
                string whole = t.Substring(0, at), frac = t.Substring(at + 1);
                if (frac.Length == 3 && whole.TrimStart('-', '+').TrimStart('0').Length > 0) { ambiguous = true; return false; }
                t = whole + "." + frac;
            }
            return decimal.TryParse(t, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value);
        }

        internal static string AmbiguousText()
            => KmhText.Get("KMH_Number_Ambiguous", "That number could mean two things. Write whole amounts without separators (1000), and fractions with one separator (1.5 or 1,5).");
    }
}
