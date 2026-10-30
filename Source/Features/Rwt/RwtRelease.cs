using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace KMHPatch.Features.Rwt
{
    internal enum RwtAssetRole { Unknown, Client, Server, SlimServer }
    internal enum RwtOs { Any, Windows, Linux }
    internal enum RwtArch { Any, X86, X64, Arm, Arm64 }

    internal sealed class RwtAsset
    {
        public string Name = "";
        public string Url = "";
        public long   Size;
        public string Sha256 = "";

        public RwtAssetRole Role = RwtAssetRole.Unknown;
        public RwtOs   Os   = RwtOs.Any;
        public RwtArch Arch = RwtArch.Any;

        // The unprefixed platform archives duplicate the Server ones, so an explicit name wins a tie.
        public bool ExplicitRole;

        public override string ToString() => $"{Name} [{Role}/{Os}/{Arch}]";
    }

    internal sealed class RwtRelease
    {
        public string Tag = "";
        public string Version = "";
        public bool   Prerelease;
        public bool   Draft;
        public DateTimeOffset PublishedAt;
        public List<RwtAsset> Assets = new List<RwtAsset>();

        public bool Stable => !Prerelease && !Draft;

        public RwtAsset Find(RwtAssetRole role, RwtOs os, RwtArch arch)
            => Assets.Where(a => a.Role == role)
                     .Where(a => a.Os == os || a.Os == RwtOs.Any)
                     .Where(a => a.Arch == arch || a.Arch == RwtArch.Any)
                     .OrderByDescending(a => a.ExplicitRole)
                     .ThenByDescending(a => a.Os == os)
                     .ThenByDescending(a => a.Arch == arch)
                     .FirstOrDefault();
    }

    // The client package is named by RWT's Steam Workshop id, not a descriptive name, so the mapping is pinned.
    internal static class RwtAssetClassifier
    {
        public const string ClientWorkshopId = "3005289691";

        public static void Classify(RwtAsset asset)
        {
            string n = (asset.Name ?? "").Trim();
            string bare = n.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? n.Substring(0, n.Length - 4) : n;
            string lower = bare.ToLowerInvariant();

            if (bare == ClientWorkshopId)
            {
                asset.Role = RwtAssetRole.Client;
                asset.Os = RwtOs.Any;
                asset.Arch = RwtArch.Any;
                asset.ExplicitRole = true;
                return;
            }

            if (lower.StartsWith("slim-", StringComparison.Ordinal))
            {
                asset.Role = RwtAssetRole.SlimServer;
                asset.ExplicitRole = true;
                ReadPlatform(lower.Substring(5), asset);
                return;
            }

            if (lower.StartsWith("server-", StringComparison.Ordinal))
            {
                asset.Role = RwtAssetRole.Server;
                asset.ExplicitRole = true;
                ReadPlatform(lower.Substring(7), asset);
                return;
            }

            // A bare platform archive is the server build under an older name.
            RwtAsset probe = new RwtAsset();
            ReadPlatform(lower, probe);
            if (probe.Os != RwtOs.Any)
            {
                asset.Role = RwtAssetRole.Server;
                asset.Os = probe.Os;
                asset.Arch = probe.Arch;
                asset.ExplicitRole = false;
                return;
            }

            asset.Role = RwtAssetRole.Unknown;
        }

        private static void ReadPlatform(string s, RwtAsset asset)
        {
            if (s.Contains("win")) asset.Os = RwtOs.Windows;
            else if (s.Contains("linux")) asset.Os = RwtOs.Linux;

            if (s.Contains("arm64")) asset.Arch = RwtArch.Arm64;
            else if (s.Contains("arm")) asset.Arch = RwtArch.Arm;
            else if (s.Contains("x64")) asset.Arch = RwtArch.X64;
            else if (s.Contains("x86")) asset.Arch = RwtArch.X86;
        }
    }

    internal static class RwtVersionText
    {
        // RWT tags carry decorations ("26.8.16.1_(1)" re-cuts, "Release 26.8.31.1"); all must reduce to one version.
        public static string Normalize(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            string v = raw.Trim();

            int space = v.LastIndexOf(' ');
            if (space >= 0 && space + 1 < v.Length) v = v.Substring(space + 1);

            int cut = v.IndexOfAny(new[] { '_', '(', '+' });
            if (cut > 0) v = v.Substring(0, cut);

            return v.Trim().TrimEnd('.');
        }

        public static bool Same(string a, string b)
            => string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);

        // Ordered numerically so "26.8.31.1" sorts above "26.8.16.1"; a tag that is not a version sorts last.
        public static int Compare(string a, string b)
        {
            int[] pa = Parts(a), pb = Parts(b);
            for (int i = 0; i < 4; i++)
            {
                if (pa[i] != pb[i]) return pa[i].CompareTo(pb[i]);
            }
            return 0;
        }

        private static int[] Parts(string v)
        {
            int[] p = { -1, -1, -1, -1 };
            string[] bits = Normalize(v).Split('.');
            for (int i = 0; i < 4 && i < bits.Length; i++)
                if (int.TryParse(bits[i], NumberStyles.None, CultureInfo.InvariantCulture, out int n)) p[i] = n;
            return p;
        }
    }
}
