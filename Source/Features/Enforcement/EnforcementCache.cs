using System;
using System.Collections.Generic;
using KMHPatch.Diagnostics;
using Verse;

namespace KMHPatch.Features.Enforcement
{
    // Client cache of the server's enforcement state (from the snapshot). The lock patches ask IsModEditable() per
    // mod
    internal static class EnforcementCache
    {
        public static bool Enabled         { get; private set; }
        public static bool AdminBypass     { get; private set; } = true;
        public static bool PreservePersonal { get; private set; }
        public static bool IsAdmin         { get; private set; }
        public static bool HasProfile      { get; private set; }
        public static string ServerProfileHash { get; private set; } = ""; // the profile the server currently holds

        private static HashSet<string> _safe = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Raised whenever a fresh snapshot lands, so any open UI can refresh.
        public static event Action Updated;

        public static void Apply(bool enabled, bool adminBypass, bool preservePersonal, bool isAdmin, bool hasProfile, IEnumerable<string> safeMods, string serverProfileHash = "")
        {
            Enabled         = enabled;
            AdminBypass     = adminBypass;
            PreservePersonal = preservePersonal;
            IsAdmin         = isAdmin;
            HasProfile      = hasProfile;
            ServerProfileHash = serverProfileHash ?? "";

            HashSet<string> set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (safeMods != null)
                foreach (string m in safeMods)
                    if (!string.IsNullOrWhiteSpace(m)) set.Add(m.Trim());
            _safe = set;

            // Lock patches are install-on-demand; a snapshot that can lock (or an admin who gets the mark-safe
            // button) needs them in place
            if (enabled || isAdmin)
                LongEventHandler.ExecuteWhenFinished(Patches.EnforcementSettingsPatches.EnsureInstalled);

            KmhCacheEvents.Raise(Updated, "Enforcement");
        }

        // Active while connected+enforcing (non-exempt), or offline while a profile is still applied (keeps the
        // main menu locked until restore)
        public static bool IsLockActive()
        {
            if (Enabled) return !(AdminBypass && IsAdmin);
            return EnforcementProfileApplier.IsApplied;
        }

        public static int SafeCount => _safe.Count;
        public static IEnumerable<string> SafeMods => _safe;

        public static bool IsSafeId(string id)
            => !string.IsNullOrWhiteSpace(id) && _safe.Contains(id.Trim());

        // Workshop installs suffix PackageId with "_steam", so an exact compare misses KMH's own mod: the lock then
        // hides its settings and skips WriteSettings, silently reverting them. PackageIdPlayerFacing is undecorated.
        public static bool IsKmhItself(ModContentPack content)
            => content != null
            && (string.Equals(content.PackageId, Constants.PackageId, StringComparison.OrdinalIgnoreCase)
             || string.Equals(content.PackageIdPlayerFacing, Constants.PackageId, StringComparison.OrdinalIgnoreCase));

        // Editable when the lock is off, or this mod is safe (by packageId/name).
        public static bool IsModEditable(ModContentPack content)
        {
            if (content == null) return true;
            // KMH-Patch's own settings stay reachable (the restore button lives there).
            if (IsKmhItself(content)) return true;
            if (!IsLockActive()) return true;
            // Connected: live safe list. Offline: lock only what the profile wrote.
            if (Enabled) return IsSafeId(content.PackageId) || IsSafeId(content.Name);
            return !EnforcementProfileApplier.IsModConfigEnforced(content.FolderName);
        }

        // Preserve-personal keeps personal field VALUES on apply (the merge); it does NOT unlock the UI. Surfaced
        // for the client status view
        public static bool PreservePersonalActive()
            => Enabled ? PreservePersonal : EnforcementProfileApplier.PreservePersonalApplied;

        // One-line state dump for diagnosing "why isn't this mod locked?" - logged once per session the first time
        // a mod's settings are drawn
        public static string DiagState(ModContentPack content)
            => $"enabled={Enabled}, lockActive={IsLockActive()}, isAdmin={IsAdmin}, adminBypass={AdminBypass}, " +
               $"preserve={PreservePersonalActive()}, applied={EnforcementProfileApplier.IsApplied}, safeCount={SafeCount}, " +
               $"editable={IsModEditable(content)}";

        public static bool IsModEditable(Verse.Mod mod)
            => mod == null || IsModEditable(mod.Content);
    }
}
