using HarmonyLib;
using KMHPatch.Features.Enforcement;

namespace KMHPatch.Patches
{
    // Send runs after RWT sets ServerEndpoint, pre-auth; 26.10.20.1+ dropped its ServerClient arg, so key on the endpoint.
    [HarmonyPatch(typeof(PM_Version), nameof(PM_Version.Send))]
    internal static class Patch_PM_Version_KmhConnection
    {
        [HarmonyPrefix]
        private static void Prefix()
        {
            object endpoint = Network.ServerEndpoint;
            if (endpoint == null) return;
            // Queued before the server can answer Version; session UI/cache changes belong on RWT's main thread.
            MainThreadHandler.Instance.Enqueue(() =>
            {
                if (ReferenceEquals(Network.ServerEndpoint, endpoint)) EnforcementAdmission.StartConnection(endpoint);
            });
        }
    }
}
