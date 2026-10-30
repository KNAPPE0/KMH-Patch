using System;
using System.Collections.Generic;
using System.IO;
using KMHPatch.Diagnostics;
using Newtonsoft.Json;

namespace KMHPatch.Features.Mods
{
    internal static class KmhPrepareStage
    {
        public const string Running = "running";
        public const string Restarting = "restarting";
        public const string Connecting = "connecting";
    }

    // Persisted so a restart mid-preparation carries on instead of starting over.
    internal sealed class KmhPrepareJob
    {
        [JsonProperty("server_id")]   public string ServerId = "";
        [JsonProperty("server_name")] public string ServerName = "";
        [JsonProperty("endpoint")]    public string Endpoint = "";
        [JsonProperty("rwt_version")] public string RwtVersion = "";
        // The RimWorld version the job was made on. Steam can switch branches between two launches.
        [JsonProperty("game_version")] public string GameVersion = "";
        [JsonProperty("stage")]       public string Stage = KmhPrepareStage.Running;
        [JsonProperty("created")]     public long CreatedUnix;
        [JsonProperty("updated")]     public long UpdatedUnix;
        // Checked against what RimWorld actually loaded after the restart.
        [JsonProperty("expected_order")] public List<string> ExpectedOrder = new List<string>();
        [JsonProperty("expected_rwt")]   public string ExpectedRwt = "";
        [JsonProperty("optional")]       public List<string> OptionalChosen = new List<string>();
        [JsonProperty("subscribed")]     public List<string> Subscribed = new List<string>();
        [JsonProperty("accept_unverified_rwt")] public bool AcceptUnverifiedRwt;
        // What the player reviewed: the job acts only while the server still asks for exactly this.
        [JsonProperty("approved")]          public bool Approved;
        [JsonProperty("approved_policy")]   public string ApprovedPolicy = "";
        [JsonProperty("approved_profile")]  public string ApprovedProfile = "";
        [JsonProperty("list_written")]   public bool ListWritten;
        [JsonProperty("rescanned")]      public bool Rescanned;
        // Downloaded mods RimWorld only finds on start-up: one restart to see them, then the list is set.
        [JsonProperty("discover_after_restart")] public bool DiscoverAfterRestart;
        [JsonProperty("restarts")]       public int Restarts;
        [JsonProperty("auto_join")]      public bool AutoJoin = true;
        [JsonProperty("last_error")]     public string LastError = "";
    }

    internal static class KmhPrepareJobStore
    {
        // Older than this and the server, its list or the player's mind have probably moved on.
        internal static readonly TimeSpan MaxAge = TimeSpan.FromHours(6);
        internal static string PathOverrideForTest;

        private static string FilePath => PathOverrideForTest ?? Path.Combine(Rwt.RwtPaths.KmhRoot, "join-preparation.json");

        public static KmhPrepareJob Load(DateTime nowUtc)
        {
            try
            {
                if (!File.Exists(FilePath)) return null;
                KmhPrepareJob job = JsonConvert.DeserializeObject<KmhPrepareJob>(File.ReadAllText(FilePath));
                if (job == null || job.ServerId.Length != 32) { Clear(); return null; }
                if (Expired(job, nowUtc)) { KmhLog.Info($"Join preparation for {job.ServerName} is over {MaxAge.TotalHours:0} h old; dropped."); Clear(); return null; }
                if (FromOtherGame(job, Compat.KmhGameVersion.MajorMinor))
                {
                    KmhLog.Info($"Join preparation for {job.ServerName} was made on RimWorld {job.GameVersion}; this game is {Compat.KmhGameVersion.MajorMinor}, so it was dropped untouched.");
                    Clear(); return null;
                }
                if (job.ExpectedOrder == null) job.ExpectedOrder = new List<string>();
                if (job.OptionalChosen == null) job.OptionalChosen = new List<string>();
                if (job.Subscribed == null) job.Subscribed = new List<string>();
                return job;
            }
            catch (Exception ex)
            {
                KmhLog.Warn($"Join preparation: ignoring an unreadable record: {ex.Message}");
                Clear();
                return null;
            }
        }

        internal static bool Expired(KmhPrepareJob job, DateTime nowUtc)
            => nowUtc - DateTimeOffset.FromUnixTimeSeconds(Math.Max(job.UpdatedUnix, job.CreatedUnix)).UtcDateTime > MaxAge;

        // Its mod list and RWT choice were for the other version; a job without the stamp says nothing.
        internal static bool FromOtherGame(KmhPrepareJob job, string mine)
            => job != null && !string.IsNullOrEmpty(job.GameVersion) && !string.IsNullOrEmpty(mine)
            && !string.Equals(job.GameVersion, mine, StringComparison.Ordinal);

        public static bool Save(KmhPrepareJob job, DateTime nowUtc)
        {
            if (job == null) return false;
            job.UpdatedUnix = new DateTimeOffset(nowUtc).ToUnixTimeSeconds();
            return Persistence.KmhAtomicFile.TryWrite(FilePath, JsonConvert.SerializeObject(job, Formatting.Indented), "join preparation");
        }

        public static void Clear()
        {
            try { if (File.Exists(FilePath)) File.Delete(FilePath); }
            catch (Exception ex) { KmhLog.Warn($"Join preparation: could not remove its record: {ex.Message}"); }
        }
    }
}
