using System;
using System.Text.RegularExpressions;
using RimWorld;

namespace KMHPatch.Compat
{
    internal enum GameMatch { Unknown, Same, BuildDiffers, Differs }

    // Every RimWorld-version decision asks here, so supporting another version is one file, not a search for "1.6".
    internal static class KmhGameVersion
    {
        private static readonly Regex Leading = new Regex(@"^\s*(\d+)\.(\d+)(?:\.(\d+))?", RegexOptions.CultureInvariant);

        // Set only by the self-test, so a version comparison can be pinned whatever game the suite runs in.
        internal static string FullForTest;

        // "1.7.4976 rev1043", or empty when RimWorld cannot say.
        public static string Full
        {
            get
            {
                if (FullForTest != null) return FullForTest;
                try { return VersionControl.CurrentVersionStringWithRev ?? ""; } catch { return ""; }
            }
        }

        public static string MajorMinor => MajorMinorOf(Full);

        // 1.7 (Waylith) runs on CoreCLR, 1.6 on Mono: asked as a capability, not inferred from the version number.
        public static bool IsCoreClr => _coreClr ?? (_coreClr = Type.GetType("Mono.Runtime") == null).Value;
        private static bool? _coreClr;

        // "1.6.4871 rev591" -> "1.6", "1.6" -> "1.6"; anything unreadable -> "".
        internal static string MajorMinorOf(string version)
        {
            Match m = Leading.Match(version ?? "");
            return m.Success ? m.Groups[1].Value + "." + m.Groups[2].Value : "";
        }

        // "1.6.4871 rev591" -> "1.6.4871"; only as far as the text goes.
        internal static string BuildOf(string version)
        {
            Match m = Leading.Match(version ?? "");
            return !m.Success ? "" : m.Groups[3].Success ? m.Value.Trim() : m.Groups[1].Value + "." + m.Groups[2].Value;
        }

        // A major.minor mismatch refuses (no shared world), a build difference only notes; "1.6" matches every 1.6 build.
        internal static GameMatch Compare(string server, string mine)
        {
            string s = MajorMinorOf(server), m = MajorMinorOf(mine);
            if (s.Length == 0 || m.Length == 0) return GameMatch.Unknown;
            if (!string.Equals(s, m, StringComparison.Ordinal)) return GameMatch.Differs;
            string sb = BuildOf(server), mb = BuildOf(mine);
            bool bothBuilds = sb.Length > s.Length && mb.Length > m.Length;
            return bothBuilds && !string.Equals(sb, mb, StringComparison.Ordinal) ? GameMatch.BuildDiffers : GameMatch.Same;
        }

        public static GameMatch CompareWithMine(string server) => Compare(server, Full);
    }
}
