using System;
using System.Collections.Generic;
using KMHPatch.Diagnostics;

namespace KMHPatch.Features.Wealth
{
    // Sums off-map KMH wealth into the one silver figure vanilla folds into threat scaling. Cached - the storyteller asks several times per roll.
    internal static class KmhWealthLedger
    {
        private static readonly List<IKmhWealthSource> _sources = new List<IKmhWealthSource>();
        private static readonly object _lock = new object();

        private static float _cached;
        private static int   _cachedMs = int.MinValue;
        private const int    CacheMs = 250;   // recompute at most ~4x/sec; threat rolls are far rarer than this

        private static bool _builtinsRegistered;

        public static void Register(IKmhWealthSource source)
        {
            if (source == null) return;
            lock (_lock) { if (!_sources.Contains(source)) _sources.Add(source); }
        }

        // Built-in sources are wired lazily (no bootstrap-ordering dependency). Add new systems here as they land.
        private static void EnsureBuiltins()
        {
            if (_builtinsRegistered) return;
            _builtinsRegistered = true;
            Register(new Sources.TreasuryWealthSource());
            Register(new Sources.GuildVaultWealthSource());
            Register(new Sources.MarketplaceWealthSource());
            Register(new Sources.AuctionWealthSource());
            Register(new Sources.WantBoardWealthSource());
            Register(new Sources.MailWealthSource());
            Register(new Sources.QuestWealthSource());
            Register(new Sources.RoadworksWealthSource());
            Register(new Sources.SiteStorageWealthSource());
        }

        // Server-owned, never a player setting - a local opt-out would hand back the dodge; unknown flags read as enabled, so it stays on against older servers.
        public const  string FeatureKey = "wealth";
        public static bool   Enabled => KMHPatch.Features.KmhFeatures.IsEnabled(FeatureKey);

        // Total off-map KMH wealth (silver) for the local player. 0 when disabled or nothing is held. Cached briefly.
        public static float Total()
        {
            if (!Enabled) return 0f;
            EnsureBuiltins();

            int now = Environment.TickCount;
            if (_cachedMs != int.MinValue && (uint)(now - _cachedMs) < CacheMs) return _cached;

            float sum;
            lock (_lock) sum = Sum(_sources);

            _cached = sum;
            _cachedMs = now;
            return sum;
        }

        // Per-source values in the same order and by the same rules Sum uses, so a breakdown can never disagree with the total.
        internal static List<KeyValuePair<string, float>> Breakdown(IEnumerable<IKmhWealthSource> sources)
        {
            var outp = new List<KeyValuePair<string, float>>();
            if (sources == null) return outp;
            foreach (IKmhWealthSource s in sources)
            {
                float v = 0f;
                try { v = s.SilverValue(); }
                catch (Exception ex) { KmhLog.Debug($"KMH wealth source '{s?.Name}' threw: {ex.Message}"); }
                outp.Add(new KeyValuePair<string, float>(s?.Name ?? "?", v > 0f ? v : 0f));
            }
            return outp;
        }

        // Until vanilla's ~83s recount, a deposit counts twice (stale map total AND ledger), so force one - debounced so a deposit burst cannot make it per-frame.
        private const int RecountDebounceTicks = 300;
        private static int _lastForcedRecountTick = -99999;

        public static void NudgeMapRecount()
        {
            if (!Enabled) return;
            try
            {
                Verse.Map map = Verse.Find.CurrentMap;
                if (map?.wealthWatcher == null) return;
                int now = Verse.Find.TickManager?.TicksGame ?? 0;
                if (now - _lastForcedRecountTick < RecountDebounceTicks) return;
                _lastForcedRecountTick = now;
                map.wealthWatcher.ForceRecount(false);
            }
            catch (Exception ex) { Diagnostics.KmhLog.Debug($"KMH wealth: map recount nudge failed: {ex.Message}"); }
        }

        // Pure so the debounce can be proven without a running game.
        internal static bool RecountDue(int nowTick, int lastTick) => nowTick - lastTick >= RecountDebounceTicks;

        public static List<KeyValuePair<string, float>> Breakdown()
        {
            EnsureBuiltins();
            lock (_lock) return Breakdown(new List<IKmhWealthSource>(_sources));
        }

        // Only POSITIVE values count, so a buggy source cannot LOWER threat below real wealth, and one throwing source cannot break the total.
        internal static float Sum(IEnumerable<IKmhWealthSource> sources)
        {
            if (sources == null) return 0f;
            float sum = 0f;
            foreach (IKmhWealthSource s in sources)
            {
                try { float v = s.SilverValue(); if (v > 0f) sum += v; }
                catch (Exception ex) { KmhLog.Debug($"KMH wealth source '{s?.Name}' threw: {ex.Message}"); }
            }
            return sum;
        }
    }
}
