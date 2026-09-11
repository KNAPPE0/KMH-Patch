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
        private static bool Prefix(byte[] bytes)
        {
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

            if (pkt == null || pkt.Username != KmhProtocol.SystemUsername)
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

            // Network thread; the generation captured here and re-checked at the pump keeps a packet from an ending session off the next one's caches.
            int gen = KmhDispatcher.SessionGeneration;
            KmhMainThread.Post(() => { if (gen == KmhDispatcher.SessionGeneration) KmhDispatcher.Receive(env); });
            return false;
        }

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
