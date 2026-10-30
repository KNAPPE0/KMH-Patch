using System;

namespace KMHPatch.Features.Global
{
    internal enum BrowserSort { Recommended, Players, Active, Name, Recent }

    // Every mode is a total order down to ServerId so tied servers never swap places between frames.
    internal struct SortKey
    {
        public bool   Official;
        public int    Rank;            // compatibility bucket; lower is more joinable
        public int    Players;
        public int    PlayersPeak;
        public float  PlayersAverage;
        public long   LastHeartbeat;
        public string Name;
        public string ServerId;
    }

    internal static class BrowserSorting
    {
        public static string Label(BrowserSort sort)
        {
            switch (sort)
            {
                case BrowserSort.Players: return UI.KmhText.Get("KMH_Browser_Sort_Players", "Most players");
                case BrowserSort.Active:  return UI.KmhText.Get("KMH_Browser_Sort_Active", "Most active");
                case BrowserSort.Name:    return UI.KmhText.Get("KMH_Browser_Sort_Name", "Name");
                case BrowserSort.Recent:  return UI.KmhText.Get("KMH_Browser_Sort_Heard", "Last heard from");
                default:                  return UI.KmhText.Get("KMH_Browser_Sort_Recommended", "Recommended");
            }
        }

        public static string Describe(BrowserSort sort)
        {
            switch (sort)
            {
                case BrowserSort.Players: return UI.KmhText.Get("KMH_Browser_Sort_Players_Tip", "Highest player count right now.");
                case BrowserSort.Active:  return UI.KmhText.Get("KMH_Browser_Sort_Active_Tip", "Busiest in the last 24 hours, so a server that is quiet this hour still ranks.");
                case BrowserSort.Name:    return UI.KmhText.Get("KMH_Browser_Sort_Name_Tip", "Alphabetical.");
                case BrowserSort.Recent:  return UI.KmhText.Get("KMH_Browser_Sort_Heard_Tip", "Most recently heard from Colony Nexus Global. Not the same as the Recent tab, which lists servers you joined.");
                default:                  return UI.KmhText.Get("KMH_Browser_Sort_Recommended_Tip", "Servers you can join, busiest first.");
            }
        }

        public const int CannotJoinRank = 5;

        // Official pins to the top in every mode, but Recommended sinks unjoinable servers (even official) below it.
        public static int Compare(SortKey a, SortKey b, BrowserSort sort)
        {
            if (sort == BrowserSort.Recommended && (a.Rank >= CannotJoinRank) != (b.Rank >= CannotJoinRank))
                return a.Rank >= CannotJoinRank ? 1 : -1;
            if (a.Official != b.Official) return a.Official ? -1 : 1;

            int by = Primary(a, b, sort);
            if (by != 0) return by;

            int name = string.Compare(a.Name ?? "", b.Name ?? "", StringComparison.OrdinalIgnoreCase);
            if (name != 0) return name;
            return string.CompareOrdinal(a.ServerId ?? "", b.ServerId ?? "");
        }

        private static int Primary(SortKey a, SortKey b, BrowserSort sort)
        {
            switch (sort)
            {
                case BrowserSort.Players:
                    return b.Players.CompareTo(a.Players);
                case BrowserSort.Active:
                    int peak = b.PlayersPeak.CompareTo(a.PlayersPeak);
                    return peak != 0 ? peak : b.PlayersAverage.CompareTo(a.PlayersAverage);
                case BrowserSort.Name:
                    return 0;
                case BrowserSort.Recent:
                    return b.LastHeartbeat.CompareTo(a.LastHeartbeat);
                default:
                    int rank = a.Rank.CompareTo(b.Rank);
                    return rank != 0 ? rank : b.Players.CompareTo(a.Players);
            }
        }

        public static bool Matches(string search, string name, System.Collections.Generic.List<string> tags, string region)
        {
            if (string.IsNullOrEmpty(search)) return true;

            // "tag:pvp" narrows to the tag alone, so a word that is also in server names stays usable as a filter.
            if (search.StartsWith("tag:", StringComparison.OrdinalIgnoreCase))
            {
                string wanted = search.Substring(4).Trim();
                if (wanted.Length == 0) return true;
                if (tags == null) return false;
                foreach (string t in tags)
                    if (!string.IsNullOrEmpty(t) && t.IndexOf(wanted, StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                return false;
            }

            if (!string.IsNullOrEmpty(name) && name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (!string.IsNullOrEmpty(region) && region.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (tags != null)
                foreach (string t in tags)
                    if (!string.IsNullOrEmpty(t) && t.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }
    }
}
