using System;
using KMHPatch.Features.Guilds.Dto;

namespace KMHPatch.Features.Guilds
{
    // InGuild separates "no snapshot yet" from "snapshot arrived and the caller is in no guild".
    public static class GuildCache
    {
        public static GuildSnapshotEnvelope Envelope       { get; private set; }
        public static DateTime              LastUpdatedUtc { get; private set; } = DateTime.MinValue;

        public static bool HasSnapshot => Envelope != null;

        public static bool          InGuild => Envelope?.InGuild ?? false;
        public static GuildSnapshot Guild   => Envelope?.Guild;

        public static event Action Updated;

        internal static void Apply(GuildSnapshotEnvelope envelope)
        {
            // Transports reorder; Clear() on disconnect is what lets a new server's lower revision still apply.
            if (envelope == null) return;
            if (Envelope != null && envelope.Revision < Envelope.Revision) return;
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
