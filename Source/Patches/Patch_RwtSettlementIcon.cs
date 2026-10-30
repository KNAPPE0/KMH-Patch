using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using KMHPatch.Diagnostics;

namespace KMHPatch.Patches
{
    // RWT indexes other players' settlement icons unchecked; an unset one (int.MinValue) throws and hides the settlement.
    internal static class Patch_RwtSettlementIcon
    {
        private static PropertyInfo _iconId;
        private static FieldInfo _icons;
        private static MethodInfo _loadIcons;
        private static bool _loadTried;

        public static void TryApply(Harmony harmony)
        {
            try
            {
                string ns = RwtCompat.ClientNsRoot;
                Type pm = RwtCompat.ResolveType(ns, "PM_Settlements", ns + ".PacketManagers.PM_Settlements");
                Type session = RwtCompat.ResolveType(ns, "SessionManager", ns + ".Managers.SessionManager");
                MethodInfo spawn = pm == null ? null : AccessTools.Method(pm, "SpawnSingleSettlement");
                ParameterInfo[] ps = spawn?.GetParameters();
                _iconId = ps != null && ps.Length == 1 ? AccessTools.Property(ps[0].ParameterType, "IconID") : null;
                _icons = session == null ? null : AccessTools.Field(session, "AvailableSettlementIcons");
                _loadIcons = session == null ? null : AccessTools.Method(session, "LoadAllFactionIcons");
                if (spawn == null || _icons == null || _iconId == null || _iconId.PropertyType != typeof(int) || !_iconId.CanWrite)
                {
                    KmhLog.Info("Settlement icons: this RWT build draws none - no fallback needed.");
                    return;
                }
                harmony.Patch(spawn, prefix: new HarmonyMethod(typeof(Patch_RwtSettlementIcon)
                    .GetMethod(nameof(ClampIcon), BindingFlags.NonPublic | BindingFlags.Static)));
                KmhLog.Info("Settlement icons: a settlement without one is drawn with RWT's first icon.");
            }
            catch (Exception ex) { KmhLog.Warn($"Settlement icon fallback setup threw: {ex.Message}"); }
        }

        // Not "Prefix": Harmony patches a method by that name on any class it is handed, and this one has no target.
        private static void ClampIcon(object __0)
        {
            try
            {
                if (__0 == null) return;
                int count = IconCount();
                // RWT fills the list on its first menu frame; a join straight after a restart can beat it, leaving it empty.
                if (count == 0 && !_loadTried && _loadIcons != null)
                {
                    _loadTried = true;
                    _loadIcons.Invoke(null, null);
                    count = IconCount();
                    KmhLog.Info($"Settlement icons: RWT had not loaded its icons before the world arrived; loaded {count} now.");
                }
                if (count == 0) return;
                int id = (int)_iconId.GetValue(__0);
                int safe = SafeIconIndex(id, count);
                if (safe != id) _iconId.SetValue(__0, safe);
            }
            catch (Exception) { }
        }

        internal static int SafeIconIndex(int id, int count) => id >= 0 && id < count ? id : 0;

        private static int IconCount() => (_icons?.GetValue(null) as ICollection)?.Count ?? 0;

        // The autopilot reproduces a join that beats RWT's first main-menu frame by emptying the list first.
        internal static int EmptyIconsForTest()
        {
            (_icons?.GetValue(null) as IDictionary)?.Clear();
            _loadTried = false;
            return IconCount();
        }
    }
}
