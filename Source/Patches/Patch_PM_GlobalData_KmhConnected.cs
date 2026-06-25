using GameClient.Misc;
using GameClient.PacketManagers;
using HarmonyLib;
using KMHPatch.Diagnostics;
using KMHPatch.SubProtocol;

namespace KMHPatch.Patches
{
    // RWT sends PKT_ServerGlobalData immediately after a successful login, so this postfix is our cleanest "we are
    // fully connected and ready" signal
    //
    // We intentionally don't hook earlier (e.g. login-packet receive) because global-data delivery is what unblocks
    // BypassReadyPackets gating in RWT - before this fires, the client is connected but not fully synced
    //
    // What we do here:
    //   1. Reset any stale KMH state from a previous session
    //   2. Log the connection (with server endpoint for diagnostics)
    //   3. Wait passively for the server to send kmh.hello; if it never does,
    //      IsKmhServer stays false and we behave as a stock RWT client.
    [HarmonyPatch(typeof(PM_GlobalData), nameof(PM_GlobalData.Receive))]
    internal static class Patch_PM_GlobalData_KmhConnected
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            KmhDispatcher.ResetSession();

            string endpoint = string.IsNullOrEmpty(Network.Ip)
                ? "unknown"
                : $"{Network.Ip}:{Network.Port}";

            KmhLog.Info(
                $"Connected to server {endpoint} - waiting for KMH handshake. " +
                $"(If this is a stock RWT server, no handshake will arrive and KMH features stay hidden.)"
            );

            // If opted in, dial the KMH API now - independent of the chat handshake - so KMH can come up even where RWT
            // chat is unavailable. The server authenticates this by matching the username to its live RWT session+IP.
            try
            {
                if (KMHPatchMod.Settings?.UseKmhApiTransport == true)
                {
                    string host = string.IsNullOrEmpty(KMHPatchMod.Settings.KmhApiHostOverride)
                        ? Network.Ip : KMHPatchMod.Settings.KmhApiHostOverride;
                    KmhApiClient.Connect(host, KMHPatchMod.Settings.KmhApiPort, SessionHandler.Username, "",
                        KMHPatchMod.Settings.AllowChatTransportFallback);
                }
            }
            catch (System.Exception ex) { KmhLog.Warn($"KMH API: connect on RWT-connect threw: {ex.Message}"); }
        }
    }
}
