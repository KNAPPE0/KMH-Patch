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

    internal static class KmhCommsNotifications
    {
        public static void Register() => ChatCache.MessageArrived += OnMessage;

        private static void OnMessage(ChatMessage m)
        {
            KMHPatchSettings s = KMHPatchMod.Settings;
            if (m == null || s == null) return;
            if (KmhSession.Same(m.FromUsername, KmhSession.Me)) return;
            if (s.ChatNotificationsMuted) return;
            if (Comms.CommsFocus.IsViewing(m.Channel)) return;

            // Being named cuts through a silenced channel; the global mute still wins, since it means "leave me alone".
            bool atMe = ChatMentions.MentionsMe(m.Body, KmhSession.Me);
            CommsNotifyLevel level = LevelFor(m.Channel, s);
            if (atMe && level == CommsNotifyLevel.Silent) level = CommsNotifyLevel.Toast;
            if (level == CommsNotifyLevel.Silent) return;

            // The overlay already shows the line; a toast would only double it.
            if (ToastWanted(atMe, ChatOverlay.WillShow(m.Channel)))
            {
                bool global    = ChatChannels.IsGlobal(m.Channel);
                string who     = global ? GlobalWho(m) : LinkedAccountsCache.Format(m.FromUsername);
                string preview = ChatOverlay.OneLine(m.Body, 80);
                string mark    = atMe ? "@ " : "";
                string line    = $"{mark}{Label(m.Channel)}{who}: {preview}";
                // Global speaks as Colony Nexus; everything else came through this server and wears its name.
                if (global) KmhNotifications.Neutral(line);
                else KmhNotifications.FromServer(line);
            }

            // Fires on the network thread; audio is main-thread only.
            if (level == CommsNotifyLevel.ToastSound)
                Diagnostics.KmhMainThread.Post(() =>
                {
                    try { SoundDef snd = SoundDefOf.LetterArrive; if (snd != null) snd.PlayOneShotOnCamera(); } catch { }
                });
        }

        internal static bool ToastWanted(bool atMe, bool overlayShows) => atMe || !overlayShows;

        // A Global name is a Colony Nexus account: no server badge or recasing, and a relayed or Discord name says so.
        internal static string GlobalWho(ChatMessage m)
        {
            string name = UI.KmhDisplayText.Inert(m.FromUsername ?? "");
            if (m.FromDiscord) return (m.UnverifiedSender ? "[D?] " : "[D] ") + name;
            return m.SenderVerified ? name : "[relayed] " + name;
        }

        internal static CommsNotifyLevel LevelFor(string channel, KMHPatchSettings s)
        {
            int raw = ChatChannels.IsDm(channel)     ? s.ChatNotifyDm
                    : ChatChannels.IsGuild(channel)  ? s.ChatNotifyGuild
                    : ChatChannels.IsGlobal(channel) ? s.ChatNotifyGlobal
                    :                                  s.ChatNotifyServer;
            return (CommsNotifyLevel)(raw < 0 ? 0 : raw > 2 ? 2 : raw);
        }

        internal static string Label(string channel)
            => ChatChannels.IsDm(channel)     ? "DM · "
             : ChatChannels.IsGuild(channel)  ? "Guild · "
             : ChatChannels.IsGlobal(channel) ? "Global · "
             :                                  "Chat · ";
    }
}
