using System;
using System.Threading;

namespace KMHPatch.Features.Global
{
    internal sealed class KmhPollGate
    {
        private int _inFlight;
        private DateTime _lastAttemptUtc;
        private int _failures;
        private DateTime _holdUntilUtc;

        private readonly TimeSpan _interval;
        private readonly bool _timed;

        // Zero is refused: it is due every OnGUI pass, which is what a constant declared BELOW the gate captures.
        public KmhPollGate(TimeSpan interval)
        {
            if (interval <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(interval), interval,
                    "A poll gate needs a positive interval; zero means due on every frame.");
            _interval = interval;
            _timed = true;
        }

        private KmhPollGate() { }

        // Due() throws on a slot-only gate rather than return false and silently switch that client's polling off.
        public static KmhPollGate SlotOnly() => new KmhPollGate();

        public bool Busy => Volatile.Read(ref _inFlight) != 0;

        // Settable: a cache loaded from disk is data of a known age, not a refresh that never happened.
        public DateTime LastSuccessUtc { get; set; }

        internal static readonly TimeSpan MinRetry = TimeSpan.FromSeconds(5);
        internal static readonly TimeSpan MaxRetry = TimeSpan.FromSeconds(60);

        // Capped doubling so an outage does not turn every open window into a load generator.
        internal static TimeSpan RetryFloor(int failures)
        {
            if (failures <= 0) return TimeSpan.Zero;
            double seconds = MinRetry.TotalSeconds * Math.Pow(2, Math.Min(failures - 1, 8));
            return TimeSpan.FromSeconds(Math.Min(seconds, MaxRetry.TotalSeconds));
        }

        // Uses the last ATTEMPT: after a failure the success clock is always due and the slot was the only throttle.
        internal static bool DueAt(DateTime nowUtc, DateTime lastAttemptUtc, DateTime lastSuccessUtc,
                                   int failures, TimeSpan interval)
            => DueAt(nowUtc, lastAttemptUtc, lastSuccessUtc, failures, interval, default);

        internal static bool DueAt(DateTime nowUtc, DateTime lastAttemptUtc, DateTime lastSuccessUtc,
                                   int failures, TimeSpan interval, DateTime holdUntilUtc)
        {
            // A server-named delay outranks the backoff curve both ways: only the server knows when it will answer again.
            if (holdUntilUtc != default && nowUtc < holdUntilUtc) return false;
            if (lastAttemptUtc != default && nowUtc - lastAttemptUtc < RetryFloor(failures)) return false;
            return lastSuccessUtc == default || nowUtc - lastSuccessUtc >= interval;
        }

        public bool Due(DateTime nowUtc)
        {
            if (!_timed)
                throw new InvalidOperationException("This gate holds only the in-flight slot and has no interval.");
            return !Busy && DueAt(nowUtc, _lastAttemptUtc, LastSuccessUtc, _failures, _interval, _holdUntilUtc);
        }

        // Stamps the attempt on entry so the caller has backed off whether or not it succeeds.
        public bool TryEnter(DateTime nowUtc)
        {
            if (Interlocked.CompareExchange(ref _inFlight, 1, 0) != 0) return false;
            _lastAttemptUtc = nowUtc;
            return true;
        }

        public bool TryEnter() => TryEnter(DateTime.UtcNow);

        public void Exit() => Interlocked.Exchange(ref _inFlight, 0);

        public void Succeeded(DateTime nowUtc) { _failures = 0; _holdUntilUtc = default; LastSuccessUtc = nowUtc; }

        // Not a failure: counting it would stack the backoff on top of the delay the directory named.
        public void HoldUntil(DateTime nowUtc, TimeSpan retryAfter)
        {
            TimeSpan wait = retryAfter > TimeSpan.Zero ? retryAfter : MinRetry;
            if (wait > MaxRetry) wait = MaxRetry;
            _holdUntilUtc = nowUtc + wait;
        }

        public DateTime HoldingUntilUtc => _holdUntilUtc;
        public void Succeeded() => Succeeded(DateTime.UtcNow);

        // Saturates: a wrapped negative count would restore the per-frame loop the floor exists to stop.
        public void Failed() { if (_failures < int.MaxValue) _failures++; }

        public void Reset()
        {
            LastSuccessUtc = default;
            _lastAttemptUtc = default;
            _holdUntilUtc = default;
            _failures = 0;
        }
    }
}
