using System.Text;
using HarmonyLib;
using KMHPatch.Diagnostics;
using KMHPatch.SubProtocol;

namespace KMHPatch.Patches
{
    // Inbound side of the sub-protocol: KMH messages are swallowed, and anything else returns true for RWT to display.
    [HarmonyPatch(typeof(PM_Chat), nameof(PM_Chat.Receive))]
    internal static class Patch_PM_Chat_KmhIntercept
    {
        private static bool _seenChat;

        [HarmonyPrefix]
        private static bool Prefix(ServerClient __0, byte[] bytes)
        {
            if (!ReferenceEquals(Network.ServerEndpoint, __0?.Listener)) return false;
            // The whole handshake rides PM_Chat, so proving the hook fires at all is worth one line.
            if (KmhLog.DebugEnabled && !_seenChat) { _seenChat = true; KmhLog.Debug("chat intercept: active - PM_Chat.Receive is hooked."); }

            PKT_Chat pkt;
            try
            {
                pkt = Serializer.ConvertBytesToObject<PKT_Chat>(bytes);
            }
            catch
            {
                if (KmhLog.DebugEnabled) KmhLog.Debug($"chat intercept: PKT_Chat deserialize failed ({bytes?.Length ?? 0} bytes).");
                return true;
            }

            Sender from = pkt == null ? Sender.Player : Classify(pkt.Username, pkt.Message, SessionHandler.Username, KmhDispatcher.StockServerSeen);
            if (from == Sender.OwnEcho)
            {
                bool first = !KmhDispatcher.StockServerSeen;
                KmhDispatcher.MarkStockServer();
                if (first) KmhMainThread.Post(KmhHandshakeHandler.StockServerDetected);
                return false;
            }
            if (from == Sender.Forged) return false;

            if (from == Sender.Player)
            {
                // A KMH-shaped payload under another username means this RWT build mangled the tag in transit.
                if (KmhLog.DebugEnabled && pkt?.Message != null && pkt.Message.Contains("\"kind\""))
                    KmhLog.Debug($"chat intercept: KMH-looking payload, username='{Escape(pkt.Username)}' != system '{Escape(KmhProtocol.SystemUsername)}' - not dispatched.");
                return true;
            }

            KmhEnvelope env = KmhEnvelope.TryParse(pkt.Message);
            if (env == null)
            {
                KmhLog.Warn("Received malformed KMH envelope, swallowing");
                return false;
            }

            // Network thread: re-checking the generation on the main thread keeps an ended session's packet off the next one.
            int gen = KmhDispatcher.SessionGeneration;
            KmhMainThread.Post(() => { if (gen == KmhDispatcher.SessionGeneration) KmhDispatcher.Receive(env); });
            return false;
        }

        internal enum Sender { Player, Server, OwnEcho, Forged }

        // A Colony Nexus server swallows the client's envelopes; a stock RWT server broadcasts them back to everyone, which
        // proves any "server" message on this connection came from a player using the reserved name.
        internal static Sender Classify(string user, string message, string me, bool stockServerSeen)
        {
            if (IsOwnEcho(user, message, me)) return Sender.OwnEcho;
            if (user != KmhProtocol.SystemUsername) return Sender.Player;
            return stockServerSeen ? Sender.Forged : Sender.Server;
        }

        // Relabelled with the sender's own name, or left with the client tag by a server that keeps what it was sent.
        internal static bool IsOwnEcho(string sender, string message, string me)
            => (string.Equals(sender, KmhProtocol.ClientUsername, System.StringComparison.Ordinal)
                || (!string.IsNullOrEmpty(me) && string.Equals(sender, me, System.StringComparison.Ordinal)))
            && message != null && message.StartsWith("{", System.StringComparison.Ordinal)
            && !string.IsNullOrEmpty(KmhEnvelope.TryParse(message)?.Kind);

        // Show zero-width / control / non-ASCII chars so a mangled username tag is visible in the log.
        private static string Escape(string s)
        {
            if (s == null) return "<null>";
            var sb = new StringBuilder(s.Length + 8);
            foreach (char c in s) sb.Append(c < ' ' || c > '~' ? $"\\u{(int)c:x4}" : c.ToString());
            return sb.ToString();
        }
    }
}
