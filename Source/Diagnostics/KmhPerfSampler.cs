using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace KMHPatch.Diagnostics
{
    // Fixed arrays written once per frame, so measuring allocates nothing; sorting waits until someone looks.
    internal static class KmhPerfSampler
    {
        internal const int Frames = 600;   // about ten seconds at 60 fps

        private static readonly float[] _frameMs = new float[Frames];
        private static int _next, _count;

        private static int   _lastTicks = -1;
        private static long  _lastTickStamp;
        private static float _tps = -1f, _tpsTarget = -1f;

        private static string[] _stepNames = new string[0];
        private static long[]   _stepTotal = new long[0], _stepMax = new long[0];
        private static int      _stepFrames;

        private static Snapshot _cached;
        private static long     _cachedAt;

        public sealed class Snapshot
        {
            public int    FramesSampled;
            public float  FpsAvg, FpsLow1, FrameMsP50, FrameMsP95, FrameMsP99, FrameMsWorst;
            public float  Tps = -1f, TpsTarget = -1f;
            public double KmhMsPerFrame;
            public List<(string Step, double AvgMs, double MaxMs)> Steps = new List<(string, double, double)>();
        }

        internal static void Frame(float unscaledDeltaSeconds)
        {
            if (unscaledDeltaSeconds <= 0f || unscaledDeltaSeconds > 10f) return;   // a load screen is not a frame
            _frameMs[_next] = unscaledDeltaSeconds * 1000f;
            _next = (_next + 1) % Frames;
            if (_count < Frames) _count++;
        }

        internal static void Ticks(int ticksGame, float tickRateMultiplier, bool paused)
        {
            long now = Stopwatch.GetTimestamp();
            if (_lastTicks < 0 || ticksGame < _lastTicks) { _lastTicks = ticksGame; _lastTickStamp = now; return; }
            double seconds = (now - _lastTickStamp) / (double)Stopwatch.Frequency;
            if (seconds < 1.0) return;
            _tps = (float)((ticksGame - _lastTicks) / seconds);
            _tpsTarget = paused ? 0f : 60f * tickRateMultiplier;
            _lastTicks = ticksGame;
            _lastTickStamp = now;
        }

        internal static void NoGame() { _lastTicks = -1; _tps = -1f; _tpsTarget = -1f; }

        internal static void Steps(string[] names, long[] elapsedTicks)
        {
            if (names == null || elapsedTicks == null) return;
            if (!ReferenceEquals(names, _stepNames))
            {
                _stepNames = names;
                _stepTotal = new long[names.Length];
                _stepMax   = new long[names.Length];
                _stepFrames = 0;
            }
            // Halved when full, so an old spike does not stay the maximum.
            if (_stepFrames >= Frames)
            {
                for (int i = 0; i < _stepTotal.Length; i++) { _stepTotal[i] /= 2; _stepMax[i] /= 2; }
                _stepFrames /= 2;
            }
            int n = Math.Min(names.Length, elapsedTicks.Length);
            for (int i = 0; i < n; i++)
            {
                _stepTotal[i] += elapsedTicks[i];
                if (elapsedTicks[i] > _stepMax[i]) _stepMax[i] = elapsedTicks[i];
            }
            _stepFrames++;
        }

        // Cached: an open panel asks every frame.
        public static Snapshot Read()
        {
            long now = Stopwatch.GetTimestamp();
            if (_cached != null && now - _cachedAt < Stopwatch.Frequency / 2) return _cached;
            _cached = Build();
            _cachedAt = now;
            return _cached;
        }

        internal static Snapshot Build()
        {
            var s = new Snapshot { Tps = _tps, TpsTarget = _tpsTarget, FramesSampled = _count };
            if (_count > 0)
            {
                float[] sorted = new float[_count];
                Array.Copy(_frameMs, sorted, _count);
                Array.Sort(sorted);
                double sum = 0;
                foreach (float f in sorted) sum += f;
                double avgMs = sum / _count;
                s.FpsAvg       = avgMs > 0 ? (float)(1000.0 / avgMs) : 0f;
                s.FrameMsP50   = Percentile(sorted, 0.50);
                s.FrameMsP95   = Percentile(sorted, 0.95);
                s.FrameMsP99   = Percentile(sorted, 0.99);
                s.FrameMsWorst = sorted[sorted.Length - 1];
                int slow = Math.Max(1, sorted.Length / 100);
                double slowSum = 0;
                for (int i = sorted.Length - slow; i < sorted.Length; i++) slowSum += sorted[i];
                s.FpsLow1 = slowSum > 0 ? (float)(1000.0 / (slowSum / slow)) : 0f;
            }

            if (_stepFrames > 0)
            {
                double toMs = 1000.0 / Stopwatch.Frequency;
                double total = 0;
                for (int i = 0; i < _stepNames.Length && i < _stepTotal.Length; i++)
                {
                    double avg = _stepTotal[i] * toMs / _stepFrames;
                    total += avg;
                    s.Steps.Add((_stepNames[i], avg, _stepMax[i] * toMs));
                }
                s.KmhMsPerFrame = total;
                s.Steps.Sort((a, b) => b.AvgMs.CompareTo(a.AvgMs));
            }
            return s;
        }

        internal static float Percentile(float[] sorted, double p)
        {
            if (sorted == null || sorted.Length == 0) return 0f;
            int i = (int)Math.Ceiling(p * sorted.Length) - 1;
            return sorted[Math.Max(0, Math.Min(sorted.Length - 1, i))];
        }

        internal static void ResetForTest()
        {
            Array.Clear(_frameMs, 0, _frameMs.Length);
            _next = _count = 0;
            _stepNames = new string[0]; _stepTotal = new long[0]; _stepMax = new long[0]; _stepFrames = 0;
            _cached = null;
            NoGame();
        }
    }
}
