using System;
using KMHPatch.Diagnostics;
using KMHPatch.Features.Chat.Dto;
using KMHPatch.Features.Global;

namespace KMHPatch.Features.Chat
{
    internal static class GlobalChatBridge
    {
        public static void Install()
        {
            GlobalChatClient.Arrived = Deliver;
            GlobalChatClient.Withdrawn = Remove;
        }

        internal static void DropSender(string playerGlobalId)
        {
            if (string.IsNullOrEmpty(playerGlobalId)) return;
            GlobalChatClient.Withdraw(fromUserId: playerGlobalId);
        }

        private static void Remove(GlobalChatEntry entry)
        {
            if (entry == null) return;
            long id = IdFor(entry);
            KmhMainThread.Post(() => ChatCache.RemoveWhere(ChatChannels.Global, m => m.Id == id));
        }

        internal static bool ShouldNotify(bool primingHistory) => !primingHistory;

        private static void Deliver(GlobalChatEntry entry)
        {
            if (entry == null) return;
            // Read now, not in the posted action: by then the load has finished and backlog would look live.
            bool notify = ShouldNotify(GlobalChatClient.PrimingHistory);
            KmhMainThread.Post(() => ChatCache.Append(ToMessage(entry), notify));
        }

        // Ticks, not seconds plus a hash: hashed ids collide, and ChatCache drops a collision as a duplicate.
        internal static long IdFor(GlobalChatEntry entry)
        {
            long ticks = GlobalChatClient.Order(entry);
            // An older Global sends seconds only: spread same-second messages widely, since a collision loses one.
            if (entry.SentAtTicks > 0) return ticks;
            unchecked
            {
                int h = 17;
                foreach (char c in entry.MessageId ?? "") h = h * 31 + c;
                return ticks + (uint)h % TimeSpan.TicksPerSecond;
            }
        }

        internal static ChatMessage ToMessage(GlobalChatEntry entry) => new ChatMessage
        {
            Id           = IdFor(entry),
            Channel      = ChatChannels.Global,
            FromUsername = entry.Username ?? "",
            Body         = entry.Body ?? "",
            SentUtcTicks = DateTimeOffset.FromUnixTimeSeconds(entry.SentAtUnix).UtcDateTime.Ticks,
            // A Discord relay takes the "discord" origin, which drives the marker saying the name is self-chosen.
            Origin       = OriginFor(entry),
            // Only a real account posting as itself is verified; anything a server relayed is a display name.
            SenderVerified = string.IsNullOrEmpty(entry.RelaySource),
            // Blank for relays: a relayed line carries the server owner's id, not the sender's.
            SenderGlobalId = string.IsNullOrEmpty(entry.RelaySource) ? entry.UserId ?? "" : "",
            RelayedBy      = entry.RelaySource == "discord" ? entry.OriginServerName ?? "" : "",
        };

        private static string OriginFor(GlobalChatEntry entry)
        {
            if (entry.RelaySource == "discord") return "discord";
            return string.IsNullOrEmpty(entry.OriginServerName) ? "global" : entry.OriginServerName;
        }
    }
}
