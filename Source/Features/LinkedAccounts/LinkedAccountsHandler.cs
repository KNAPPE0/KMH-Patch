using KMHPatch.Diagnostics;
using KMHPatch.Features.LinkedAccounts.Dto;
using KMHPatch.SubProtocol;

namespace KMHPatch.Features.LinkedAccounts
{
    // Receives kmh.linked_accounts.snapshot pushes from the server and applies them to LinkedAccountsCache. Server
    // is expected to push:
    //   - Once during handshake / after login (initial state)
    //   - Whenever a link / unlink happens (so the map stays fresh)
    //
    // RequestSnapshot is exposed for the rare case where a dialog wants a hard refresh, but normal operation
    // doesn't need to poll
    internal static class LinkedAccountsHandler
    {
        public static void Register()
        {
            KmhDispatcher.RegisterHandler(KmhProtocol.Kind.LinkedAccountsSnapshot, OnSnapshot);
        }

        public static bool RequestSnapshot()
        {
            return KmhDispatcher.Send(KmhProtocol.Kind.LinkedAccountsRequest, null);
        }

        private static void OnSnapshot(KmhEnvelope env)
        {
            LinkedAccountsSnapshot snapshot = env?.DataAs<LinkedAccountsSnapshot>();
            if (snapshot == null)
            {
                KmhLog.Warn("LinkedAccounts snapshot envelope had no parseable payload, ignoring");
                return;
            }
            LinkedAccountsCache.Apply(snapshot);
        }
    }
}
