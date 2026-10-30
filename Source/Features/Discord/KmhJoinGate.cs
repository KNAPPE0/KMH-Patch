using System;
using KMHPatch.Features.Global;

namespace KMHPatch.Features.Discord
{
    internal enum JoinRefusal { None, JoinsOff, NotKnown, NotPublic, PasswordRequired, StaleDirectory, Incompatible }

    internal enum AdvertiseBlock { None, JoinsOff, NotOnServer, NotListed, PasswordRequired, NoAddress, NoUriScheme, PresenceOff }

    internal readonly struct JoinDecision
    {
        public readonly JoinRefusal Refusal;
        public readonly CompatState Compat;

        private JoinDecision(JoinRefusal refusal, CompatState compat) { Refusal = refusal; Compat = compat; }

        public bool Allowed => Refusal == JoinRefusal.None;

        internal static JoinDecision Ok() => new JoinDecision(JoinRefusal.None, CompatState.Ready);
        internal static JoinDecision No(JoinRefusal refusal) => new JoinDecision(refusal, CompatState.Ready);
        internal static JoinDecision Blocked(CompatState compat) => new JoinDecision(JoinRefusal.Incompatible, compat);

        public string Explain(ServerCardDto card)
        {
            switch (Refusal)
            {
                case JoinRefusal.None:
                    return "";
                case JoinRefusal.JoinsOff:
                    return UI.KmhText.Get("KMH_Presence_JoinOff",
                        "A Discord join was offered, but joining from Discord is turned off in Colony Nexus settings.");
                case JoinRefusal.NotKnown:
                    return UI.KmhText.Get("KMH_Presence_JoinUnknown",
                        "That server is not in your Colony Nexus server list. Open the Colony Nexus server browser to refresh it.");
                case JoinRefusal.NotPublic:
                    return UI.KmhText.Get("KMH_Presence_JoinNotPublic", "That server is not publicly listed.");
                case JoinRefusal.PasswordRequired:
                    return UI.KmhText.Get("KMH_Presence_JoinLocked",
                        "That server needs a password, so it cannot be joined from Discord.");
                case JoinRefusal.StaleDirectory:
                    return UI.KmhText.Get("KMH_Presence_JoinStale",
                        "Colony Nexus could not refresh your server list, so it cannot check that server right now. Try again in a moment.");
                default:
                    return KmhCompatibility.Explain(card, Compat);
            }
        }
    }

    // Same compat verdict either way; only an invite adds the password, listing and directory-freshness rules.
    internal enum JoinSource { Browser, DiscordInvite }

    internal static class KmhJoinGate
    {
        // A directory row older than this is not an answer to "can I join right now", so the join refreshes first.
        internal static readonly TimeSpan MaxDirectoryAge = TimeSpan.FromSeconds(60);

        public static bool DirectoryIsFresh(DateTime asOfUtc, bool fromCache, DateTime nowUtc)
            => !fromCache && asOfUtc != default && nowUtc - asOfUtc <= MaxDirectoryAge;

        // Narrower than MayJoin: mods, slots and protocol are the joiner's facts, re-checked when they click.
        public static bool MayAdvertise(ServerCardDto card) => WhyNotAdvertised(card) == AdvertiseBlock.None;

        public static AdvertiseBlock WhyNotAdvertised(ServerCardDto card)
        {
            if (!KMHPatchMod.SettingsOrDefault.AllowDiscordJoin) return AdvertiseBlock.JoinsOff;
            if (card == null || string.IsNullOrEmpty(card.ServerId)) return AdvertiseBlock.NotListed;
            if (!card.FromPublicDirectory) return AdvertiseBlock.NotListed;
            if (card.RequiresPassword) return AdvertiseBlock.PasswordRequired;
            if (!card.TrySplitEndpoint(out _, out _)) return AdvertiseBlock.NoAddress;
            return AdvertiseBlock.None;
        }

        // The browser and a Discord invite both ask here, so no rule can be enforced in only one of them.
        public static JoinDecision MayJoin(ServerCardDto card, bool directoryFresh,
                                           JoinSource source = JoinSource.DiscordInvite)
        {
            bool invite = source == JoinSource.DiscordInvite;

            if (invite && !KMHPatchMod.SettingsOrDefault.AllowDiscordJoin) return JoinDecision.No(JoinRefusal.JoinsOff);
            if (card == null || string.IsNullOrEmpty(card.ServerId)) return JoinDecision.No(JoinRefusal.NotKnown);
            // The browser shows its own staleness and refreshes while open; an invite arrives with no such chance.
            if (invite && !directoryFresh) return JoinDecision.No(JoinRefusal.StaleDirectory);
            if (invite && !card.FromPublicDirectory) return JoinDecision.No(JoinRefusal.NotPublic);
            // RWT can prompt a player who chose a server themselves. Nobody can be prompted for a stranger's invite.
            if (invite && card.RequiresPassword) return JoinDecision.No(JoinRefusal.PasswordRequired);

            CompatState state = KmhCompatibility.Evaluate(card);
            if (KmhCompatibility.IsBlocking(state)) return JoinDecision.Blocked(state);

            if (!card.TrySplitEndpoint(out _, out _)) return JoinDecision.Blocked(CompatState.NoAddress);
            return JoinDecision.Ok();
        }
    }
}
