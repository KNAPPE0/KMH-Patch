using System;
using System.Reflection;
using KMHPatch.Diagnostics;
using Verse;

namespace KMHPatch.Features.Treasury
{
    // RimWorld Together only offers "Save and quit"; its in-place ForceSave is what turns pending deposits spendable.
    internal static class KmhSaveNow
    {
        internal static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(60);

        private static bool _resolved;
        private static MethodInfo _forceSave;
        private static PropertyInfo _saveName;
        private static DateTime _lastUtc = DateTime.MinValue;

        internal static bool Available
        {
            get
            {
                Resolve();
                return _forceSave != null && RwtCompat.NetworkState == "Connected" && Current.ProgramState == ProgramState.Playing;
            }
        }

        internal static bool CoolingDown(DateTime last, DateTime now) => now - last < Cooldown;

        internal static bool Busy => CoolingDown(_lastUtc, DateTime.UtcNow);

        internal static bool TrySave()
        {
            if (!Available || Busy) return false;
            _lastUtc = DateTime.UtcNow;
            try
            {
                _forceSave.Invoke(null, null);
                KmhLog.Info("Treasury: saving now so pending deposits finalize (RimWorld Together's own save, no quit).");
                return true;
            }
            catch (Exception ex)
            {
                KmhLog.Warn($"Treasury: Save now failed ({(ex.InnerException ?? ex).GetType().Name}: {(ex.InnerException ?? ex).Message}).");
                return false;
            }
        }

        private static void Resolve()
        {
            if (_resolved) return;
            _resolved = true;
            _forceSave = ForceSaveOn(RwtType());
            _saveName = SaveNameOn(RwtType());
        }

        // RimWorld Together's file for this server's save ("RT - address-port - name"), or null outside its session.
        internal static string OnlineSaveNameForTest;

        internal static string OnlineSaveName()
        {
            if (OnlineSaveNameForTest != null) return OnlineSaveNameForTest;
            if (RwtCompat.NetworkState != "Connected" || Current.ProgramState != ProgramState.Playing) return null;
            Resolve();
            return _saveName?.GetValue(null) as string;
        }

        // RimWorld Together uploads its own file after every save, whatever name the save asked for.
        internal static string SaveNameFor(string requested, string online) => string.IsNullOrEmpty(online) ? requested : online;

        internal static PropertyInfo SaveNameOn(Type t)
            => t?.GetProperty("CustomSaveName", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

        internal static Type RwtType()
            => RwtCompat.ResolveType("RTClient", "PM_Saves", "RTClient.PacketManagers.PM_Saves", "GameClient.PacketManagers.PM_Saves");

        // Static and parameterless, as RimWorld Together's console command calls it; anything else is not the same call.
        internal static MethodInfo ForceSaveOn(Type t)
            => t?.GetMethod("ForceSave", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null, Type.EmptyTypes, null);
    }
}
