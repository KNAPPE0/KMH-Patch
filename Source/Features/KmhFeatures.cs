using System;
using System.Collections.Generic;

namespace KMHPatch.Features
{
    // Which KMH systems the current server has switched off (from the handshake). Used to show them as disabled
    // instead of spinning on a request the server won't answer. Empty until a server tells us otherwise.
    internal static class KmhFeatures
    {
        private static HashSet<string> _disabled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // csv from the hello, e.g. "marketplace,auctions".
        public static void SetDisabled(string csv)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(csv))
                foreach (string s in csv.Split(','))
                    if (s.Trim().Length > 0) set.Add(s.Trim());
            _disabled = set;
        }

        public static void Clear() => _disabled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static bool IsEnabled(string feature) => !_disabled.Contains(feature);
    }
}
