using System;
using KMHPatch.Features.Global;

namespace KMHPatch.Features.Discord
{
    // Built on the main thread, read by the pure Compose below.
    internal sealed class PresenceSession
    {
        public bool Playing;
        public bool Multiplayer;
        public bool KmhServer;
        public ServerCardDto Card;          // only a card from the public directory for the address dialled
        public string PrivateServerName = "";
        public string JoinServerName = "";
        public bool JoinServerPublic;
        public int Colonists = -1;
        public int Day = -1;
        public DateTime StartedUtc;
        public bool JoinAdvertisable;
    }

    internal static class KmhPresenceFormat
    {
        public static KmhDiscordRpc.Presence Compose(PresenceChoice choice, PresenceSession session, KMHPatchSettings s,
                                                     int seed, DateTime nowUtc)
        {
            var p = new KmhDiscordRpc.Presence
            {
                LargeImageKey = KmhRichPresence.LargeAsset,
                LargeImageText = KmhRichPresence.AppName,
                StartedUtc = s.DiscordShowElapsed && session.StartedUtc != default ? session.StartedUtc : (DateTime?)null,
            };
            choice = choice ?? new PresenceChoice();
            bool mp = session.Multiplayer && s.DiscordShowMultiplayer;

            if (!session.Playing || PresenceKinds.IsPhase(choice.Kind) && choice.Kind != PresenceKind.Disconnected)
            {
                PresenceKind phase = PresenceKinds.IsPhase(choice.Kind) ? choice.Kind : PresenceKind.MainMenu;
                // Without consent to show multiplayer, joining a server reads as loading.
                if (!s.DiscordShowMultiplayer && phase != PresenceKind.MainMenu) phase = PresenceKind.Loading;
                p.Details = Line(KmhPresenceCatalog.Details(phase, null), seed, phase, choice, nowUtc);
                if (phase == PresenceKind.Connecting || phase == PresenceKind.PreparingMods)
                    p.State = session.JoinServerPublic && s.DiscordShowServerName
                        ? KmhDiscordRpc.Trim(UI.KmhDisplayText.Inert(session.JoinServerName), 128) : "";
                return p;
            }

            string server = mp ? ServerLine(session, s) : null;
            bool detailed = s.DiscordShowGameplay && s.DiscordDetailedActivity;
            PresenceKind k = choice.Kind;
            // Server-only activities would say "multiplayer" by themselves.
            if (k == PresenceKind.Disconnected && !s.DiscordShowMultiplayer) k = PresenceKind.None;
            if (!mp && (k == PresenceKind.ServerScreen || k == PresenceKind.ServerEvent)) k = PresenceKind.None;

            if (k == PresenceKind.Disconnected)
            {
                p.Details = Line(KmhPresenceCatalog.Details(k, null), seed, k, choice, nowUtc);
                return p;
            }

            if (!detailed || k == PresenceKind.None)
            {
                if (mp)
                {
                    p.Details = Line(MultiplayerLines(), seed, PresenceKind.None, choice, nowUtc);
                    p.State = server ?? "";
                    Badge(p, session, PresenceTier.Normal);
                    Party(p, session);
                }
                else
                {
                    p.Details = UI.KmhText.Get("KMH_RP_InColony", "In a colony");
                    p.State = session.Multiplayer ? "" : UI.KmhText.Get("KMH_RP_Singleplayer", "Singleplayer");
                }
                AddColonyInfo(p, session, s, mp);
                return p;
            }

            p.Details = Line(KmhPresenceCatalog.Details(k, choice.Label), seed, k, choice, nowUtc);
            string sub = !string.IsNullOrEmpty(choice.Sub) ? choice.Sub
                       : choice.WithAllies ? KmhPresenceCatalog.AlliesLine()
                       : Line(KmhPresenceCatalog.States(k), seed ^ 0x5bd1, k, choice, nowUtc);

            PresenceIcon icon = KmhPresenceCatalog.IconFor(k);
            if (mp)
            {
                p.State = server ?? sub;
                Badge(p, session, choice.Tier);
                if (choice.Tier > PresenceTier.Normal || p.SmallImageKey.Length == 0)
                {
                    p.SmallImageKey = KmhPresenceCatalog.AssetFor(icon);
                    p.SmallImageText = KmhDiscordRpc.Trim(sub.Length > 0 ? sub : KmhPresenceCatalog.IconText(icon), 128);
                }
                Party(p, session);
            }
            else
            {
                p.State = sub;
                p.SmallImageKey = KmhPresenceCatalog.AssetFor(icon);
                p.SmallImageText = KmhPresenceCatalog.IconText(icon);
            }
            AddColonyInfo(p, session, s, mp);
            return p;
        }

