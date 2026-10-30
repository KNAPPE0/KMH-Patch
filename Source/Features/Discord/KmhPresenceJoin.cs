using System;
using System.Threading.Tasks;
using KMHPatch.Diagnostics;
using KMHPatch.Features.Global;
using KMHPatch.Notifications;
using KMHPatch.UI;
using Verse;

namespace KMHPatch.Features.Discord
{
    // The secret is another player's claim carrying only a ServerId: the address comes from this client's directory
    // and every condition is re-checked here, since a secret with host:port could send a player anywhere.
    internal static class KmhPresenceJoin
    {
        internal const string Prefix = "kmh1:";

        // Discord delivers one event per click, and a player who clicks twice must not open two connections.
        internal static readonly TimeSpan DuplicateWindow = TimeSpan.FromSeconds(10);
        // One authorised action at a time: a second invite must not race a connection already starting.
        internal static readonly TimeSpan ConnectGuard = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan RefreshTimeout = TimeSpan.FromSeconds(8);

        private static string _lastSecret = "";
        private static DateTime _lastAcceptUtc = DateTime.MinValue;
        private static DateTime _connectingUntil = DateTime.MinValue;
        private static bool _confirmOpen;

        public static string Secret(ServerCardDto card)
            => card == null || string.IsNullOrEmpty(card.ServerId) ? "" : Prefix + card.ServerId;

        internal static bool TryReadServerId(string secret, out string serverId)
        {
            serverId = "";
            if (string.IsNullOrEmpty(secret) || !secret.StartsWith(Prefix, StringComparison.Ordinal)) return false;
            string id = secret.Substring(Prefix.Length).Trim();
            if (!IsIssuedServerId(id)) return false;
            serverId = id;
            return true;
        }

        // The shape the directory issues, so a crafted secret cannot become a lookup key for anything else.
        internal static bool IsIssuedServerId(string id)
        {
            if (id.Length != 32) return false;
            foreach (char c in id)
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return false;
            return true;
        }

        public static void Accept(string secret)
        {
            // Captured on the callback, so the join stays tied to the presence and session Discord delivered it against.
            int epoch = KmhRichPresence.Epoch;
            int generation = SubProtocol.KmhDispatcher.SessionGeneration;
            // Off the Discord callback thread: everything below touches RimWorld state.
            KmhMainThread.Post(() => Run(secret, epoch, generation, attempt: 0));
        }

        // A join Discord delivered against a presence KMH has since replaced belongs to a session that is gone.
        internal static bool IsStale(int deliveredEpoch) => deliveredEpoch != KmhRichPresence.Epoch;

        // The player joined a server while this invite was being checked; the older invite loses, not their session.
        internal static bool SessionMovedOn(int deliveredGeneration, int nowGeneration, bool onKmhServer)
            => deliveredGeneration != nowGeneration && onKmhServer;

        internal static bool ConnectInFlight(DateTime nowUtc) => nowUtc < _connectingUntil;

        internal static bool LeavingAnotherServer(string targetServerId)
            => SubProtocol.KmhDispatcher.IsKmhServer
               && !string.IsNullOrEmpty(targetServerId)
               && !string.Equals(SubProtocol.KmhDispatcher.ServerId, targetServerId, StringComparison.Ordinal);

        internal static bool IsDuplicate(string secret, DateTime nowUtc)
            => !string.IsNullOrEmpty(secret)
               && string.Equals(secret, _lastSecret, StringComparison.Ordinal)
               && nowUtc - _lastAcceptUtc < DuplicateWindow;

        private static void Run(string secret, int epoch, int generation, int attempt)
        {
            try
            {
                if (IsStale(epoch))
                {
                    KmhLog.Debug("Discord: a join arrived against a presence KMH has already replaced - ignored.");
                    return;
                }
                if (SessionMovedOn(generation, SubProtocol.KmhDispatcher.SessionGeneration,
                                   SubProtocol.KmhDispatcher.IsKmhServer))
                {
                    KmhLog.Debug("Discord: the player joined a server while KMH was checking this invite - not interrupting it.");
                    return;
                }
                if (_confirmOpen) return;
                if (ConnectInFlight(DateTime.UtcNow)) { KmhLog.Debug("Discord: a join is already connecting - ignored."); return; }

                if (attempt == 0)
                {
                    DateTime now = DateTime.UtcNow;
                    if (IsDuplicate(secret, now)) { KmhLog.Debug("Discord: duplicate join ignored."); return; }
                    _lastSecret = secret;
                    _lastAcceptUtc = now;
                }

                if (!TryReadServerId(secret, out string serverId))
                {
                    KmhLog.Warn("Discord: a join secret arrived in a shape KMH does not issue - ignored.");
                    return;
                }

                ServerCardDto card = Lookup(serverId);
                bool fresh = KmhJoinGate.DirectoryIsFresh(KmhDirectoryClient.LastRefreshUtc,
                                                          KmhDirectoryClient.ShowingCache, DateTime.UtcNow);

                // Refresh once: a cached row or an unfinished mod comparison cannot answer "can I join now".
                if (attempt == 0 && (!fresh || card == null || KmhCompatibility.Evaluate(card) == CompatState.Checking))
                {
                    if (card != null) ModProfileClient.EnsureFetched(card);
                    KmhNotifications.Neutral(KmhText.Get("KMH_Presence_JoinChecking",
                        "Checking that server with Colony Nexus Global…"));
                    RefreshThenRetry(secret, epoch, generation);
                    return;
                }

                JoinDecision decision = KmhJoinGate.MayJoin(card, fresh);
                if (!decision.Allowed) { KmhNotifications.Rejected(decision.Explain(card)); return; }

                Confirm(card);
            }
            catch (Exception ex) { KmhLog.Warn($"Discord: join failed ({ex.GetType().Name}: {ex.Message})"); }
        }

