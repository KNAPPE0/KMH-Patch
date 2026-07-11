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

        private static bool _malformedLogged;

        internal static void Apply(WorldSnapshot snapshot)
        {
            Snapshot       = Sanitize(snapshot);
            LastUpdatedUtc = DateTime.UtcNow;
            if (Diagnostics.KmhLog.DebugEnabled)
                Diagnostics.KmhLog.Debug($"Snapshot received: world.snapshot - {(HasEvents ? Snapshot.Events.Count + " event(s)" : "no events")}, {ActiveServerQuests().Count} global quest(s). Dashboard rows World Events + Global Quests will update.");
            try { Updated?.Invoke(); } catch (Exception ex) { Diagnostics.KmhLog.Warn($"World cache subscriber threw: {ex.Message}"); }
        }

        // Harden server-supplied world data before it can ever reach a rich-text Label. This is the root fix for the
        // blank-tab regression: null lists/entries are dropped, numbers clamped non-negative, and display strings have
        // their angle brackets neutralized + length capped so malformed/unbalanced markup (e.g. from an event title)
        // can't throw mid-draw and blank the KMH tab.
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
