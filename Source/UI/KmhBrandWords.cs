using System.Globalization;
using System.Text;

namespace KMHPatch.UI
{
    // Words only Colony Nexus itself may wear: a server's badge or name carrying one would pass for Global or its staff.
    internal static class KmhBrandWords
    {
        // Folded so "C0l0ny-N3xus", "Gl0bal", full-width and Cyrillic look-alikes read as their plain spelling.
        internal static string Skeleton(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length);
            char last = '\0';
            foreach (char raw in s.Normalize(NormalizationForm.FormKD))
            {
                UnicodeCategory cat = CharUnicodeInfo.GetUnicodeCategory(raw);
                if (cat == UnicodeCategory.NonSpacingMark || cat == UnicodeCategory.Format) continue;
                char c = Plain(char.ToLowerInvariant(raw));
                if (!char.IsLetterOrDigit(c) || c == last) continue;
                sb.Append(c);
                last = c;
            }
            return sb.ToString();
        }

        // "I", "l", "1" and "|" draw alike in many fonts, so all of them fold to one letter.
        private static char Plain(char c)
        {
            switch (c)
            {
                case '0': return 'o'; case '3': return 'e'; case '4': return 'a';
                case '5': return 's'; case '7': return 't'; case '8': return 'b';
                case '1': case 'i': case '|': case 'і': case 'ι': case 'ӏ': return 'l';
                case 'а': return 'a'; case 'е': return 'e'; case 'о': return 'o'; case 'р': return 'p';
                case 'с': return 'c'; case 'у': return 'y'; case 'х': return 'x';
                case 'ј': return 'j'; case 'ѕ': return 's'; case 'ο': return 'o'; case 'α': return 'a';
                case 'ε': return 'e'; case 'κ': return 'k'; case 'τ': return 't';
                default: return c;
            }
        }

        private static readonly string[] Authority =
        {
            Skeleton("colony nexus"), Skeleton("nexus"), Skeleton("global"), Skeleton("official"), Skeleton("verified"),
            Skeleton("kmh"), Skeleton("system"), Skeleton("worldweave"), Skeleton("world splice"), Skeleton("knappe0"),
            Skeleton("cn staff"), Skeleton("cn dev"), Skeleton("cn team"),
        };

        // A server's badge label may name its own roles, never Colony Nexus's.
        internal static bool ClaimsAuthority(string text)
        {
            string k = Skeleton(text);
            if (k.Length == 0) return false;
            foreach (string w in Authority) if (k.IndexOf(w, System.StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        // Marks a name uses to look official or verified; the browser draws the real mark itself.
        internal static string StripMarks(string text)
        {
            if (string.IsNullOrEmpty(text)) return text ?? "";
            var sb = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                int cp = char.IsHighSurrogate(text[i]) && i + 1 < text.Length ? char.ConvertToUtf32(text[i], text[i + 1]) : text[i];
                bool pair = cp > 0xFFFF;
                if (!IsMark(cp)) { sb.Append(text[i]); if (pair) sb.Append(text[i + 1]); }
                if (pair) i++;
            }
            return sb.ToString().Trim();
        }

        private static bool IsMark(int cp)
            => cp == 0x2605 || cp == 0x2606 || cp == 0x2B50 || cp == 0x272A || cp == 0x272F || cp == 0x2730 || cp == 0x2729
            || cp == 0x272B || cp == 0x272C || cp == 0x272D || cp == 0x272E || cp == 0x2713 || cp == 0x2714 || cp == 0x2611
            || cp == 0x2705 || cp == 0x1F5F8 || cp == 0x1F5F9 || cp == 0x1F31F || cp == 0x2B52 || cp == 0x235F;
    }
}
