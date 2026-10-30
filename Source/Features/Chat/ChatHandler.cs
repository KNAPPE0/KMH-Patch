using System;
using KMHPatch.Diagnostics;
using KMHPatch.Features.Chat.Dto;
using KMHPatch.Notifications;
using KMHPatch.SubProtocol;

namespace KMHPatch.Features.Chat
{
    // Each send carries a fresh token so a retry or reconnect cannot post the same line twice.
    internal static class ChatHandler
    {
        public static void Register()
        {
            KmhDispatcher.RegisterHandler(KmhProtocol.Kind.ChatSnapshot,   OnSnapshot);
            KmhDispatcher.RegisterHandler(KmhProtocol.Kind.ChatMessage,    OnMessage);
            KmhDispatcher.RegisterHandler(KmhProtocol.Kind.ChatModeration, OnModeration);
            ChatMediaClient.Register();
            ChatMediaRefreshClient.Register();
            ChatVideoServer.Register();
            ChatRosterCache.Register();
            ChatOverlay.Register();
        }

        public static bool RequestSnapshot(string channel = ChatCache.ServerChannel)
            => KmhDispatcher.Send(KmhProtocol.Kind.ChatRequest, new { channel = channel ?? ChatCache.ServerChannel });

        public static bool RequestModeration() => KmhDispatcher.Send(KmhProtocol.Kind.ChatModerationReq, null);

        public static bool TryBlock(string username, bool on)
            => !string.IsNullOrEmpty(username) && KmhDispatcher.Send(KmhProtocol.Kind.ChatBlock, new { username, on });

        public static bool TryRemove(string channel, long id)
            => id > 0 && KmhDispatcher.Send(KmhProtocol.Kind.ChatRemove, new { channel = channel ?? "", id });

        public static bool TrySend(string channel, string body)
        {
            body = (body ?? "").Trim();
            if (body.Length == 0) return false;

            // Global Chat bypasses the server transport so it works from the main menu and survives server outages.
            if (ChatChannels.IsGlobal(channel))
            {
                if (Features.Global.GlobalChatClient.Send(body)) return true;
                string why = Features.Global.GlobalChatClient.WhyCannotSend();
                KmhNotifications.Rejected(why.Length > 0 ? why : "Global Chat is not ready yet.");
                return false;
            }

            bool sent = KmhDispatcher.Send(KmhProtocol.Kind.ChatSend, new
            {
                channel = channel ?? ChatCache.ServerChannel,
                body,
                token = Guid.NewGuid().ToString("N"),
            });
            if (!sent) KmhNotifications.NotConnected();
            return sent;
        }

        private static void OnSnapshot(KmhEnvelope env)
        {
            ChatSnapshot snap = env?.DataAs<ChatSnapshot>();
            if (snap == null || string.IsNullOrEmpty(snap.Channel)) { KmhLog.Warn("Chat snapshot had no parseable payload, ignoring"); return; }
            if (GlobalOwned(snap.Channel)) return;
            ChatCache.ApplySnapshot(snap.Channel, snap.Messages);
        }

        private static void OnMessage(KmhEnvelope env)
        {
            ChatMessagePush push = env?.DataAs<ChatMessagePush>();
            if (push?.Message == null || GlobalOwned(push.Message.Channel)) return;
            ChatCache.Append(push.Message);
        }

        // Global's channels come only from Global; a game server writing into them would be speaking as Global.
        internal static bool GlobalOwned(string channel)
        {
            if (!ChatChannels.IsGlobal(channel) && !ChatChannels.IsGlobalDm(channel)) return false;
            KmhLog.Warn("Chat: dropped a line the game server sent on a Global channel.");
            return true;
        }

        private static void OnModeration(KmhEnvelope env)
        {
            ChatModerationSnapshot snap = env?.DataAs<ChatModerationSnapshot>();
            if (snap == null) return;
            ChatModerationCache.Apply(snap.Blocked, snap.BlockingEnabled);
        }
    }
}
