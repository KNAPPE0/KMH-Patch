using System.Threading;

namespace KMHPatch.Features.Global
{
    // Checked before applying any identity-scoped answer, so a poll in flight at sign-out or account switch cannot
    // write the previous account's data into the next one's page. One shared counter, so no subsystem goes without.
    internal static class KmhIdentityGeneration
    {
        private static int _generation;

        public static int Current => Volatile.Read(ref _generation);

        // Bumped only by KmhIdentityTeardown, which runs on every identity change, so a new path cannot forget it.
        public static int Bump() => Interlocked.Increment(ref _generation);

        public static bool IsCurrent(int captured) => captured == Current;

        internal static void ResetForTest() => Interlocked.Exchange(ref _generation, 0);
    }
}
