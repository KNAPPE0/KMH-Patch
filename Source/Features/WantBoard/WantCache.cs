using System;
using KMHPatch.Features.WantBoard.Dto;

namespace KMHPatch.Features.WantBoard
{
    // Client cache for the latest want-board snapshot. Pushed on handshake, on every change, and on the dialog's auto-refresh tick.
    public static class WantCache
    {
        public static WantSnapshot Snapshot       { get; private set; }
        public static DateTime     LastUpdatedUtc { get; private set; } = DateTime.MinValue;
        public static bool         HasSnapshot    => Snapshot != null;

        public static event Action Updated;

        internal static void Apply(WantSnapshot snapshot)
        {
            Snapshot       = snapshot;
            LastUpdatedUtc = DateTime.UtcNow;
            try { Updated?.Invoke(); } catch (Exception ex) { Diagnostics.KmhLog.Warn($"Want cache subscriber threw: {ex.Message}"); }
        }

        internal static void Clear()
        {
            Snapshot       = null;
            LastUpdatedUtc = DateTime.MinValue;
        }
    }
}
