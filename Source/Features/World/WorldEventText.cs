using System;
using KMHPatch.Features.World.Dto;
using KMHPatch.UI;

namespace KMHPatch.Features.World
{
    // The server writes the whole player-facing string; Magnitude's meaning is per-type, so never re-derive from it.
    internal static class WorldEventText
    {
        public static string Title(WorldEventDto e)
            => string.IsNullOrWhiteSpace(e?.Title) ? (e?.Type ?? KmhText.Get("KMH_World_Event", "Event")) : e.Title;

        // From the server's UTC clock - never game time.
        public static string Remaining(WorldEventDto e)
            => e == null || e.EndsUtcTicks <= 0 ? "" : DialogLayout.TimeLeft(e.EndsUtcTicks, DateTime.UtcNow.Ticks);
    }
}
