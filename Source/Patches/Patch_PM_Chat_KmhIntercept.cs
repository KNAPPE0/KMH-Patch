using System.Text;
using HarmonyLib;
using KMHPatch.Diagnostics;
using KMHPatch.SubProtocol;

namespace KMHPatch.Patches
{
    // Inbound side of the KMH sub-protocol. Prefix PM_Chat.Receive and SKIP RWT's chat-display path when a message is
    // KMH protocol (identified by the system username); regular chat returns true and RWT proceeds as normal.
    [HarmonyPatch(typeof(PM_Chat), nameof(PM_Chat.Receive))]
    internal static class Patch_PM_Chat_KmhIntercept
    {
        private static bool _seenChat;

        [HarmonyPrefix]
        private static bool Prefix(byte[] bytes)
        {
            // diagnostic: prove the hook fires at all on this RWT build (the whole KMH handshake rides PM_Chat)
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
                // diagnostic: a KMH-shaped payload that didn't match our system username means this RWT build mangled
                // the tag in transit - log the actual username so we can see what changed.
                if (KmhLog.DebugEnabled && pkt?.Message != null && pkt.Message.Contains("\"kind\""))
                    KmhLog.Debug($"chat intercept: KMH-looking payload, username='{Escape(pkt.Username)}' != system '{Escape(KmhProtocol.SystemUsername)}' - not dispatched.");
                return true;
            }

            // KMH protocol message. Parse the envelope, hand to dispatcher, and SWALLOW so the JSON never reaches chat.
            KmhEnvelope env = KmhEnvelope.TryParse(pkt.Message);
            if (env == null)
            {
                KmhLog.Warn("Received malformed KMH envelope, swallowing");
                return false;
            }

            KmhDispatcher.Receive(env);
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
