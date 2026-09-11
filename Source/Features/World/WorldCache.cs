using System;
using System.Collections.Generic;
using KMHPatch.Features.World.Dto;

namespace KMHPatch.Features.World
{
    // client cache for the latest world snapshot (active events + server quests)
    public static class WorldCache
    {
        public static WorldSnapshot Snapshot       { get; private set; }
        public static DateTime      LastUpdatedUtc { get; private set; } = DateTime.MinValue;

        public static bool HasSnapshot => Snapshot != null;
        public static bool HasEvents => ActiveEvents().Count > 0;

        // EndsUtcTicks == 0 means instantaneous or force-expired, not "runs forever" - disagree with the weather component and the UI advertises weather the engine refuses.
        public static System.Collections.Generic.List<WorldEventDto> ActiveEvents()
        {
            var list = new System.Collections.Generic.List<WorldEventDto>();
            var ev = Snapshot?.Events;
            if (ev == null) return list;
            long now = DateTime.UtcNow.Ticks;
            foreach (var e in ev)
                if (e != null && e.EndsUtcTicks > now)
                    list.Add(e);
            return list;
        }

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

        private static bool _malformedLogged;

        internal static void Apply(WorldSnapshot snapshot)
        {
            // Two transports can deliver out of order; drop stale before the diff or it raises fired/ended backwards.
            if (snapshot == null) return;
            if (Snapshot != null && snapshot.Revision < Snapshot.Revision) return;

            // Seed silently on the session's first snapshot (a reconnect clears it too): no "fired" for events already running when the player joined.
            List<WorldEventDto> oldActive = Snapshot == null ? null : ActiveEvents();

            Snapshot       = Sanitize(snapshot);
            LastUpdatedUtc = DateTime.UtcNow;

            if (oldActive != null) RaiseWorldEventDiffs(oldActive, ActiveEvents());
            if (Diagnostics.KmhLog.DebugEnabled)
            {
                int active = ActiveEvents().Count;   // active, not Events.Count: the raw list can still hold expired ones
                Diagnostics.KmhLog.Debug($"Snapshot received: world.snapshot - {(active > 0 ? active + " event(s)" : "no events")}, {ActiveServerQuests().Count} global quest(s). Dashboard rows World Events + Global Quests will update.");
            }
            KmhCacheEvents.Raise(Updated, "World");
        }

        // Compare the active-event set across a snapshot swap and raise fired/ended to extensions. Keyed by event Id.
        private static void RaiseWorldEventDiffs(List<WorldEventDto> oldActive, List<WorldEventDto> newActive)
        {
            var oldIds = new HashSet<long>(); foreach (WorldEventDto e in oldActive) oldIds.Add(e.Id);
            var newIds = new HashSet<long>(); foreach (WorldEventDto e in newActive) newIds.Add(e.Id);

            foreach (WorldEventDto e in newActive)
                if (!oldIds.Contains(e.Id))
                    Extensibility.KmhClientEventBus.Instance.RaiseWorldEventFired(new KMH.Sdk.Client.Events.KmhWorldEventFiredEvent
                        { Id = e.Id, Type = e.Type ?? "", Title = e.Title ?? "", Magnitude = e.Magnitude, EndsUtcTicks = e.EndsUtcTicks });

            foreach (WorldEventDto e in oldActive)
                if (!newIds.Contains(e.Id))
                    Extensibility.KmhClientEventBus.Instance.RaiseWorldEventEnded(new KMH.Sdk.Client.Events.KmhWorldEventEndedEvent
                        { Id = e.Id, Type = e.Type ?? "", Title = e.Title ?? "" });
        }

        // Harden server text before it reaches a rich-text Label: unbalanced markup throws mid-draw and blanks the KMH tab.
        private static WorldSnapshot Sanitize(WorldSnapshot s)
        {
            if (s == null) return null;
            s.Events       ??= new System.Collections.Generic.List<Dto.WorldEventDto>();
            s.ServerQuests ??= new System.Collections.Generic.List<Dto.ServerQuestDto>();
            bool malformed = false;

            s.Events.RemoveAll(e => e == null);
            foreach (Dto.WorldEventDto e in s.Events)
            {
                string t0 = e.Title, d0 = e.Description;
                e.Type        = Safe(e.Type, 40, stripMarkup: false);   // identifier - used as a type tag
                e.Target      = Safe(e.Target, 80, stripMarkup: false); // identifier - used as a GameConditionDef name
                e.Title       = Safe(e.Title, 80);
                e.Description = Safe(e.Description, 200);
                if (t0 != e.Title || d0 != e.Description) malformed = true;
            }

            s.ServerQuests.RemoveAll(q => q == null);
            foreach (Dto.ServerQuestDto q in s.ServerQuests)
            {
                string t0 = q.Title, d0 = q.Description;
                q.Title         = Safe(q.Title, 80);
                q.Description   = Safe(q.Description, 200);
                q.TargetDefName = Safe(q.TargetDefName, 60);
                if (q.GoalQty     < 0) q.GoalQty     = 0;
                if (q.ProgressQty < 0) q.ProgressQty = 0;
                if (q.RewardPool  < 0) q.RewardPool  = 0;
                if (t0 != q.Title || d0 != q.Description) malformed = true;
            }

            if (malformed && !_malformedLogged)
            {
                _malformedLogged = true;
                Diagnostics.KmhLog.Warn("World snapshot carried markup/oversized text - sanitized it for safe display (logged once).");
            }
            return s;
        }

        // null -> ""; cap length; optionally neutralize angle brackets so embedded/unbalanced tags render literally.
        private static string Safe(string v, int maxLen, bool stripMarkup = true)
        {
            if (string.IsNullOrEmpty(v)) return "";
            if (v.Length > maxLen) v = v.Substring(0, maxLen) + "…";
            return stripMarkup ? v.Replace('<', '‹').Replace('>', '›') : v;
        }

        internal static void Clear()
        {
            Snapshot       = null;
            LastUpdatedUtc = DateTime.MinValue;
        }
    }
}
