using GameClient.Managers;
using HarmonyLib;
using KMHPatch.Diagnostics;
using KMHPatch.SubProtocol;

namespace KMHPatch.Patches
{
    // RWT calls DisconnectionManager.HandleDisconnect for every disconnect path (clean exit, connection lost,
    // kicked, etc.), so a single postfix here covers all cases. We log it and reset KMH session state so a
    // subsequent connection re-runs the handshake from a clean slate
    //
    // We use Postfix (not Prefix) so RWT's own disconnect bookkeeping runs first - by the time we execute, the
    // connection is already torn down and SessionHandler state is being cleared
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

            // Clear the live snapshot + stop the tamper-revert watcher. Applied server configs stay on disk and
            // STAY LOCKED in the main menu (the lock falls back to the applied-profile file list) until the player
            // restores them from the KMH tab or joins a non-enforcing server
            Features.Enforcement.EnforcementCache.Apply(false, true, false, false, false, null);
            Features.Enforcement.EnforcementProfileApplier.StopWatching();
            Features.Enforcement.EnforcementHandler.ResetConnectionState();
        }
    }
}
