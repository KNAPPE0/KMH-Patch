using KMHPatch.Features.Chat.Dto;
using KMHPatch.Features.LinkedAccounts;
using KMHPatch.Notifications;
using KMHPatch.UI;
using RimWorld;
using Verse;
using Verse.Sound;

namespace KMHPatch.Features.Chat
{
    public enum CommsNotifyLevel { Silent = 0, Toast = 1, ToastSound = 2 }

    // The extra popup/sound layer only; the badge and glow update regardless, in ChatCache.
    internal static class KmhCommsNotifications
    {
        public static void Register() => ChatCache.MessageArrived += OnMessage;

        private static void OnMessage(ChatMessage m)
        {
            KMHPatchSettings s = KMHPatchMod.Settings;
            if (m == null || s == null) return;
            if (KmhSession.Same(m.FromUsername, KmhSession.Me)) return;      // not my own line
            if (s.ChatNotificationsMuted) return;                            // global mute
            if (Comms.CommsFocus.IsViewing(m.Channel)) return;               // already reading this channel, in any window

            // Being named cuts through a silenced channel; the global mute still wins, because that one means leave me alone.
            bool atMe = ChatMentions.MentionsMe(m.Body, KmhSession.Me);
            CommsNotifyLevel level = LevelFor(m.Channel, s);
            if (atMe && level == CommsNotifyLevel.Silent) level = CommsNotifyLevel.Toast;
            if (level == CommsNotifyLevel.Silent) return;

            string who     = LinkedAccountsCache.Format(m.FromUsername);
            string preview = (m.Body != null && m.Body.Length > 80) ? m.Body.Substring(0, 77) + "…" : m.Body;
            string mark    = atMe ? "@ " : "";
            KmhNotifications.Neutral($"{mark}{Label(m.Channel)}{who}: {preview}");

            // Fires on the network thread; audio is main-thread only.
            if (level == CommsNotifyLevel.ToastSound)
                Diagnostics.KmhMainThread.Post(() =>
                {
                    try { SoundDef snd = SoundDefOf.LetterArrive; if (snd != null) snd.PlayOneShotOnCamera(); } catch { }
                });
        }

        private static CommsNotifyLevel LevelFor(string channel, KMHPatchSettings s)
        {
            int raw = ChatChannels.IsDm(channel) ? s.ChatNotifyDm : ChatChannels.IsGuild(channel) ? s.ChatNotifyGuild : s.ChatNotifyServer;
            return (CommsNotifyLevel)(raw < 0 ? 0 : raw > 2 ? 2 : raw);
        }

        private static string Label(string channel)
            => ChatChannels.IsDm(channel) ? "DM · " : ChatChannels.IsGuild(channel) ? "Guild · " : "Chat · ";
    }
}
