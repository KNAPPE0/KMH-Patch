using System;
using KMHPatch.Features.WantBoard;
using KMHPatch.Features.WantBoard.Dto;
using KMHPatch.UI;

namespace KMHPatch.Features.Wealth.Sources
{
    // Want escrow left the treasury, so nothing else counts it, but stays mine until filled or refunded.
    internal sealed class WantBoardWealthSource : IKmhWealthSource
    {
        public string Name => "Want Board";

        public float SilverValue()
        {
            WantSnapshot s = WantCache.Snapshot;
            string me = KmhSession.Me;
            if (s?.Wants == null || string.IsNullOrEmpty(me)) return 0f;

            float v = 0f;
            foreach (WantDto w in s.Wants)
                if (w != null && w.EscrowRemaining > 0
                    && string.Equals(w.BuyerUsername, me, StringComparison.OrdinalIgnoreCase))
                    v += w.EscrowRemaining;
            return v;
        }
    }
}
