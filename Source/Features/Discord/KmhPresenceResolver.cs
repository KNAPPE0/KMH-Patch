using System;
using System.Collections.Generic;

namespace KMHPatch.Features.Discord
{
    // Holds one activity still long enough to read; no game calls, so time is passed in.
    internal sealed class KmhPresenceResolver
    {
        // A raider stepping out of sight for a moment must not end the raid on Discord.
        internal static readonly TimeSpan ThreatGrace = TimeSpan.FromSeconds(15);
        internal static readonly TimeSpan MediumGrace = TimeSpan.FromSeconds(6);
        internal static readonly TimeSpan Settle = TimeSpan.FromSeconds(4);
        // Samples come every two seconds, so a gap this short is the same sighting continuing, not a new one.
        internal static readonly TimeSpan SampleGap = TimeSpan.FromSeconds(5);
        // Measured from first to last sighting, so the grace never makes a blip look like a battle.
        internal static readonly TimeSpan MinRaidForRecovery = TimeSpan.FromSeconds(15);
        internal static readonly TimeSpan RecoveryFor = TimeSpan.FromMinutes(3);

        private readonly Dictionary<PresenceKind, PresenceCandidate> _held = new Dictionary<PresenceKind, PresenceCandidate>();
        private readonly Dictionary<PresenceKind, DateTime> _lastSeen = new Dictionary<PresenceKind, DateTime>();
        private readonly Dictionary<PresenceKind, DateTime> _firstSeen = new Dictionary<PresenceKind, DateTime>();
        private readonly List<PresenceCandidate> _eligible = new List<PresenceCandidate>();
        private readonly List<PresenceKind> _drop = new List<PresenceKind>();
        private DateTime _raidSince = DateTime.MinValue;
        private DateTime _raidLastSeen = DateTime.MinValue;
        private DateTime _recoverUntil = DateTime.MinValue;
        private PresenceChoice _shown = new PresenceChoice();

        public PresenceChoice Current => _shown;

        internal static TimeSpan GraceFor(PresenceTier tier)
            => tier >= PresenceTier.High ? ThreatGrace : tier == PresenceTier.Medium ? MediumGrace : TimeSpan.Zero;

        internal static TimeSpan RotateEvery(PresenceTier tier)
            => tier == PresenceTier.Normal ? TimeSpan.FromMinutes(10)
             : tier == PresenceTier.Medium ? TimeSpan.FromMinutes(4) : TimeSpan.FromMinutes(3);

        internal static TimeSpan MinDwell(PresenceTier tier)
            => tier == PresenceTier.Normal ? TimeSpan.FromSeconds(60)
             : tier == PresenceTier.Medium ? TimeSpan.FromSeconds(30) : TimeSpan.FromSeconds(20);

        public void Reset()
        {
            _held.Clear(); _lastSeen.Clear(); _firstSeen.Clear();
            _raidSince = DateTime.MinValue; _raidLastSeen = DateTime.MinValue; _recoverUntil = DateTime.MinValue;
            _shown = new PresenceChoice();
        }

