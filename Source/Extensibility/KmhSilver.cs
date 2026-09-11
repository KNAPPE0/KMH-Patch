using System;

namespace KMHPatch.Extensibility
{
    // One conversion seam, because silver and milli share a CLR type and confusing them is a silent 1000x price error.
    internal static class KmhSilver
    {
        public const int MilliPerSilver = 1000;

        public static int ToMilli(int silver)
        {
            if (silver <= 0) return silver;
            long milli = (long)silver * MilliPerSilver;
            return milli > int.MaxValue ? int.MaxValue : (int)milli;
        }

        public static bool TryToMilli(decimal silver, out int milli)
        {
            milli = 0;
            if (silver <= 0m || silver > int.MaxValue / (decimal)MilliPerSilver) return false;
            decimal scaled = silver * MilliPerSilver;
            // Refused rather than rounded, so a price finer than a thousandth can never be listed as a different one.
            if (scaled != decimal.Truncate(scaled)) return false;
            milli = (int)scaled;
            return true;
        }
    }
}
