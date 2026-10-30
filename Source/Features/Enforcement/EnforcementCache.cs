using System;
using System.Collections.Generic;
using KMHPatch.Diagnostics;
using Verse;

namespace KMHPatch.Features.Enforcement
{
    internal static class EnforcementCache
    {
        public static bool Enabled         { get; private set; }
        public static bool AdminBypass     { get; private set; } = true;
        public static bool PreservePersonal { get; private set; }
        public static bool IsAdmin         { get; private set; }
        public static bool HasProfile      { get; private set; }
        public static string ServerProfileHash { get; private set; } = "";
        public static long ProfileRevision { get; private set; }
        public static int  ProfileFiles    { get; private set; }

        private static HashSet<string> _safe = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // ModOrderHash is RWT's own hash of the order document; an empty order enforces nothing.
        public static List<string> ModOrder { get; private set; } = new List<string>();
        public static string ModOrderHash { get; private set; } = "";
        // Consent is bound to this, computed here from the list: the server's own hash is only its word.
        public static string ModOrderFingerprint { get; private set; } = "";
        public static bool HasModOrder => ModOrder.Count > 0;

        // Length-prefixed, so no id can absorb its neighbour; empty for no order, which enforces nothing.
        internal static string OrderFingerprint(IReadOnlyList<string> order)
        {
            if (order == null || order.Count == 0) return "";
            var sb = new System.Text.StringBuilder("kmh.enforcement.order.v1\n");
            foreach (string id in order) sb.Append((id ?? "").Length).Append(':').Append(id).Append('\n');
            return ConfigProfileUtility.Sha256Hex(System.Text.Encoding.UTF8.GetBytes(sb.ToString()));
        }

        internal const int MaxOrderEntries = 512;

        internal const int MaxSafeMods   = 2048;
        internal const int MaxModIdChars = 256;
        internal const int MaxHashChars  = 128;

        private static string Bounded(string s, int max)
            => string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s.Substring(0, max));

        public static event Action Updated;

        public static void Apply(bool enabled, bool adminBypass, bool preservePersonal, bool isAdmin, bool hasProfile,
                                 IEnumerable<string> safeMods, string serverProfileHash = "",
                                 IEnumerable<string> modOrder = null, string modOrderHash = "",
                                 long profileRevision = 0, int profileFiles = 0)
        {
            Enabled         = enabled;
            AdminBypass     = adminBypass;
            PreservePersonal = preservePersonal;
            IsAdmin         = isAdmin;
            HasProfile      = hasProfile;
            ServerProfileHash = Bounded(serverProfileHash, MaxHashChars);
            ProfileRevision = profileRevision;
            ProfileFiles    = profileFiles;

            // Bounded so a malformed snapshot cannot cost unbounded memory or per-mod lookup time.
            HashSet<string> set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (safeMods != null)
                foreach (string m in safeMods)
                {
                    if (set.Count >= MaxSafeMods) break;
                    if (!string.IsNullOrWhiteSpace(m)) set.Add(Bounded(m.Trim(), MaxModIdChars));
                }
            _safe = set;

            // Duplicates collapse: one mod holds one position.
            List<string> order = new List<string>();
            if (modOrder != null)
                foreach (string id in modOrder)
                {
                    if (order.Count >= MaxOrderEntries) break;
                    string trimmed = Bounded((id ?? "").Trim(), MaxModIdChars);
                    if (trimmed.Length > 0 && !ContainsId(order, trimmed))
                        order.Add(trimmed);
                }
            ModOrder = order;
            ModOrderFingerprint = OrderFingerprint(order);
            ModOrderHash = Bounded(modOrderHash, MaxHashChars);

            // Lock patches install on demand; locking and the admin mark-safe button both need them.
            if (enabled || isAdmin)
                LongEventHandler.ExecuteWhenFinished(Patches.EnforcementSettingsPatches.EnsureInstalled);

            KmhCacheEvents.Raise(Updated, "Enforcement");
        }

        // Stays active offline while a profile is applied, which keeps the main menu locked until restore.
        public static bool IsLockActive()
        {
            if (Enabled) return !(AdminBypass && IsAdmin);
            return EnforcementProfileApplier.IsApplied;
        }

        private static bool ContainsId(List<string> list, string id)
        {
            foreach (string existing in list)
                if (string.Equals(existing, id, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public static int SafeCount => _safe.Count;
        public static IEnumerable<string> SafeMods => _safe;

        // Kept raw for matching against mod names and ids; made inert only where drawn.
        internal static string SafeModsShown()
        {
            var shown = new List<string>(_safe.Count);
            foreach (string m in _safe) shown.Add(UI.KmhDisplayText.Inert(m));
            return string.Join(", ", shown.ToArray());
        }

        public static bool IsSafeId(string id)
            => !string.IsNullOrWhiteSpace(id) && _safe.Contains(id.Trim());

        // Workshop suffixes PackageId with "_steam" and a local test install is renamed; an exact compare would
        // miss KMH and lock the settings page that holds the restore button.
        public static bool IsKmhItself(ModContentPack content)
            => content != null
            && (IsKmhId(content.PackageId) || IsKmhId(content.PackageIdPlayerFacing));

        // The base id, optionally suffixed - "knappe.kmh.patcher" is a different mod and must not match.
        private static bool IsKmhId(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return false;
            string trimmed = id.Trim();
            if (string.Equals(trimmed, Constants.PackageId, StringComparison.OrdinalIgnoreCase)) return true;
            if (!trimmed.StartsWith(Constants.PackageId, StringComparison.OrdinalIgnoreCase)) return false;
            char next = trimmed[Constants.PackageId.Length];
            return next == '.' || next == '_';
        }

        public static bool IsModEditable(ModContentPack content)
        {
            if (content == null) return true;
            // The restore button lives in KMH's own settings.
            if (IsKmhItself(content)) return true;
            // Its own settings are back on disk but the server's are still in memory; saving now writes those back.
            if (EnforcementProfileApplier.ModAwaitsRestart(content.FolderName)) return false;
            if (!IsLockActive()) return true;
            // Connected: live safe list. Offline: lock only what the profile wrote.
            if (Enabled) return IsSafeId(content.PackageId) || IsSafeId(content.Name);
            return !EnforcementProfileApplier.IsModConfigEnforced(content.FolderName);
        }

        // Preserve-personal keeps personal field VALUES on apply; it does NOT unlock the UI.
        public static bool PreservePersonalActive()
            => Enabled ? PreservePersonal : EnforcementProfileApplier.PreservePersonalApplied;

        public static string DiagState(ModContentPack content)
            => $"enabled={Enabled}, lockActive={IsLockActive()}, isAdmin={IsAdmin}, adminBypass={AdminBypass}, " +
               $"preserve={PreservePersonalActive()}, applied={EnforcementProfileApplier.IsApplied}, safeCount={SafeCount}, " +
               $"editable={IsModEditable(content)}";

        public static bool IsModEditable(Verse.Mod mod)
            => mod == null || IsModEditable(mod.Content);
    }
}
