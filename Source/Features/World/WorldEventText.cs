using System;
using KMHPatch.Features.World.Dto;
using KMHPatch.UI;

namespace KMHPatch.Features.World
{
    // Display text for world events. The server writes the whole player-facing string (title + description, duration
    // and magnitude already folded in), so the client never re-derives an effect line from Magnitude: its meaning is
    // per-type (percent for market swings, percent/100 for worker XP, silver for stipends) and a client that guesses
    // just drifts. Server text also means a new event type needs no client update.
    internal static class WorldEventText
    {
        public static string Title(WorldEventDto e)
            => string.IsNullOrWhiteSpace(e?.Title) ? (e?.Type ?? "Event") : e.Title;

        // From the server's UTC clock - never game time.
        public static string Remaining(WorldEventDto e)
            => e == null || e.EndsUtcTicks <= 0 ? "" : DialogLayout.TimeLeft(e.EndsUtcTicks, DateTime.UtcNow.Ticks);
    }
}
