using KMHPatch.Features.ConnectedTreasury.Dto;
using KMHPatch.Notifications;
using KMHPatch.SubProtocol;

namespace KMHPatch.Features.ConnectedTreasury
{
    // Only an amount is sent: the account, operation, silver and completion all live on the server.
    internal static class ConnectedTreasuryHandler
    {
        public static void Register()
            => KmhDispatcher.RegisterHandler(KmhProtocol.Kind.ConnectedSnapshot, OnSnapshot);

        public static bool RequestSnapshot() => KmhDispatcher.Send(KmhProtocol.Kind.ConnectedRequest, null);

        // Written out in full, not built from a scope, so the contract check can match it to the server's scope.
        public static bool TryDeposit(long amount)
            => Move(KmhProtocol.Kind.ConnectedDeposit, KmhOpId.For($"connected.deposit|{amount}"), amount);

        // Global grants, to the player, one spend of exactly this amount on this server; the server can spend only that.
        // Dropped if the player moved on meanwhile, or it would reach the next server.
        public static bool TryWithdraw(long amount)
        {
            if (amount <= 0) { KmhNotifications.Negative("That amount is not allowed."); return false; }
            if (!KmhDispatcher.IsKmhServer) { KmhNotifications.NotConnected(); return false; }
            string opId = KmhOpId.For($"connected.withdraw|{amount}");
            int session = KmhDispatcher.SessionGeneration;
            string serverId = KmhDispatcher.ServerId;
            string dialled = Catalog.CatalogPushGuard.CurrentEndpoint();
            KmhNotifications.Neutral("Asking Colony Nexus Global for your approval…");

            System.Threading.Tasks.Task.Run(async () =>
            {
                (string grant, string error) = await ConnectedGlobalClient.RequestWithdrawGrantAsync(
                    serverId, amount, dialled, System.Threading.CancellationToken.None).ConfigureAwait(false);
                Diagnostics.KmhMainThread.Post(() =>
                {
                    if (!Global.KmhJoinTicketClient.StillSameSession(session, serverId, KmhDispatcher.SessionGeneration, KmhDispatcher.ServerId))
                        return;
                    if (grant.Length == 0) { KmhNotifications.Negative($"Colony Nexus Global did not approve that withdrawal ({error})."); return; }
                    if (KmhDispatcher.Send(KmhProtocol.Kind.ConnectedWithdraw, new { amount, grant }, opId))
                        KmhNotifications.Neutral("Asking the server…");
                    else KmhNotifications.NotConnected();
                });
            });
            return true;
        }

        private static bool Move(string kind, string opId, long amount)
        {
            if (amount <= 0) { KmhNotifications.Negative("That amount is not allowed."); return false; }

            // The server dedups on the op id, so a re-send or double click cannot start two federated operations.
            bool sent = KmhDispatcher.Send(kind, new { amount }, opId);
            if (sent) KmhNotifications.Neutral("Asking the server…");
            else KmhNotifications.NotConnected();
            return sent;
        }

        private static void OnSnapshot(KmhEnvelope env)
        {
            ConnectedSnapshot snapshot = env?.DataAs<ConnectedSnapshot>();
            if (snapshot == null) return;
            ConnectedTreasuryCache.Apply(snapshot);
        }
    }
}
