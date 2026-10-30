using System;

namespace KMHPatch.Features.Rwt
{
    // Only a parsed version becomes one, so a server's display name can't be passed where its RWT version belongs.
    internal readonly struct RwtVersion : IEquatable<RwtVersion>
    {
        public static readonly RwtVersion Unknown = default;

        private readonly string _value;

        private RwtVersion(string normalized) { _value = normalized; }

        public string Value => _value ?? "";
        public bool IsKnown => Value.Length > 0;

        public static bool TryParse(string raw, out RwtVersion version)
        {
            string n = RwtVersionText.Normalize(raw);
            if (!LooksLikeVersion(n)) { version = Unknown; return false; }
            version = new RwtVersion(n);
            return true;
        }

        // Unknown, not an exception: a server may advertise no version, and the browser has a state for that.
        public static RwtVersion From(string raw) => TryParse(raw, out RwtVersion v) ? v : Unknown;

        private static bool LooksLikeVersion(string n)
        {
            if (n.Length == 0) return false;
            bool digit = false;
            foreach (char c in n)
            {
                if (c >= '0' && c <= '9') { digit = true; continue; }
                if (c != '.') return false;
            }
            return digit;
        }

        public bool Equals(RwtVersion other) => RwtVersionText.Same(Value, other.Value);
        public override bool Equals(object obj) => obj is RwtVersion o && Equals(o);
        public override int GetHashCode() => Value.ToLowerInvariant().GetHashCode();
        public override string ToString() => Value;
    }
}
