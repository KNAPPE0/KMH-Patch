using KMHPatch.Features.Treasury;

namespace KMHPatch.Features.Wealth.Sources
{
    // Personal vault only: silver plus stored goods; the guild vault is GuildVaultWealthSource's, because attribution across members is its own rule.
    internal sealed class TreasuryWealthSource : IKmhWealthSource
    {
        public string Name => "Treasury";

        public float SilverValue()
        {
            // Personal, not Snapshot: Snapshot is whichever vault was fetched last, so opening the Guild Hall would drop this to zero.
            return KmhWealthValue.OfTreasury(TreasuryCache.Personal);
        }
    }
}
