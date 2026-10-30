using System;
using KMHPatch.Features.Global;
using KMHPatch.SubProtocol;

namespace KMHPatch.Features.Discord
{
    // Derived on read from the facts the presence uses, so the screen cannot disagree with what was sent.
    internal static class KmhDiscordStatus
    {
        public static bool PresenceOn => KMHPatchMod.SettingsOrDefault.DiscordPresenceEnabled
                                      && KmhRichPresence.Active;

        public static string PresenceLine()
        {
            KMHPatchSettings s = KMHPatchMod.SettingsOrDefault;
            if (!s.DiscordPresenceEnabled)
                return UI.KmhText.Get("KMH_Discord_StatusOff", "<color=#A9AFB9>status off in Colony Nexus settings</color>");
            if (!KmhRichPresence.Active)
                return "<color=#A9AFB9>" + UI.KmhDisplayText.Inert(KmhRichPresence.LastState) + "</color>";
            if (!KmhDiscordRpc.Connected)
                return UI.KmhText.Get("KMH_Discord_StatusWaiting", "<color=#A9AFB9>waiting for Discord - the status appears once Discord is running</color>");

            KmhDiscordRpc.Presence p = KmhRichPresence.LastSent;
            if (p == null || string.IsNullOrEmpty(p.Details)) return UI.KmhText.Get("KMH_Discord_StatusConnected", "connected");
            string shown = string.IsNullOrEmpty(p.State) ? p.Details : p.Details + " - " + p.State;
            return UI.KmhText.Get("KMH_Discord_StatusShowing", "showing \"{0}\"", UI.KmhDisplayText.Inert(shown));
        }

        public static AdvertiseBlock JoinBlock()
        {
            KMHPatchSettings s = KMHPatchMod.SettingsOrDefault;
            if (!s.DiscordPresenceEnabled || !KmhRichPresence.Active) return AdvertiseBlock.PresenceOff;
            if (!s.AllowDiscordJoin) return AdvertiseBlock.JoinsOff;
            if (!KmhDispatcher.IsKmhServer) return AdvertiseBlock.NotOnServer;
            // Without the URI scheme DiscordRPC refuses the whole presence, so the secret is dropped, not the status.
            if (!KmhRichPresence.JoinAdvertisable) return AdvertiseBlock.NoUriScheme;

            // Cached: this walks the whole directory under a lock and the dashboard asks every frame.
            if (DateTime.UtcNow >= _cardBlockUntil)
            {
                _cardBlockUntil = DateTime.UtcNow + CardRecheck;
                _cardBlock = KmhJoinGate.WhyNotAdvertised(KmhRichPresence.CardForCurrentServer());
            }
            return _cardBlock;
        }

        private static readonly TimeSpan CardRecheck = TimeSpan.FromSeconds(1);
        private static DateTime _cardBlockUntil = DateTime.MinValue;
        private static AdvertiseBlock _cardBlock = AdvertiseBlock.NotListed;

        internal static void ResetForTest()
        {
            _cardBlockUntil = DateTime.MinValue;
            _cardBlock = AdvertiseBlock.NotListed;
        }

        public static string JoinLine() => LineFor(JoinBlock());

        public static string LineFor(AdvertiseBlock block)
        {
            switch (block)
            {
                case AdvertiseBlock.None:
                    return UI.KmhText.Get("KMH_Discord_JoinOn",
                        "<color=#7FC08A>offered</color> - invite from Discord with the + beside the message box");
                case AdvertiseBlock.PresenceOff:
                    return UI.KmhText.Get("KMH_Discord_JoinNoPresence", "<color=#A9AFB9>needs the Discord status on</color>");
                case AdvertiseBlock.JoinsOff:
                    return UI.KmhText.Get("KMH_Discord_JoinOff", "<color=#A9AFB9>turned off in Colony Nexus settings</color>");
                case AdvertiseBlock.NotOnServer:
                    return UI.KmhText.Get("KMH_Discord_JoinNoServer", "<color=#A9AFB9>join a Colony Nexus server first</color>");
                case AdvertiseBlock.NotListed:
                    return UI.KmhText.Get("KMH_Discord_JoinNotListed",
                        "<color=#f0c04f>this server is not publicly listed, so it cannot be joined from Discord</color>");
                case AdvertiseBlock.PasswordRequired:
                    return UI.KmhText.Get("KMH_Discord_JoinLocked",
                        "<color=#f0c04f>this server needs a password, so it cannot be joined from Discord</color>");
                case AdvertiseBlock.NoAddress:
                    return UI.KmhText.Get("KMH_Discord_JoinNoAddress",
                        "<color=#f0c04f>this server publishes no address to connect to</color>");
                case AdvertiseBlock.NoUriScheme:
                    return UI.KmhText.Get("KMH_Discord_JoinNoScheme",
                        "<color=#f0c04f>Discord cannot launch RimWorld on this machine - restart the game with joins on</color>");
                default:
                    return "";
            }
        }

        // The RPC has no invite call, so the game can only say where Discord's own button is.
        public static string InviteHint()
            => UI.KmhText.Get("KMH_Discord_InviteHint",
                "Discord sends the invite, not Colony Nexus: open a Discord channel, press + beside the message box and "
              + "choose Invite to play. Whoever accepts needs RimWorld and Colony Nexus installed.");
    }
}
