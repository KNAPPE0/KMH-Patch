using System;
using System.Collections.Generic;
using KMHPatch.Diagnostics;
using KMHPatch.SubProtocol;
using Newtonsoft.Json;

namespace KMHPatch.Features.Leaderboards.Dto
{
    // Mirrors the addon's LeaderboardsSnapshot and LeaderboardBoardDto, same JSON names and fields.
    public class LeaderboardsSnapshot
    {
        [JsonProperty("season")]             public int    Season { get; set; } = 1;
        [JsonProperty("season_started_utc")] public long   SeasonStartedUnix { get; set; }
        [JsonProperty("generated_utc")]      public long   GeneratedUnix { get; set; }
        [JsonProperty("boards")]             public List<LeaderboardBoard> Boards { get; set; } = new List<LeaderboardBoard>();
        [JsonProperty("page")]               public string Page { get; set; } = "";
    }

    public class LeaderboardBoard
    {
        [JsonProperty("key")]                public string Key { get; set; } = "";
        [JsonProperty("title")]              public string Title { get; set; } = "";
        [JsonProperty("group")]              public string Group { get; set; } = "";
        [JsonProperty("unit")]               public string Unit { get; set; } = "";
        [JsonProperty("scope")]              public string Scope { get; set; } = "";
        [JsonProperty("source")]             public string Source { get; set; } = "";
        [JsonProperty("description")]        public string Description { get; set; } = "";
        [JsonProperty("season")]             public int    Season { get; set; }
        [JsonProperty("season_started_utc")] public long   SeasonStartedUnix { get; set; }
        [JsonProperty("generated_utc")]      public long   GeneratedUnix { get; set; }
        [JsonProperty("total")]              public int    Total { get; set; }
        [JsonProperty("rows")]               public List<LeaderboardRow> Rows { get; set; } = new List<LeaderboardRow>();

        public bool Reported => Source == "reported";
        public bool IsGuildBoard => Scope == "guild";
    }

    public class LeaderboardRow
    {
        [JsonProperty("rank")]    public int    Rank { get; set; }
        [JsonProperty("name")]    public string Name { get; set; } = "";
        [JsonProperty("guild")]   public string Guild { get; set; } = "";
        [JsonProperty("colony")]  public string Colony { get; set; } = "";
        [JsonProperty("value")]   public long   Value { get; set; }
        [JsonProperty("display")] public string Display { get; set; } = "";
        [JsonProperty("online")]  public bool   Online { get; set; }
    }
}

namespace KMHPatch.Features.Leaderboards
{
    using KMHPatch.Features.Leaderboards.Dto;

    // The server's ranking, held as it arrived: the window draws it and never re-sorts or re-scores it.
    public static class LeaderboardCache
    {
        public static LeaderboardsSnapshot Snapshot { get; private set; }
        public static DateTime LastUpdatedUtc { get; private set; } = DateTime.MinValue;
        public static bool HasSnapshot => Snapshot != null;
        public static event Action Updated;

        internal static void Apply(LeaderboardsSnapshot snapshot)
        {
            Snapshot = snapshot ?? new LeaderboardsSnapshot();
            LastUpdatedUtc = DateTime.UtcNow;
            KmhCacheEvents.Raise(Updated, "Leaderboards");
        }

        internal static void Clear()
        {
            Snapshot = null;
            LastUpdatedUtc = DateTime.MinValue;
        }

        public static LeaderboardBoard Board(string key)
        {
            if (Snapshot?.Boards == null) return null;
            foreach (LeaderboardBoard b in Snapshot.Boards) if (b != null && b.Key == key) return b;
            return null;
        }

        // Only a page on the directory this game already talks to: the server can never send a player anywhere else.
        internal static string WebsitePage(string page, string directory, string board)
        {
            if (string.IsNullOrEmpty(page) || !Uri.TryCreate(page, UriKind.Absolute, out Uri p)
                || !Uri.TryCreate(directory ?? "", UriKind.Absolute, out Uri d)) return "";
            bool secure = p.Scheme == Uri.UriSchemeHttps || (p.Scheme == Uri.UriSchemeHttp && Global.GlobalHttp.IsLoopback(page));
            if (!secure || p.Scheme != d.Scheme || !string.Equals(p.Host, d.Host, StringComparison.OrdinalIgnoreCase) || p.Port != d.Port
                || p.UserInfo.Length > 0 || p.Query.Length > 0 || p.Fragment.Length > 0
                || !System.Text.RegularExpressions.Regex.IsMatch(p.AbsolutePath, "^/servers/[A-Za-z0-9-]{1,64}/leaderboards$")) return "";
            string root = p.GetLeftPart(UriPartial.Path);
            return string.IsNullOrEmpty(board) ? root : root + "/" + Uri.EscapeDataString(board);
        }
    }

    public static class LeaderboardHandler
    {
        public static void Register()
        {
            KmhDispatcher.RegisterHandler(KmhProtocol.Kind.LeaderboardsSnapshot, OnSnapshot);
        }

        public static bool RequestSnapshot() => KmhDispatcher.Send(KmhProtocol.Kind.LeaderboardsRequest, null);

        private static void OnSnapshot(KmhEnvelope env)
        {
            LeaderboardsSnapshot snapshot = env?.DataAs<LeaderboardsSnapshot>();
            if (snapshot == null) { KmhLog.Warn("Leaderboards snapshot had no parseable payload, ignoring"); return; }
            LeaderboardCache.Apply(snapshot);
        }
    }
}
