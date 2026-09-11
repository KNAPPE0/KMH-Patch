using System;
using System.Collections.Generic;
using KMHPatch.Features.Treasury.Dto;
using KMHPatch.Items;
using KMHPatch.UI;
using Verse;

namespace KMHPatch.Features.Wealth
{
    // The one formula for valuing KMH-held goods: payloads at captured per-unit market value x stack, compact entries at base market value; asking and bid prices never count.
    internal static class KmhWealthValue
    {
        // One full-state payload stack (captured per-unit market value x stack; 0 when unpriced).
        public static float Payload(KmhThingPayload p)
            => p != null && p.MarketValue > 0 ? (float)p.MarketValue * Math.Max(1, p.StackCount) : 0f;

        public static float Payloads(IEnumerable<KmhThingPayload> payloads)
        {
            float v = 0f;
            if (payloads != null) foreach (KmhThingPayload p in payloads) v += Payload(p);
            return v;
        }

        // A compact composed key (def|stuff|quality) + count at base market value; only the def drives base value.
        public static float CompactKey(string itemKey, int count)
        {
            if (count <= 0 || string.IsNullOrEmpty(itemKey)) return 0f;
            ItemKeys.Split(itemKey, out string defName, out _, out _);
            return CompactDef(defName, count);
        }

        // A raw def name + count at base market value (0 when the def is unknown or valueless).
        public static float CompactDef(string defName, int count)
        {
            if (count <= 0) return 0f;
            ThingDef def = ColonyGoods.Def(defName);
            return def != null && def.BaseMarketValue > 0f ? def.BaseMarketValue * count : 0f;
        }

        // The whole silver value of a treasury/vault snapshot: silver + compact items + full-state payloads.
        public static float OfTreasury(TreasurySnapshot s)
        {
            if (s == null) return 0f;
            float v = s.SilverBalance;
            if (s.Items != null)
                foreach (KeyValuePair<string, int> kv in s.Items) v += CompactKey(kv.Key, kv.Value);
            v += Payloads(s.ItemPayloads);
            return v;
        }
    }
}
