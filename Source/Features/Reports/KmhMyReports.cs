using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using KMHPatch.Diagnostics;
using KMHPatch.Notifications;
using KMHPatch.UI;
using Newtonsoft.Json;

namespace KMHPatch.Features.Reports
{
    internal static class KmhMyReports
    {
        internal sealed class Remote
        {
            [JsonProperty("report_id")]  public string ReportId = "";
            [JsonProperty("reference")]  public string Reference = "";
            [JsonProperty("kind")]       public string Kind = "";
            [JsonProperty("area")]       public string Area = "";
            [JsonProperty("title")]      public string Title = "";
            [JsonProperty("status")]     public string Status = "";
            [JsonProperty("fixed_in")]   public string FixedIn = "";
            [JsonProperty("created_at")] public long   CreatedUnix = 0;
            [JsonProperty("updated_at")] public long   UpdatedUnix = 0;
            [JsonProperty("messages")]   public List<Reply> Messages = new List<Reply>();
        }

        internal sealed class Reply
        {
            [JsonProperty("body")]       public string Body = "";
            [JsonProperty("created_at")] public long   CreatedUnix = 0;
        }

        private sealed class RemoteList
        {
            [JsonProperty("reports")] public List<Remote> Reports = new List<Remote>();
        }

        // Every half hour while linked: a status is news, not a chat message.
        internal const float EverySeconds = 1800f;
        // Not .json: the copy pruning counts every .json beside it as a report.
        private const string SeenFile = "developer-status.seen";

        private static readonly Dictionary<string, Remote> _remote = new Dictionary<string, Remote>(StringComparer.OrdinalIgnoreCase);
        private static float _next = 30f;
        private static int _busy;
        public static DateTime LastFetchUtc { get; private set; } = DateTime.MinValue;
        public static string LastProblem { get; private set; } = "";
        public static bool Fetching => Volatile.Read(ref _busy) == 1;

        public static Remote For(string reportId)
            => !string.IsNullOrEmpty(reportId) && _remote.TryGetValue(reportId, out Remote r) ? r : null;

        public static List<Remote> All() => new List<Remote>(_remote.Values);

        public static bool Linked => Account.KmhAccountStore.HasToken(Global.GlobalHttp.BaseUrl);

        public static void Tick()
        {
            if (UnityEngine.Time.realtimeSinceStartup < _next) return;
            _next = UnityEngine.Time.realtimeSinceStartup + EverySeconds;
            if (Linked) Fetch(notify: true);
        }

        public static void Refresh() => Fetch(notify: true);

        private static void Fetch(bool notify)
        {
            if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return;
            string token = Account.KmhAccountStore.TokenFor(Global.GlobalHttp.BaseUrl);
            if (token.Length == 0) { Volatile.Write(ref _busy, 0); LastProblem = KmhText.Get("KMH_MyReports_NotLinked", "Link your Colony Nexus account to see what the developers did with your reports."); return; }
            ulong steam = Account.KmhSteamPersona.SteamId64();
            string path = "/api/v1/player/reports?limit=50" + (steam > 0 ? "&steam_id=" + steam : "");
            Task.Run(async () =>
            {
                Global.GlobalHttp.Result r;
                try { r = await Global.GlobalHttp.GetAsync(path, token, CancellationToken.None).ConfigureAwait(false); }
                catch (Exception ex) { r = new Global.GlobalHttp.Result { Status = 0, ErrorCode = ex.GetType().Name }; }
                KmhMainThread.Post(() =>
                {
                    Volatile.Write(ref _busy, 0);
                    if (!r.Ok)
                    {
                        LastProblem = r.Status == 404 ? KmhText.Get("KMH_MyReports_NoService", "This Colony Nexus directory does not report back on reports yet.")
                                                      : KmhText.Get("KMH_MyReports_Failed", "Could not reach Colony Nexus just now; it tries again later.");
                        return;
                    }
                    RemoteList list;
                    try { list = r.Body?.ToObject<RemoteList>() ?? new RemoteList(); }
                    catch (Exception ex) { KmhLog.Warn("My reports: unreadable answer: " + ex.Message); return; }
                    LastProblem = "";
                    LastFetchUtc = DateTime.UtcNow;
                    Apply(list.Reports ?? new List<Remote>(), notify);
                });
            });
        }

