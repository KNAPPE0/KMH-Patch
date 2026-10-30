using System;

namespace KMHPatch.Features.Global
{
    // Shared and jittered by every Global socket so a Global restart is not met by the whole fleet at once.
    internal static class KmhRealtimeBackoff
    {
        internal static readonly TimeSpan Min = TimeSpan.FromSeconds(2);
        internal static readonly TimeSpan Max = TimeSpan.FromSeconds(60);

        internal static TimeSpan For(int attempt, double jitter01)
        {
            if (attempt < 0) attempt = 0;
            double seconds = Min.TotalSeconds * Math.Pow(2, Math.Min(attempt, 10));
            if (seconds > Max.TotalSeconds) seconds = Max.TotalSeconds;

            if (jitter01 < 0) jitter01 = 0;
            else if (jitter01 > 1) jitter01 = 1;
            double lowest = Min.TotalSeconds;
            double spread = seconds - lowest;
            return TimeSpan.FromSeconds(lowest + spread * jitter01);
        }

        [ThreadStatic] private static Random _rng;

        public static TimeSpan For(int attempt)
        {
            _rng ??= new Random(Guid.NewGuid().GetHashCode());
            return For(attempt, _rng.NextDouble());
        }
    }
}
