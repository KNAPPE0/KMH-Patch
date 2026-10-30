using System.Collections.Generic;
using KMHPatch.Features.Mail;
using KMHPatch.Features.Mail.Dto;
using Verse;

namespace KMHPatch.Features.Wealth.Sources
{
    // Unaccepted outgoing attachments stay the sender's until claimed or refunded; uncounted, mail shelters value.
    internal sealed class MailWealthSource : IKmhWealthSource
    {
        public string Name => "Mail";

        public float SilverValue()
        {
            MailSnapshot s = MailCache.Snapshot;
            if (s == null) return 0f;

            float v = s.EscrowedOutSilver + s.EscrowedOutPayloadValue;
            if (s.EscrowedOutItems != null)
                foreach (KeyValuePair<string, int> kv in s.EscrowedOutItems)
                {
                    ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(kv.Key);
                    if (def != null) v += def.BaseMarketValue * kv.Value;
                }
            return v;
        }
    }
}
