using KMHPatch.Features.ConnectedGuild.Dto;
using KMHPatch.Notifications;
using KMHPatch.SubProtocol;

namespace KMHPatch.Features.ConnectedGuild
{
    // Only an amount is sent: the guild, rank, operation, silver and completion all live on the server.
    internal static class ConnectedGuildHandler
    {
        public static void Register()
            => KmhDispatcher.RegisterHandler(KmhProtocol.Kind.GuildConnectedSnapshot, OnSnapshot);

        // Also makes the server re-read Global, so opening the window notices a newly demanded re-proof.
        public static bool RequestSnapshot() => KmhDispatcher.Send(KmhProtocol.Kind.GuildConnectedRequest, null);

        // Written out in full, not built from a scope, so the contract check can match it to the server's scope.
        public static bool TryDeposit(long amount)
            => Move(KmhProtocol.Kind.GuildConnectedDeposit, KmhOpId.For($"guild.connected.deposit|{amount}"), amount);

        public static bool TryWithdraw(long amount)
            => Move(KmhProtocol.Kind.GuildConnectedWithdraw, KmhOpId.For($"guild.connected.withdraw|{amount}"), amount);

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
            ConnectedGuildSnapshot snapshot = env?.DataAs<ConnectedGuildSnapshot>();
            if (snapshot == null) return;
            ConnectedGuildCache.Apply(snapshot);
        }
    }
}
