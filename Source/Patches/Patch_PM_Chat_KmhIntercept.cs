using GameClient.PacketManagers;
using HarmonyLib;
using KMHPatch.Diagnostics;
using KMHPatch.SubProtocol;

namespace KMHPatch.Patches
{
    // Inbound side of the KMH sub-protocol.
    //
    // We Prefix PM_Chat.Receive so we can SKIP RWT's normal chat-display path when an incoming message is KMH
    // protocol (identified by Username prefix). Returning false from a Prefix prevents the original method from
    // running
    //
    // For non-KMH chat we return true and RWT proceeds as normal.
    [HarmonyPatch(typeof(PM_Chat), nameof(PM_Chat.Receive))]
    internal static class Patch_PM_Chat_KmhIntercept
    {
        [HarmonyPrefix]
        private static bool Prefix(byte[] bytes)
        {
            PKT_Chat pkt;
            try
            {
                pkt = Serializer.ConvertBytesToObject<PKT_Chat>(bytes);
            }
            catch
            {
                // Deserialization failed - let RWT handle it (or fail in its own path, which will at least surface
                // a useful error message)
                return true;
            }

            if (pkt == null || pkt.Username != KmhProtocol.SystemUsername)
            {
                // Regular chat - defer to RWT.
                return true;
            }

            // KMH protocol message. Parse the envelope, hand to dispatcher, and SWALLOW so the JSON never reaches
            // the visible chat log
            KmhEnvelope env = KmhEnvelope.TryParse(pkt.Message);
            if (env == null)
            {
                KmhLog.Warn("Received malformed KMH envelope, swallowing");
                return false;
            }

            KmhDispatcher.Receive(env);
            return false;
        }
    }
}