        private static void RefreshThenRetry(string secret, int epoch, int generation)
        {
            Task.Run(async () =>
            {
                await KmhDirectoryClient.RefreshNowAsync(RefreshTimeout).ConfigureAwait(false);
                // Both tokens carried back: whatever could have revoked this invite meanwhile is re-asked on the main thread.
                KmhMainThread.Post(() => Run(secret, epoch, generation, attempt: 1));
            });
        }

        // Nothing connects without the player's yes: the click came from someone else's status in another app.
        private static void Confirm(ServerCardDto card)
        {
            string name = string.IsNullOrEmpty(card.Name)
                ? KmhText.Get("KMH_Presence_JoinUnnamed", "this server") : card.Name;
            string text = KmhText.Get("KMH_Presence_JoinConfirm", "Join {0}?", name)
                        + "\n\n" + KmhText.Get("KMH_Presence_JoinConfirmBody",
                            "A Discord invite asked RimWorld to connect you. Colony Nexus checked this server against your "
                            + "own server list, not against anything the invite claimed.")
                        + "\n\n" + card.JoinEndpoint;

            // Confirming leaves the current server; say so, so the target's name is not read as the current one.
            if (LeavingAnotherServer(card.ServerId))
                text += "\n\n" + KmhText.Get("KMH_Presence_JoinLeaves",
                    "You are on another server right now. Joining will disconnect you from it.");

            text += ModNotice(card);

            _confirmOpen = true;
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                text,
                () => { _confirmOpen = false; Connect(card); },
                () => { _confirmOpen = false; }));
        }

        // Gate-blocked cases never reach here; this tells an invitee what the server will change before they connect.
        internal static string ModNotice(ServerCardDto card)
        {
            if (card == null || ModPolicyNames.EnforcesNothing(card.ModPolicy)) return "";

            var sb = new System.Text.StringBuilder();
            sb.Append("\n\n").Append(KmhText.Get("KMH_Presence_JoinEnforces",
                "This server sets its own mod list. Colony Nexus asks before changing anything, and offers to put your "
              + "own list back from the main menu afterwards."));

            System.Collections.Generic.List<string> details = KmhCompatibility.Details(card);
            foreach (string line in details) sb.Append('\n').Append(line);
            return sb.ToString();
        }

        private static void Connect(ServerCardDto card)
        {
            try
            {
                // Re-asked after the dialog sat open; freshness is not, since the refresh earned this dialog.
                JoinDecision again = KmhJoinGate.MayJoin(card, directoryFresh: true);
                if (!again.Allowed) { KmhNotifications.Rejected(again.Explain(card)); return; }

                if (!card.TrySplitEndpoint(out string host, out int port))
                {
                    KmhNotifications.Rejected(KmhText.Get("KMH_Presence_JoinNoAddress",
                        "That server publishes no address to connect to."));
                    return;
                }

                // Claimed before connecting, so a second invite arriving mid-connect cannot start another one.
                _connectingUntil = DateTime.UtcNow.Add(ConnectGuard);

                // Recorded before connecting, so a server that refuses at the door is still in Recent to try again.
                Features.Servers.SeenServersStore.Record(card.ServerId, card.Name, card.JoinEndpoint,
                                                         card.KmhVersion, card.RwtVersion);
                KmhLog.Debug($"Discord: joining {card.Name} at {host}:{port} from a Discord invite.");
                if (!RwtCompat.ConnectTo(host, port, card.ServerId))
                    KmhNotifications.Rejected(KmhText.Get("KMH_Presence_JoinFailed",
                        "Could not start the connection. Try joining from the Colony Nexus server browser."));
            }
            catch (Exception ex) { KmhLog.Warn($"Discord: connecting from a Discord invite failed ({ex.GetType().Name})"); }
        }

        private static ServerCardDto Lookup(string serverId)
        {
            foreach (ServerCardDto c in KmhDirectoryClient.Cards)
                if (string.Equals(c.ServerId, serverId, StringComparison.Ordinal)) return c;
            return null;
        }

        internal static void ResetForTest()
        {
            _lastSecret = ""; _lastAcceptUtc = DateTime.MinValue; _confirmOpen = false;
            _connectingUntil = DateTime.MinValue;
        }

        internal static void SeenForTest(string secret, DateTime atUtc) { _lastSecret = secret; _lastAcceptUtc = atUtc; }

        internal static void ConnectingForTest(DateTime untilUtc) { _connectingUntil = untilUtc; }
    }
}