        private static void Apply(List<Remote> reports, bool notify)
        {
            Dictionary<string, string> seen = LoadSeen(out bool firstRun);
            foreach (Remote r in reports)
            {
                if (r == null || string.IsNullOrEmpty(r.ReportId)) continue;
                _remote[r.ReportId] = r;
                string now = Mark(r);
                seen.TryGetValue(r.ReportId, out string before);
                if (!firstRun && notify && before != null && before != now) Announce(r, before);
                seen[r.ReportId] = now;
            }
            SaveSeen(seen);
        }

        internal static string Mark(Remote r) => $"{r.Status}|{r.FixedIn}|{r.Messages?.Count ?? 0}";

        private static void Announce(Remote r, string before)
        {
            string reference = string.IsNullOrEmpty(r.Reference) ? KmhReportClient.Short(r.ReportId) : r.Reference;
            int repliesBefore = 0;
            string[] parts = before.Split('|');
            if (parts.Length == 3) int.TryParse(parts[2], out repliesBefore);
            string line = (r.Messages?.Count ?? 0) > repliesBefore
                ? string.Format(KmhText.Get("KMH_MyReports_NewReply", "The Colony Nexus developers replied to your report {0}."), reference)
                : string.Format(KmhText.Get("KMH_MyReports_NewStatus", "Your report {0} is now: {1}."), reference, StatusLabel(r.Status, r.FixedIn));
            KmhNotifications.Positive(line);
        }

        internal static string StatusLabel(string status, string fixedIn)
        {
            switch ((status ?? "").ToLowerInvariant())
            {
                case "new":         return KmhText.Get("KMH_MyReports_StatusNew", "Received");
                case "seen":        return KmhText.Get("KMH_MyReports_StatusSeen", "Seen by the developers");
                case "acknowledged":return KmhText.Get("KMH_MyReports_StatusAck", "Acknowledged");
                case "in_progress": return KmhText.Get("KMH_MyReports_StatusWorking", "Being worked on");
                case "fixed":       return string.IsNullOrEmpty(fixedIn) ? KmhText.Get("KMH_MyReports_StatusFixed", "Fixed")
                                                                         : string.Format(KmhText.Get("KMH_MyReports_StatusFixedIn", "Fixed in {0}"), fixedIn);
                case "wont_fix":    return KmhText.Get("KMH_MyReports_StatusWontFix", "Won't be changed");
                case "duplicate":   return KmhText.Get("KMH_MyReports_StatusDuplicate", "Already reported by someone else");
                case "closed":      return KmhText.Get("KMH_MyReports_StatusClosed", "Closed");
                default:            return string.IsNullOrEmpty(status) ? "" : status;
            }
        }

        private static string SeenPath => Path.Combine(KmhReportClient.CopiesFolder, SeenFile);

        private static Dictionary<string, string> LoadSeen(out bool firstRun)
        {
            firstRun = !File.Exists(SeenPath);
            try
            {
                if (!firstRun)
                    return JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(SeenPath))
                           ?? new Dictionary<string, string>();
            }
            catch (Exception ex) { KmhLog.Warn("My reports: could not read what was already seen: " + ex.Message); }
            return new Dictionary<string, string>();
        }

        private static void SaveSeen(Dictionary<string, string> seen)
        {
            try
            {
                Directory.CreateDirectory(KmhReportClient.CopiesFolder);
                File.WriteAllText(SeenPath, JsonConvert.SerializeObject(seen));
            }
            catch (Exception ex) { KmhLog.Warn("My reports: could not save what was seen: " + ex.Message); }
        }
    }
}
