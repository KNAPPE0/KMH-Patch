using System;
using System.Collections.Generic;

namespace KMHPatch.UI
{
    // Last-known-good KMH capability/UI state. Kept separate from the live dispatcher so a re-handshake or a hello that
    // omits a field can't blank the tab's feature buttons (merge-not-replace; capabilities only change when a hello
    // actually carries them).
    internal static class KmhDashboardState
    {
        private static readonly HashSet<string> _disabled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static bool _haveCapabilities;
        private static bool _confirmedThisConnection;

        public static bool HaveCapabilities => _haveCapabilities;

        // True once a KMH server was confirmed on the current RWT connection; stays true across KMH re-handshakes so the
        // feature buttons don't vanish mid-session. Cleared only on a real disconnect or a version mismatch.
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
