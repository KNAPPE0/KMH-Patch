using System;
using System.Reflection;
using KMHPatch.Diagnostics;
using KMHPatch.Features.Global;

namespace KMHPatch.Features.Discord
{
    // Steam offers Join Game only for its reserved "connect" key. Reflected: outside Steam there is no Steamworks.
    internal static class KmhSteamPresence
    {
        internal const string ConnectKey = "connect";

        // Steam caps a rich presence value at 256 bytes and drops the whole key if it is longer.
        internal const int MaxConnectLength = 256;

        internal const string Prefix = "+kmh ";

        private static string _lastConnect = "";
        internal static string LastConnectForTest => _lastConnect;
        internal static bool SuppressForTest;

        // Same rules as the Discord join: a password or an unlisted server is not ours to hand to a friend.
        internal static string ConnectFor(ServerCardDto card)
        {
            if (card == null || !KmhJoinGate.MayAdvertise(card)) return "";
            string s = Prefix + card.ServerId;
            return s.Length <= MaxConnectLength ? s : "";
        }

        internal static bool TryReadServerId(string connect, out string serverId)
        {
            serverId = "";
            if (string.IsNullOrEmpty(connect) || !connect.StartsWith(Prefix, StringComparison.Ordinal)) return false;
            string id = connect.Substring(Prefix.Length).Trim();
            if (!KmhPresenceJoin.IsIssuedServerId(id)) return false;
            serverId = id;
            return true;
        }

        // Throttled: Tick runs every frame and finding the card walks the whole directory under a lock.
        private static readonly TimeSpan Recheck = TimeSpan.FromSeconds(1);
        private static DateTime _nextCheck = DateTime.MinValue;

        public static void Tick()
        {
            try
            {
                if (DateTime.UtcNow < _nextCheck) return;
                _nextCheck = DateTime.UtcNow + Recheck;

                string wanted = KMHPatchMod.SettingsOrDefault.AllowDiscordJoin && SubProtocol.KmhDispatcher.IsKmhServer
                    ? ConnectFor(KmhRichPresence.CardForCurrentServer())
                    : "";

                if (wanted == _lastConnect) return;
                _lastConnect = wanted;
                if (SuppressForTest) return;

                if (wanted.Length == 0) { Set(ConnectKey, ""); KmhLog.Debug("Steam: Join Game withdrawn."); }
                else if (Set(ConnectKey, wanted)) KmhLog.Debug("Steam: Join Game offered to friends.");
            }
            catch (Exception ex) { KmhLog.Debug($"Steam presence: tick failed ({ex.GetType().Name})."); }
        }

        // An empty value is how Steamworks clears one key without touching the others.
        private static bool Set(string key, string value)
        {
            if (!Account.KmhSteamPersona.SteamIsRunning) return false;
            try
            {
                Type friends = Features.Account.KmhSteamInterop.Find("Steamworks.SteamFriends");
                MethodInfo m = friends?.GetMethod("SetRichPresence", BindingFlags.Public | BindingFlags.Static,
                                                  null, new[] { typeof(string), typeof(string) }, null);
                if (m == null)
                {
                    KmhLog.Debug("Steam: this Steamworks build does not expose SetRichPresence.");
                    return false;
                }
                return m.Invoke(null, new object[] { key, value }) as bool? ?? false;
            }
            catch (Exception ex) { KmhLog.Debug($"Steam presence: could not set '{key}' ({ex.GetType().Name})."); return false; }
        }

        public static void Clear()
        {
            _lastConnect = "";
            if (SuppressForTest) return;
            try
            {
                Type friends = Features.Account.KmhSteamInterop.Find("Steamworks.SteamFriends");
                friends?.GetMethod("ClearRichPresence", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, null);
            }
            catch (Exception) { }
        }

        internal static void ResetForTest() { _lastConnect = ""; _nextCheck = DateTime.MinValue; }
    }
}
