using System;

namespace KMHPatch.Features.Account
{
    // A hint only; Global decides, since only it knows what is reserved or already taken.
    internal static class KmhUsernameRules
    {
        public const int MinLength = 3;
        public const int MaxLength = 24;

        public static bool LooksValid(string raw) => Explain(raw).Length == 0;

        public static string Explain(string raw)
        {
            string s = (raw ?? "").Trim();
            if (s.Length == 0) return "";
            if (s.Length < MinLength) return $"At least {MinLength} characters.";
            if (s.Length > MaxLength) return $"At most {MaxLength} characters.";
            if (!IsLetter(s[0])) return "Has to start with a letter.";
            foreach (char c in s)
                if (!IsLetter(c) && !IsDigit(c) && c != '_') return "Letters, numbers and underscores only.";
            return "";
        }

        private static bool IsLetter(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
        private static bool IsDigit(char c) => c >= '0' && c <= '9';

        // Mirrors Global's suggestion so the field is never blank when a Steam persona could fill it.
        public static string SuggestFrom(string persona)
        {
            string s = persona ?? "";
            System.Text.StringBuilder kept = new System.Text.StringBuilder(s.Length);
            foreach (char c in s)
            {
                if (IsLetter(c) || IsDigit(c) || c == '_') kept.Append(c);
                else if (c == ' ') kept.Append('_');
            }

            string candidate = kept.ToString().Trim('_');
            while (candidate.IndexOf("__", StringComparison.Ordinal) >= 0)
                candidate = candidate.Replace("__", "_");
            while (candidate.Length > 0 && !IsLetter(candidate[0])) candidate = candidate.Substring(1);
            if (candidate.Length > MaxLength) candidate = candidate.Substring(0, MaxLength);
            return LooksValid(candidate) ? candidate : "";
        }
    }
}
