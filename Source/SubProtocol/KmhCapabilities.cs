using System;
using System.Collections.Generic;

namespace KMHPatch.SubProtocol
{
    // Features gate on Has(...) rather than on build strings, so a client stays compatible across server builds.
    internal static class KmhCapabilities
    {
        // Tokens must match the server's KmhCapabilities exactly.
        public const string DecimalPrices   = "decimal_prices";
        public const string WealthFlag      = "wealth_flag";
        public const string WorldEventEnd   = "world_event_end";
        public const string ConfigMigration = "config_migration";
        public const string Roadworks       = "roadworks";
        public const string Frontier        = "frontier";
        // Without it a client must not push item metadata, which an older server would only log as an unknown kind.
        public const string SiteMeta        = "site_meta";
        // Absent on an older server, so the owner's settings button is simply not offered there.
        public const string ConfigEditor    = "config_editor";
        // Absent on an older server, which has no kmh.federation.* handler, so Guild Federation is not offered there.
        public const string Federation      = "federation";
        // Absent on an older server, which reads only whole silver for a want, so a fractional price is not offered there.
        public const string WantDecimalPrices = "want_decimal_prices";

        // Capabilities every KMH server has, so they hold even when no manifest is sent.
        private static readonly string[] Baseline = { DecimalPrices };

        private static readonly HashSet<string> _caps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static bool _manifestSeen;

        // An omitted field keeps the inferred baseline rather than wiping it, matching the feature-flag set's rule.
        public static void Apply(string csv, bool present)
        {
            if (!present) return;
            _caps.Clear();
            _manifestSeen = true;
            if (!string.IsNullOrEmpty(csv))
                foreach (string s in csv.Split(','))
                    if (s.Trim().Length > 0) _caps.Add(s.Trim());
        }

        public static void Reset()
        {
            _caps.Clear();
            _manifestSeen = false;
        }

        // A server that never sent a manifest is treated as the baseline.
        public static bool Has(string capability)
            => _manifestSeen ? _caps.Contains(capability) : Array.IndexOf(Baseline, capability) >= 0;

        public static bool ManifestSeen => _manifestSeen;

        // Lets a self-test drive the real gates and then put this session's manifest back exactly as it was.
        internal static (string[] Caps, bool Seen) SaveForTest()
        {
            string[] caps = new string[_caps.Count];
            _caps.CopyTo(caps);
            return (caps, _manifestSeen);
        }

        internal static void RestoreForTest((string[] Caps, bool Seen) saved)
        {
            _caps.Clear();
            if (saved.Caps != null) foreach (string c in saved.Caps) _caps.Add(c);
            _manifestSeen = saved.Seen;
        }
    }
}