        public PresenceChoice Resolve(List<PresenceCandidate> live, DateTime now)
        {
            bool outsideColony = false;
            foreach (PresenceCandidate c in live)
                if (PresenceKinds.IsPhase(c.Kind) && c.Kind != PresenceKind.Disconnected) outsideColony = true;
            // A raid on the last colony must not survive into the menu or the next colony.
            if (outsideColony && !PresenceKinds.IsPhase(_shown.Kind)) Reset();

            foreach (PresenceCandidate c in live)
            {
                bool returning = _lastSeen.TryGetValue(c.Kind, out DateTime seen) && now - seen <= GraceFor(c.Tier) + SampleGap;
                if (!returning) _firstSeen[c.Kind] = now;
                _lastSeen[c.Kind] = now;
                _held[c.Kind] = c;
            }

            _eligible.Clear();
            _drop.Clear();
            bool anySettled = false;
            foreach (KeyValuePair<PresenceKind, PresenceCandidate> h in _held)
            {
                DateTime seen = _lastSeen[h.Key];
                if (now - seen > GraceFor(h.Value.Tier)) { _drop.Add(h.Key); continue; }
                _eligible.Add(h.Value);
                if (Settled(h.Value, now)) anySettled = true;
            }
            foreach (PresenceKind k in _drop) { _held.Remove(k); _lastSeen.Remove(k); _firstSeen.Remove(k); }

            // Short flickers wait to settle, unless nothing has settled yet and the status would otherwise be empty.
            if (anySettled) _eligible.RemoveAll(c => !Settled(c, now));

            bool raidNow = false;
            foreach (PresenceCandidate c in _eligible) if (PresenceKinds.IsRaid(c.Kind)) raidNow = true;
            foreach (PresenceCandidate c in live) if (PresenceKinds.IsRaid(c.Kind)) _raidLastSeen = now;
            if (raidNow)
            {
                if (_raidSince == DateTime.MinValue) _raidSince = now;
                _recoverUntil = DateTime.MinValue;
            }
            else if (_raidSince != DateTime.MinValue)
            {
                if (_raidLastSeen - _raidSince >= MinRaidForRecovery)
                {
                    _recoverUntil = now + RecoveryFor;
                    _firstSeen[PresenceKind.Recovering] = now;
                }
                _raidSince = DateTime.MinValue;
            }
            if (!raidNow && now < _recoverUntil) _eligible.Add(new PresenceCandidate(PresenceKind.Recovering, PresenceTier.High));

            return Choose(now);
        }

        // Whatever started most recently; ties keep the severity order.
        private int Latest()
        {
            int best = 0;
            DateTime bestAt = DateTime.MinValue;
            for (int i = 0; i < _eligible.Count; i++)
                if (_firstSeen.TryGetValue(_eligible[i].Kind, out DateTime first) && first > bestAt)
                {
                    best = i;
                    bestAt = first;
                }
            return best;
        }

        private int Newest(DateTime shownSince)
        {
            int best = -1;
            DateTime bestAt = shownSince;
            for (int i = 0; i < _eligible.Count; i++)
                if (_eligible[i].Kind != _shown.Kind && _firstSeen.TryGetValue(_eligible[i].Kind, out DateTime first) && first > bestAt)
                {
                    best = i;
                    bestAt = first;
                }
            return best;
        }

        private bool Settled(PresenceCandidate c, DateTime now)
            => c.Tier >= PresenceTier.High || PresenceKinds.IsPhase(c.Kind)
            || (_firstSeen.TryGetValue(c.Kind, out DateTime first) && now - first >= Settle);

        private PresenceChoice Choose(DateTime now)
        {
            if (_eligible.Count == 0)
            {
                if (_shown.Kind != PresenceKind.None) _shown = new PresenceChoice { SinceUtc = now };
                return _shown;
            }

            PresenceTier top = PresenceTier.Normal;
            foreach (PresenceCandidate c in _eligible) if (c.Tier > top) top = c.Tier;

            // Most severe first within a tier: the enum lists each tier mildest to worst.
            _eligible.RemoveAll(c => c.Tier != top);
            _eligible.Sort((a, b) => ((int)b.Kind).CompareTo((int)a.Kind));

            int at = _eligible.FindIndex(c => c.Kind == _shown.Kind);
            int newest = Newest(_shown.SinceUtc);
            PresenceCandidate pick;
            if (at < 0) pick = _eligible[Latest()];
            else if (newest >= 0 && now - _shown.SinceUtc >= MinDwell(top)) pick = _eligible[newest];
            else if (_eligible.Count > 1 && now - _shown.SinceUtc >= RotateEvery(top)) pick = _eligible[(at + 1) % _eligible.Count];
            else pick = _eligible[at];

            bool urgent = pick.Tier == PresenceTier.Critical && _shown.Tier != PresenceTier.Critical;
            if (pick.Kind == _shown.Kind)
            {
                _shown.Label = pick.Label; _shown.Sub = pick.Sub; _shown.WithAllies = pick.WithAllies; _shown.Tier = pick.Tier;
                _shown.Urgent = urgent;
                return _shown;
            }

            _shown = new PresenceChoice
            {
                Kind = pick.Kind, Tier = pick.Tier, Label = pick.Label, Sub = pick.Sub, WithAllies = pick.WithAllies,
                SinceUtc = now, Urgent = urgent,
            };
            return _shown;
        }
    }
}
