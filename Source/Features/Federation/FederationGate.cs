using KMHPatch.SubProtocol;

namespace KMHPatch.Features.Federation
{
    // What the Colony Nexus tab, the dashboard and the request helpers may offer, decided in one place.
    internal static class FederationGate
    {
        // The router requires every feature a kind maps to: kmh.federation.* needs guilds and federation both.
        internal static bool FederationOffered
            => Decide(KmhCapabilities.Has(KmhCapabilities.Federation),
                      KmhFeatures.IsEnabled("guilds"), KmhFeatures.IsEnabled("federation"));

        internal static bool Decide(bool advertised, bool guildsOn, bool federationOn)
            => advertised && guildsOn && federationOn;

        // While the server lists "connected" as disabled, nothing may offer the Connected Treasury.
        internal static bool TreasuryOffered => KmhFeatures.IsEnabled("connected");

        // Offered only where the server says this player may act; it re-checks each one when the request lands.
        internal static bool OfficerActionsOffered(Dto.FederationSnapshot s)
            => s != null && s.MayManage && s.Available;

        internal static bool LeaderActionsOffered(Dto.FederationSnapshot s)
            => s != null && s.MayLead && s.Available && FederationCache.IsLiveLink(s) && !FederationCache.IsDisbanded(s);

        // A leader acts on another live member, never on its own server's row.
        internal static bool MayTarget(Dto.FederationSnapshot s, Dto.FederationMember m)
            => LeaderActionsOffered(s) && m != null && !m.IsSelf && !string.IsNullOrEmpty(m.LinkId)
            && FederationRules.IsLiveLinkState(m.State);
    }
}
