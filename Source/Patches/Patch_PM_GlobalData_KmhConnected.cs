using HarmonyLib;
using KMHPatch.Diagnostics;
using KMHPatch.SubProtocol;

namespace KMHPatch.Patches
{
    // RWT weirdness: global-data delivery (not login-packet receive) is the point the client is fully synced, so this
    // is our cleanest "connected and ready" signal - reset stale KMH state here and wait passively for kmh.hello.
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

            // Don't dial the KMH API here - wait for kmh.hello, which carries THIS server's advertised api_port + token.
            // Dialing early would pin KmhApiClient.Active to the wrong port when one host runs several KMH servers.
        }
    }
}
