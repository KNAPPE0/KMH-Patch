using System;
using System.Collections.Generic;

namespace KMHPatch.UI
{
    // Merge, never replace: a hello that omits a field must not blank the tab's feature buttons.
    internal static class KmhDashboardState
    {
        private static readonly HashSet<string> _disabled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static bool _haveCapabilities;
        private static bool _confirmedThisConnection;

        public static bool HaveCapabilities => _haveCapabilities;

        // Survives a re-handshake, so feature buttons do not vanish mid-session.
        public static bool KmhConfirmed => _confirmedThisConnection;

        public static void MarkConfirmed() => _confirmedThisConnection = true;
        public static void ClearConfirmed() => _confirmedThisConnection = false;

        public static void ResetForNewConnection()
        {
            _confirmedThisConnection = false;
            _haveCapabilities = false;
            _disabled.Clear();
        }

        // present=false means the hello omitted the field -> keep the last-known-good set instead of wiping it.
        public static void ApplyDisabled(string csv, bool present)
        {
            if (!present) return;
            _disabled.Clear();
            if (!string.IsNullOrEmpty(csv))
                foreach (string s in csv.Split(','))
                    if (s.Trim().Length > 0) _disabled.Add(s.Trim());
            _haveCapabilities = true;
        }

        public static bool IsEnabled(string feature) => !_disabled.Contains(feature);
    }
}
