using System;
using System.Reflection;
using KMHPatch.Diagnostics;

namespace KMHPatch.Features.Account
{
    // OpenID proves only a SteamID64, so Global never learns the persona; it is read locally for display and
    // as a username suggestion, and never asserted to Global.
    internal static class KmhSteamPersona
    {
        private static string _cached;
        private static bool _read;

        public static string Name
        {
            get
            {
                if (_read) return _cached;
                _read = true;
                _cached = Read();
                return _cached;
            }
        }

        public static bool Known => Name.Length > 0;

        // Reflected: a copy launched outside Steam has no Steamworks, and the self-test runs with neither.
        private static string Read()
        {
            try
            {
                if (!SteamRunning()) return "";

                Type friends = KmhSteamInterop.Find("Steamworks.SteamFriends");
                MethodInfo get = friends?.GetMethod("GetPersonaName", BindingFlags.Public | BindingFlags.Static,
                                                    null, Type.EmptyTypes, null);
                if (get == null) return "";

                return ((string)get.Invoke(null, null) ?? "").Trim();
            }
            catch (Exception ex)
            {
                KmhLog.Debug($"Account: could not read the Steam persona: {ex.GetType().Name}");
                return "";
            }
        }

        internal static ulong SteamIdForTest;

        // Each read scans every loaded assembly, and Steam cannot switch account under a running game.
        private static ulong _steamId;
        private static DateTime _steamRetryUtc = DateTime.MinValue;

        public static ulong SteamId64()
        {
            if (SteamIdForTest != 0) return SteamIdForTest;
            if (AssumeNoSteamForTest) return 0;
            if (_steamId != 0) return _steamId;
            if (DateTime.UtcNow < _steamRetryUtc) return 0;
            _steamRetryUtc = DateTime.UtcNow.AddSeconds(30);
            _steamId = ReadSteamId();
            return _steamId;
        }

        private static ulong ReadSteamId()
        {
            if (!SteamRunning()) return 0;
            try
            {
                Type user = KmhSteamInterop.Find("Steamworks.SteamUser");
                MethodInfo get = user?.GetMethod("GetSteamID", BindingFlags.Public | BindingFlags.Static);
                object id = get?.Invoke(null, null);
                if (id == null) return 0;

                // CSteamID wraps a ulong; the field name is the only thing that could be reshaped.
                FieldInfo raw = id.GetType().GetField("m_SteamID", BindingFlags.Public | BindingFlags.Instance);
                return raw?.GetValue(id) as ulong? ?? 0;
            }
            catch (Exception ex)
            {
                KmhLog.Debug($"Account: could not read the Steam id: {ex.GetType().Name}");
                return 0;
            }
        }

        internal static bool AssumeNoSteamForTest;

        // Pure, so both answers are asserted directly: testing where Steam is absent passes whether or not it is read.
        internal static bool SteamAvailable(bool assumeNone, bool runtimeSaysRunning)
            => !assumeNone && runtimeSaysRunning;

        private static bool SteamRunning() => SteamAvailable(AssumeNoSteamForTest, RuntimeSteamRunning());

        // Shared with the friends reader: one place decides whether Steamworks is there to talk to.
        internal static bool SteamIsRunning => RuntimeSteamRunning();

        private static bool RuntimeSteamRunning()
        {
            Type manager = KmhSteamInterop.Find("Verse.Steam.SteamManager");
            PropertyInfo initialized = manager?.GetProperty("Initialized", BindingFlags.Public | BindingFlags.Static);
            return initialized != null && initialized.GetValue(null) is bool ok && ok;
        }

        internal static void SeedForTest(string persona)
        {
            _cached = persona ?? "";
            _read = true;
        }

        internal static void ResetForTest() { _cached = null; _read = false; _steamId = 0; _steamRetryUtc = DateTime.MinValue; }
    }
}
