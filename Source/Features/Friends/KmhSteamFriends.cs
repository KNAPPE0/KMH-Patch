using System;
using System.Collections.Generic;
using System.Reflection;
using KMHPatch.Diagnostics;

namespace KMHPatch.Features.Friends
{
    // Read by reflection: a copy launched outside Steam has no Steamworks assembly and a direct reference would fail
    // to load. Only ids leave this class - a persona is someone else's name to disclose and the matching is on the id.
    internal static class KmhSteamFriends
    {
        // Steamworks' EFriendFlags.Immediate - the actual friends list, not blocked, requested or clan members.
        private const int FriendFlagImmediate = 0x04;

        // A Steam account caps out near 2000 friends; past that something is wrong with what was read.
        internal const int MaxRead = 2500;

        // Set by the suite so the no-Steam path can be exercised on a machine that does have Steam.
        internal static bool AssumeNoSteamForTest;

        // Pure: asserting "nothing without Steam" on a Steam-less machine would prove only that it has no Steam.
        internal static bool CanRead(bool assumeNone, bool runtimeSaysRunning)
            => !assumeNone && runtimeSaysRunning;

        public static bool Available => CanRead(AssumeNoSteamForTest, Account.KmhSteamPersona.SteamIsRunning);

        // Empty, not null, on failure: an unreadable list just means nothing to match.
        public static List<ulong> Read()
        {
            List<ulong> ids;
            TryRead(out ids);
            return ids;
        }

        // Says whether it worked: an empty list must still reach Global, as it clears the stored evidence.
        public static bool TryRead(out List<ulong> ids)
        {
            ids = new List<ulong>();
            if (!Available) return false;

            try
            {
                Type friends = Features.Account.KmhSteamInterop.Find("Steamworks.SteamFriends");
                if (friends == null) return false;

                MethodInfo count = IntOrEnumMethod(friends, "GetFriendCount", 1);
                MethodInfo byIndex = IntOrEnumMethod(friends, "GetFriendByIndex", 2);
                if (count == null || byIndex == null)
                {
                    // Named, because a Steamworks reshuffle is the one thing that breaks this silently.
                    KmhLog.Debug("Steam friends: this Steamworks build does not expose the calls this reads.");
                    return false;
                }

                int n = Convert.ToInt32(count.Invoke(null, new[] { Arg(count, 0, FriendFlagImmediate) }));
                if (n <= 0) return true;   // genuinely no friends, which is a fact worth sending
                if (n > MaxRead) n = MaxRead;

                object flags = Arg(byIndex, 1, FriendFlagImmediate);
                for (int i = 0; i < n; i++)
                {
                    object steamId = byIndex.Invoke(null, new[] { Arg(byIndex, 0, i), flags });
                    ulong raw = UnwrapSteamId(steamId);
                    if (raw != 0) ids.Add(raw);
                }
            }
            catch (Exception ex)
            {
                KmhLog.Debug($"Steam friends: could not read the list ({ex.GetType().Name}).");
                ids.Clear();
                return false;
            }
            return true;
        }

        // Steamworks.NET types the flags as its EFriendFlags enum, so an (int)-only lookup finds nothing.
        internal static MethodInfo IntOrEnumMethod(Type type, string name, int args)
        {
            foreach (MethodInfo m in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (m.Name != name) continue;
                ParameterInfo[] ps = m.GetParameters();
                if (ps.Length != args) continue;
                bool ok = true;
                foreach (ParameterInfo p in ps) if (p.ParameterType != typeof(int) && !p.ParameterType.IsEnum) ok = false;
                if (ok) return m;
            }
            return null;
        }

        internal static object Arg(MethodInfo m, int index, int value)
        {
            Type t = m.GetParameters()[index].ParameterType;
            return t.IsEnum ? Enum.ToObject(t, value) : (object)value;
        }

        // CSteamID wraps a ulong in m_SteamID; read by name since the type is only reachable by reflection.
        internal static ulong UnwrapSteamId(object cSteamId)
        {
            if (cSteamId == null) return 0;
            if (cSteamId is ulong direct) return direct;

            FieldInfo field = cSteamId.GetType().GetField("m_SteamID", BindingFlags.Public | BindingFlags.Instance);
            if (field != null && field.GetValue(cSteamId) is ulong fromField) return fromField;

            PropertyInfo prop = cSteamId.GetType().GetProperty("m_SteamID", BindingFlags.Public | BindingFlags.Instance);
            if (prop != null && prop.GetValue(cSteamId) is ulong fromProp) return fromProp;
            return 0;
        }
    }
}