        private static string[] MultiplayerLines() => new[]
        {
            UI.KmhText.Get("KMH_RP_Mp1", "Playing Colony Nexus multiplayer"),
            UI.KmhText.Get("KMH_RP_Mp2", "Surviving with friends"),
            UI.KmhText.Get("KMH_RP_Mp3", "Playing on a Colony Nexus server"),
        };

        private static string Line(string[] lines, int seed, PresenceKind k, PresenceChoice choice, DateTime nowUtc)
            => KmhDiscordRpc.Trim(KmhPresenceCatalog.Pick(lines, seed ^ ((int)k * 7919), choice.SinceUtc, nowUtc,
                                                          KmhPresenceCatalog.VariantEvery(choice.Tier)), 128);

        // A public server's name is already listed; a private one is never named unless the player opted in.
        internal static string ServerLine(PresenceSession session, KMHPatchSettings s)
        {
            if (!session.KmhServer) return UI.KmhText.Get("KMH_RP_RwtServer", "RimWorld Together multiplayer");
            if (session.Card != null)
                return s.DiscordShowServerName && !string.IsNullOrEmpty(session.Card.Name)
                    ? KmhDiscordRpc.Trim(UI.KmhDisplayText.Inert(session.Card.Name), 128)
                    : UI.KmhText.Get("KMH_RP_KmhServer", "Colony Nexus multiplayer");
            return s.DiscordShowServerName && s.DiscordShowPrivateServerName && !string.IsNullOrEmpty(session.PrivateServerName)
                ? KmhDiscordRpc.Trim(UI.KmhDisplayText.Inert(session.PrivateServerName), 128)
                : UI.KmhText.Get("KMH_Presence_Private", "On a private server");
        }

        // The official badge says "official" and only an official server may carry it.
        private static void Badge(KmhDiscordRpc.Presence p, PresenceSession session, PresenceTier tier)
        {
            if (session.Card != null && session.Card.Official && tier == PresenceTier.Normal)
            {
                p.SmallImageKey = KmhRichPresence.OfficialAsset;
                p.SmallImageText = UI.KmhText.Get("KMH_Presence_OfficialKmh", "Official Colony Nexus Server");
                return;
            }
            if (tier == PresenceTier.Normal)
            {
                p.SmallImageKey = KmhPresenceCatalog.AssetFor(PresenceIcon.Multiplayer);
                p.SmallImageText = session.Card != null ? CommunityLine(session.Card) : KmhPresenceCatalog.IconText(PresenceIcon.Multiplayer);
            }
        }

        private static string CommunityLine(ServerCardDto card)
        {
            string line = card.Official
                ? UI.KmhText.Get("KMH_Presence_Official", "Official server")
                : UI.KmhText.Get("KMH_Presence_Community", "Community server");
            if (card.MaxPlayers > 0) line += "  ·  " + card.Players + "/" + card.MaxPlayers;
            if (card.RequiresPassword) line += "  ·  " + UI.KmhText.Get("KMH_Presence_Locked", "Password required");
            return KmhDiscordRpc.Trim(line, 128);
        }

        private static void Party(KmhDiscordRpc.Presence p, PresenceSession session)
        {
            ServerCardDto card = session.Card;
            if (card == null || card.MaxPlayers <= 0) return;
            p.PartyId = "kmh-" + card.ServerId;
            p.PartySize = Math.Max(1, card.Players);
            p.PartyMax = card.MaxPlayers;
            p.PartyPublic = KmhJoinGate.MayAdvertise(card);
            p.JoinSecret = session.JoinAdvertisable && KmhJoinGate.MayAdvertise(card) ? KmhPresenceJoin.Secret(card) : "";
        }

        // On a server the second line is the server's name, so the colony goes on the logo's tooltip instead.
        private static void AddColonyInfo(KmhDiscordRpc.Presence p, PresenceSession session, KMHPatchSettings s, bool mp)
        {
            if (!s.DiscordShowColonyInfo || session.Colonists < 0 || session.Day < 0) return;
            string info = session.Colonists == 1
                ? UI.KmhText.Get("KMH_RP_ColonyInfoOne", "1 colonist · day {0}", session.Day)
                : UI.KmhText.Get("KMH_RP_ColonyInfo", "{0} colonists · day {1}", session.Colonists, session.Day);
            if (mp) p.LargeImageText = KmhDiscordRpc.Trim(p.LargeImageText + "  ·  " + info, 128);
            else p.State = KmhDiscordRpc.Trim(string.IsNullOrEmpty(p.State) ? info : p.State + "  ·  " + info, 128);
        }
    }
}
