using System;
using KMHPatch.Features.Guilds.Dto;

namespace KMHPatch.Features.Guilds
{
    // Client cache for the caller's current guild snapshot.
    //
    // InGuild differentiates "no snapshot yet" (loading) from "snapshot arrived, caller isn't in a guild" (which is
    // the rendered empty state)
    public static class GuildCache
    {
        public static GuildSnapshotEnvelope Envelope       { get; private set; }
        public static DateTime              LastUpdatedUtc { get; private set; } = DateTime.MinValue;

        public static bool HasSnapshot => Envelope != null;

        // Convenience accessors so consumers don't have to dig through the envelope when they just want the guild
        // data
        public static bool          InGuild => Envelope?.InGuild ?? false;
        public static GuildSnapshot Guild   => Envelope?.Guild;

        public static event Action Updated;

        internal static void Apply(GuildSnapshotEnvelope envelope)
        {
            Envelope       = envelope;
            LastUpdatedUtc = DateTime.UtcNow;
            KmhCacheEvents.Raise(Updated, "Guild");
        }

        internal static void Clear()
        {
            Envelope       = null;
            LastUpdatedUtc = DateTime.MinValue;
        }
    }
}
