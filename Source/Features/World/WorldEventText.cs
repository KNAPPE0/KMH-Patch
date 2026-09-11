using System;
using KMHPatch.Features.World.Dto;
using KMHPatch.UI;

namespace KMHPatch.Features.World
{
    // The server writes the whole player-facing string; the client never re-derives an effect line from Magnitude, whose meaning is per-type.
    internal static class WorldEventText
    {
        public static string Title(WorldEventDto e)
            => string.IsNullOrWhiteSpace(e?.Title) ? (e?.Type ?? "Event") : e.Title;

        // From the server's UTC clock - never game time.
        public static string Remaining(WorldEventDto e)
            => e == null || e.EndsUtcTicks <= 0 ? "" : DialogLayout.TimeLeft(e.EndsUtcTicks, DateTime.UtcNow.Ticks);
    }
}
