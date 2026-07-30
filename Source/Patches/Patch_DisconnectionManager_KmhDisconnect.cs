using HarmonyLib;
using KMHPatch.Diagnostics;
using KMHPatch.SubProtocol;

namespace KMHPatch.Patches
{
    // RWT routes every disconnect through HandleDisconnect, so one postfix resets KMH session state for a clean
    // re-handshake. Postfix (not Prefix) so RWT's own teardown runs first.
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
            KmhDebugUplink.ServerRequested = false;   // next server's hello decides again

            // Clear the live snapshot + stop the tamper-revert watcher. Applied server configs stay on disk and
            // STAY LOCKED in the main menu (the lock falls back to the applied-profile file list) until the player
            // restores them from the KMH tab or joins a non-enforcing server
            Features.Enforcement.EnforcementCache.Apply(false, true, false, false, false, null);
            Features.Enforcement.EnforcementProfileApplier.StopWatching();
            Features.Enforcement.EnforcementHandler.ResetConnectionState();
        }
    }
}
