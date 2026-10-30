using KMHPatch.Features.Treasury;

namespace KMHPatch.Features.Wealth.Sources
{
    // Personal vault only; the guild vault's attribution across members is GuildVaultWealthSource's rule.
    internal sealed class TreasuryWealthSource : IKmhWealthSource
    {
        public string Name => "Treasury";

        public float SilverValue()
        {
            // Personal, not Snapshot: Snapshot is the last vault fetched, so opening the Guild Hall would zero this.
            return KmhWealthValue.OfTreasury(TreasuryCache.Personal);
        }
    }
}
