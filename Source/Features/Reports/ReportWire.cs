using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace KMHPatch.Features.Reports
{
    internal static class ReportLimits
    {
        public const int MaxTitleChars       = 120;
        public const int MaxBodyChars        = 8000;
        // The server's own ceilings, so nothing is cut there instead of here.
        public const int MaxDiagnosticsChars = 192 * 1024;
        public const int MaxScreenshotBytes  = 512 * 1024;

        public const int ReplySeconds = 45;
        public const int KeepLocalCopies = 20;
        // A report still waiting to be delivered outlives KeepLocalCopies, but not for ever.
        public static readonly TimeSpan KeepPendingFor = TimeSpan.FromDays(30);
    }

    // "question" is newer than Global and older servers, which know only the first two.
    internal static class ReportKinds
    {
        public const string Bug = "bug", Suggestion = "suggestion", Question = "question";

        public static string Normalize(string kind) => kind == Suggestion || kind == Question ? kind : Bug;

        public static string ForOlderPeer(string kind) => kind == Question ? Suggestion : Normalize(kind);
    }

    internal static class ReportAreas
    {
        public const string General = "general";

        public static readonly string[] All =
        {
            General, "joining", "marketplace", "auctions", "want_board", "treasury", "mail", "chat", "guilds", "quests",
            "world_events", "sites", "roadworks", "frontier", "standings", "performance", "other",
        };

        public static string Normalize(string area) => Array.IndexOf(All, area ?? "") >= 0 ? area : General;
    }

    internal static class ReportSeverities
    {
        public const string Blocks = "blocks", Annoying = "annoying", Minor = "minor";
        public static string Normalize(string s) => s == Blocks || s == Annoying || s == Minor ? s : "";
    }

    internal static class ReportFrequencies
    {
        public const string Always = "always", Sometimes = "sometimes", Once = "once";
        public static string Normalize(string s) => s == Always || s == Sometimes || s == Once ? s : "";
    }

    internal static class ReportDestinations
    {
        public const string Server = "server", Developers = "developers";

        public static List<string> Of(bool server, bool developers)
        {
            var d = new List<string>(2);
            if (server) d.Add(Server);
            if (developers) d.Add(Developers);
            return d;
        }
    }

    // The hello's "reports"; an older server sends none and takes no reports.
    internal static class ReportModes
    {
        public const string Off = "off", Local = "local", Global = "global";
        // The hello's "reports_v": 2 reads destinations, area, severity, frequency and the question kind.
        public const int RoutingVersion = 2;
    }

    internal static class ReportDevelopers
    {
        public const string Forwarding = "forwarding", NotForwarded = "not_forwarded";
    }

    // What the player chose in the dialog, which also goes into client_diagnostics.summary.
    internal sealed class ReportFields
    {
        public string Kind = ReportKinds.Bug;
        public string Area = ReportAreas.General;
        public string Severity = "";
        public string Frequency = "";
        public bool ToServer;
        public bool ToDevelopers;
        public string Title = "";

        public string DestinationsCsv => string.Join(",", ReportDestinations.Of(ToServer, ToDevelopers).ToArray());

        // Severity and frequency describe a bug; on anything else they are left empty.
        public ReportFields Normalized()
        {
            string kind = ReportKinds.Normalize(Kind);
            return new ReportFields
            {
                Kind = kind, Area = ReportAreas.Normalize(Area),
                Severity = kind == ReportKinds.Bug ? ReportSeverities.Normalize(Severity) : "",
                Frequency = kind == ReportKinds.Bug ? ReportFrequencies.Normalize(Frequency) : "",
                ToServer = ToServer, ToDevelopers = ToDevelopers, Title = Title ?? "",
            };
        }
    }

    // Mirror of the addon's ReportSubmit and Global's PlayerReportRequest.
    internal sealed class ReportSubmit
    {
        [JsonProperty("report_id")]          public string ReportId { get; set; } = "";
        [JsonProperty("kind")]               public string Kind { get; set; } = "";
        [JsonProperty("title")]              public string Title { get; set; } = "";
        [JsonProperty("body")]               public string Body { get; set; } = "";
        [JsonProperty("client_version")]     public string ClientVersion { get; set; } = "";
        [JsonProperty("created_at")]         public long   CreatedAtUnix { get; set; }
        [JsonProperty("client_diagnostics")] public string ClientDiagnostics { get; set; } = "";
        [JsonProperty("screenshot")]         public string Screenshot { get; set; } = "";
        [JsonProperty("destinations")]       public List<string> Destinations { get; set; } = new List<string>();
        [JsonProperty("area")]               public string Area { get; set; } = "";
        [JsonProperty("severity")]           public string Severity { get; set; } = "";
        [JsonProperty("frequency")]          public string Frequency { get; set; } = "";

        // Shallow apart from the list, so a per-peer copy never edits the saved one.
        public ReportSubmit Copy(bool withScreenshot)
            => new ReportSubmit
            {
                ReportId = ReportId, Kind = Kind, Title = Title, Body = Body, ClientVersion = ClientVersion,
                CreatedAtUnix = CreatedAtUnix, ClientDiagnostics = ClientDiagnostics, Screenshot = withScreenshot ? Screenshot : "",
                Destinations = new List<string>(Destinations ?? new List<string>()), Area = Area, Severity = Severity, Frequency = Frequency,
            };
    }

    // Mirror of the addon's ReportResultWire.
    internal sealed class ReportResultWire
    {
        [JsonProperty("report_id")]  public string ReportId { get; set; } = "";
        [JsonProperty("ok")]         public bool   Ok { get; set; }
        [JsonProperty("reason")]     public string Reason { get; set; } = "";
        [JsonProperty("forwarding")] public string Forwarding { get; set; } = "";
        [JsonProperty("message")]    public string Message { get; set; } = "";
        [JsonProperty("developers")] public string Developers { get; set; } = "";
        [JsonProperty("reference")]  public string Reference { get; set; } = "";
    }
}
