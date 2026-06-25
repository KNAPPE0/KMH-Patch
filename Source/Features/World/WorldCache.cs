using System;
using KMHPatch.Features.World.Dto;

namespace KMHPatch.Features.World
{
    // client cache for the latest world snapshot (active events + server quests)
    public static class WorldCache
    {
        public static WorldSnapshot Snapshot       { get; private set; }
        public static DateTime      LastUpdatedUtc { get; private set; } = DateTime.MinValue;

        public static bool HasSnapshot => Snapshot != null;
        public static bool HasEvents => Snapshot?.Events != null && Snapshot.Events.Count > 0;

        // Active global (server) quests only - the snapshot also carries recently-ended ones so the result lingers.
        public static System.Collections.Generic.List<Dto.ServerQuestDto> ActiveServerQuests()
        {
            var list = new System.Collections.Generic.List<Dto.ServerQuestDto>();
            var sq = Snapshot?.ServerQuests;
            if (sq != null)
                foreach (var q in sq)
                    if (q != null && string.Equals(q.State, Dto.ServerQuestDto.StateActive, StringComparison.OrdinalIgnoreCase))
                        list.Add(q);
            return list;
        }

        public static bool HasServerQuests => ActiveServerQuests().Count > 0;

        public static event Action Updated;

        internal static void Apply(WorldSnapshot snapshot)
        {
            Snapshot       = snapshot;
            LastUpdatedUtc = DateTime.UtcNow;
            try { Updated?.Invoke(); } catch (Exception ex) { Diagnostics.KmhLog.Warn($"World cache subscriber threw: {ex.Message}"); }
        }

        internal static void Clear()
        {
            Snapshot       = null;
            LastUpdatedUtc = DateTime.MinValue;
        }
    }
}
