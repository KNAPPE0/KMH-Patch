using HarmonyLib;
using KMHPatch.Diagnostics;
using KMHPatch.SubProtocol;

namespace KMHPatch.Patches
{
    // Global-data delivery, not login-packet receive, is the point RWT's client is actually fully synced.
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

            // The API is dialled only from kmh.hello: dialling here would pin the wrong port when one host runs several servers.
        }
    }
}
