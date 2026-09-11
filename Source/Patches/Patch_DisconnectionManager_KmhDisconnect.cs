using HarmonyLib;
using KMHPatch.Diagnostics;
using KMHPatch.SubProtocol;

namespace KMHPatch.Patches
{
    // Every RWT disconnect routes through here; postfix, not prefix, so RWT's own teardown runs first.
    [HarmonyPatch(typeof(DisconnectionManager), nameof(DisconnectionManager.HandleDisconnect))]
    internal static class Patch_DisconnectionManager_KmhDisconnect
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            if (KmhDispatcher.IsKmhServer)
            {
                KmhLog.Info($"Disconnected from KMH server (was on protocol v{KmhDispatcher.ServerProtocolVersion})");
                // Notify extensions before session state is wiped below.
                Extensibility.KmhClientEventBus.Instance.RaiseKmhServerDisconnected(
                    new KMH.Sdk.Client.Events.KmhServerDisconnectedEvent());
            }
            else
            {
                KmhLog.Info("Disconnected from server");
            }
            KmhDispatcher.ResetSession();
            KmhClientCaches.ClearAll();   // server switch: drop the old server's cached snapshots
            KmhDebugUplink.ResetForNewServer();   // session consent never follows the player to the next server
            KmhDebugConsent.Reset();

            // Applied server configs stay on disk and stay locked until the player restores them or joins a non-enforcing server.
            Features.Enforcement.EnforcementCache.Apply(false, true, false, false, false, null);
            Features.Enforcement.EnforcementProfileApplier.StopWatching();
            Features.Enforcement.EnforcementHandler.ResetConnectionState();
        }
    }
}
