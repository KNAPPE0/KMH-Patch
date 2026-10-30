using System;
using System.Text;

namespace KMHPatch.Features.Federation
{
    // KMH Global's own rules, checked here only to save a round trip; Global decides and its answer is what is shown.
    internal static class FederationRules
    {
        public const int NameMin = 3, NameMax = 32, TagMin = 2, TagMax = 5, DescriptionMax = 280, ReasonMax = 200;

        public const string LinkActive = "Active", LinkReproof = "ReproofRequired";
        public const string FederationActive = "Active", FederationDisbanded = "Disbanded";

        // The twelve colours Global accepts. Empty means no colour.
        public static readonly string[] Palette =
        {
            "#C0392B", "#E67E22", "#F1C40F", "#27AE60", "#16A085", "#2980B9",
            "#8E44AD", "#2C3E50", "#7F8C8D", "#D35400", "#1ABC9C", "#C2185B",
        };

        public static string NormalizeName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            var sb = new StringBuilder(name.Length);
            bool space = false;
            foreach (char c in name.Trim())
            {
                if (char.IsWhiteSpace(c)) { space = true; continue; }
                if (space && sb.Length > 0) sb.Append(' ');
                space = false;
                sb.Append(c);
            }
            return sb.ToString();
        }

        public static string NormalizeTag(string tag) => (tag ?? "").Trim().ToUpperInvariant();

        public static string NormalizeInvite(string code)
            => (code ?? "").Replace("-", "").Replace(" ", "").Trim().ToUpperInvariant();

        public static string NormalizeDescription(string description)
            => (description ?? "").Replace("\r", "").Trim();

        public static bool ValidName(string normalized)
        {
            if (normalized == null || normalized.Length < NameMin || normalized.Length > NameMax) return false;
            foreach (char c in normalized) if (char.IsControl(c) || c == '<' || c == '>') return false;
            return true;
        }

        public static bool ValidTag(string normalized)
        {
            if (normalized == null || normalized.Length < TagMin || normalized.Length > TagMax) return false;
            foreach (char c in normalized) if (!((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9'))) return false;
            return true;
        }

        public static bool ValidDescription(string normalized)
        {
            if (normalized == null || normalized.Length > DescriptionMax) return false;
            foreach (char c in normalized) if ((char.IsControl(c) && c != '\n') || c == '<' || c == '>') return false;
            return true;
        }

        public static bool ValidColor(string color)
        {
            if (string.IsNullOrEmpty(color)) return true;
            foreach (string p in Palette) if (string.Equals(p, color, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        // A colour that can sit inside a <color=…> tag without being able to close it.
        public static bool IsHexColour(string color)
        {
            if (color == null || color.Length != 7 || color[0] != '#') return false;
            for (int i = 1; i < 7; i++)
            {
                char c = color[i];
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'))) return false;
            }
            return true;
        }

        public static bool IsLiveLinkState(string state) => state == LinkActive || state == LinkReproof;

        // Typed exactly as the tag is shown: a slip of the keyboard must never disband a federation.
        public static bool DisbandConfirmed(string typed, string tag)
            => !string.IsNullOrEmpty(tag) && string.Equals((typed ?? "").Trim(), tag, StringComparison.Ordinal);
    }
}
