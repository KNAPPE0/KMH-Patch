using System;

namespace KMHPatch.Features.Global
{
    // RWT's wire format changes between releases, so a version mismatch is a real refusal.
    internal static class KmhRwtVersions
    {
        public static string Mine => RwtCompat.ExecutableVersion ?? "";

        public static bool Supports(string serverVersion)
        {
            // Unknown on either side is not a refusal: RWT's own handshake still has the final word at the door.
            if (string.IsNullOrEmpty(serverVersion) || string.IsNullOrEmpty(Mine)) return true;
            return string.Equals(Normalize(serverVersion), Normalize(Mine), StringComparison.OrdinalIgnoreCase);
        }

        // RWT stamps the same release as "26.8.31.1" and as "Release 26.8.31.1" in different places.
        public static string Normalize(string version)
        {
            if (string.IsNullOrEmpty(version)) return "";
            string v = version.Trim();
            int space = v.LastIndexOf(' ');
            if (space >= 0 && space + 1 < v.Length) v = v.Substring(space + 1);
            return v.Trim();
        }
    }
}
