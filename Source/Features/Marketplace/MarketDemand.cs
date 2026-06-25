using System;
using System.Collections.Generic;
using KMHPatch.Features.Marketplace.Dto;
using KMHPatch.Features.WantBoard;
using KMHPatch.Features.WantBoard.Dto;

namespace KMHPatch.Features.Marketplace
{
    // Client-side demand hint from cached listings + wants; advisory only, never pricing authority.

    internal static class MarketDemand
    {
        private static Dictionary<string, int> _supply, _demand;
        private static DateTime _mpStamp = DateTime.MinValue, _wbStamp = DateTime.MinValue;

        // Refresh only when marketplace or want-board snapshots change.
        private static void EnsureFresh()
        {
            DateTime mp = MarketplaceCache.LastUpdatedUtc, wb = WantCache.LastUpdatedUtc;
            if (_supply != null && mp == _mpStamp && wb == _wbStamp) return;
            _mpStamp = mp; _wbStamp = wb;
            _supply = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            _demand = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            MarketplaceSnapshot ms = MarketplaceCache.Snapshot;
            if (ms?.Listings != null)
                foreach (MarketplaceListing l in ms.Listings)
                    if (l != null && !string.IsNullOrEmpty(l.ItemDefName) && l.RemainingQty > 0)
                        _supply[l.ItemDefName] = (_supply.TryGetValue(l.ItemDefName, out int s) ? s : 0) + l.RemainingQty;

            WantSnapshot ws = WantCache.Snapshot;
            if (ws?.Wants != null)
                foreach (WantDto w in ws.Wants)
                {
                    if (w == null || string.IsNullOrEmpty(w.ItemDefName)) continue;
                    int open = w.QtyWanted - w.QtyFilled;
                    if (open > 0) _demand[w.ItemDefName] = (_demand.TryGetValue(w.ItemDefName, out int d) ? d : 0) + open;
                }
        }

        // -1 = oversupply, +1 = high demand, 0 = no signal.
        private static double Factor(string itemDefName, out int supply, out int demand)
        {
            supply = demand = 0;
            if (string.IsNullOrEmpty(itemDefName)) return 0;
            EnsureFresh();
            _supply.TryGetValue(itemDefName, out supply);
            _demand.TryGetValue(itemDefName, out demand);
            long denom = (long)supply + demand;
            return denom <= 0 ? 0 : (demand - supply) / (double)denom;
        }

        // Compact row arrow; empty when balanced or unknown.
        public static string Arrow(string itemDefName)
        {
            double f = Factor(itemDefName, out _, out _);
            if (f >=  0.34) return " <color=#7CD37C>↑</color>";
            if (f <= -0.34) return " <color=#E0A24A>↓</color>";
            return "";
        }

        // Hover detail; empty when there is no useful signal.
        public static string Tip(string itemDefName)
        {
            double f = Factor(itemDefName, out int supply, out int demand);
            if (supply == 0 && demand == 0) return "";
            string state = f >= 0.34 ? "In demand - sellers keep more (tax rebate) when dynamic pricing is on."
                         : f <= -0.34 ? "Oversupplied - higher house tax when dynamic pricing is on."
                         : "Balanced supply and demand.";
            return $"Market demand\n{demand} wanted vs {supply} listed across the server.\n{state}";
        }
    }
}
