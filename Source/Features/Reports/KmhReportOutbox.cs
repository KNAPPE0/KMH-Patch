using System;
using System.Collections.Generic;
using System.IO;
using KMHPatch.Diagnostics;
using Newtonsoft.Json;

namespace KMHPatch.Features.Reports
{
    internal static class ReportDeliveryStates
    {
        public const string NotChosen = "", Pending = "pending", Sending = "sending", Sent = "sent", Failed = "failed";
    }

    internal static class ReportReasons
    {
        public const string Waiting = "waiting", Queued = "queued", NoAnswer = "no_answer", Disconnected = "disconnected",
            CouldNotSend = "could_not_send", RateLimited = "rate_limited", Storage = "storage", Invalid = "invalid",
            NotForwarded = "not_forwarded", GlobalDown = "global_down", GlobalLimit = "global_limit",
            GlobalSignIn = "global_sign_in", GlobalInvalid = "global_invalid", Refused = "refused", Missing = "missing";

        // A kick on reconnect must not undo a wait the peer asked for.
        public static bool PeerAskedToWait(string reason) => reason == RateLimited || reason == GlobalLimit;
    }

    internal static class ReportVia
    {
        public const string Server = "server", Global = "global";
    }

    internal sealed class ReportDelivery
    {
        [JsonProperty("state")]        public string State = ReportDeliveryStates.NotChosen;
        [JsonProperty("reason")]       public string Reason = "";
        [JsonProperty("detail")]       public string Detail = "";
        [JsonProperty("via")]          public string Via = "";
        [JsonProperty("attempts")]     public int    Attempts;
        [JsonProperty("next_attempt")] public long   NextAttemptUnix;
        [JsonProperty("updated")]      public long   UpdatedUnix;

        [JsonIgnore] public bool Chosen    => !string.IsNullOrEmpty(State);
        [JsonIgnore] public bool IsPending => State == ReportDeliveryStates.Pending;
        [JsonIgnore] public bool IsSending => State == ReportDeliveryStates.Sending;
        [JsonIgnore] public bool Settled   => State == ReportDeliveryStates.Sent || State == ReportDeliveryStates.Failed;

        public static ReportDelivery For(bool chosen, long now)
            => new ReportDelivery { State = chosen ? ReportDeliveryStates.Pending : ReportDeliveryStates.NotChosen, UpdatedUnix = now,
                                    Reason = chosen ? ReportReasons.Waiting : "" };
    }

    // Beside the saved copy as <stem>.delivery.json, so a report waits across restarts.
    internal sealed class ReportOutboxEntry
    {
        [JsonProperty("report_id")]   public string ReportId = "";
        [JsonProperty("title")]       public string Title = "";
        [JsonProperty("kind")]        public string Kind = "";
        [JsonProperty("server_id")]   public string ServerId = "";
        [JsonProperty("server_name")] public string ServerName = "";
        [JsonProperty("created_at")]  public long   CreatedUnix;
        [JsonProperty("server")]      public ReportDelivery Server = new ReportDelivery();
        [JsonProperty("developers")]  public ReportDelivery Developers = new ReportDelivery();

        [JsonIgnore] public string Stem = "";
        [JsonIgnore] public string Reference => KmhReportClient.Short(ReportId);
        [JsonIgnore] public bool Waiting => Server.IsPending || Developers.IsPending;
    }

    internal struct ReportRouteView
    {
        public bool   Connected;
        public string Mode;
        public int    Version;
        public string ServerId;
        public string ServerName;
        public bool   ServerRefusedForwarding;
        public bool   GlobalLinked;
        public bool   ServerSlotFree;
        public bool   GlobalSlotFree;

        public bool ServerTakesReports => Connected && (Mode == ReportModes.Local || Mode == ReportModes.Global);
        public bool ServerForwards     => ServerTakesReports && Mode == ReportModes.Global && !ServerRefusedForwarding;
        // Only a server that reads destinations may carry a developers-only report: an older one shows it to its staff.
        public bool ServerRelays       => ServerForwards && Version >= ReportModes.RoutingVersion;
    }

    internal enum ReportHop { None, Server, Global }

    internal struct ReportPlan
    {
        public ReportHop Hop;
        public bool CarryServer;
        public bool CarryDevelopers;
        public static readonly ReportPlan Nothing = new ReportPlan { Hop = ReportHop.None };
    }

    // Pure: every rule here is decided without the network, the game or the disk.
    internal static class ReportDeliveryRules
    {
        public const long FirstBackoffSeconds = 60, MaxBackoffSeconds = 6 * 3600;
        public const long RateLimitSeconds = 10 * 60, GlobalLimitSeconds = 3600, SignInSeconds = 3600;

        public static long Backoff(int attempts)
            => Math.Min(MaxBackoffSeconds, FirstBackoffSeconds << Math.Min(Math.Max(attempts - 1, 0), 12));

        public static bool SameServer(ReportOutboxEntry e, ReportRouteView v)
        {
            if (!string.IsNullOrEmpty(e.ServerId)) return e.ServerId == v.ServerId;
            return !string.IsNullOrEmpty(e.ServerName) && e.ServerName == v.ServerName;
        }

        public static ReportPlan Plan(ReportOutboxEntry e, ReportRouteView v, long now, bool ignoreBackoff)
        {
            // A copy in flight is not pending, so the other copy may go its own way meanwhile.
            if (e == null) return ReportPlan.Nothing;
            bool serverDue = e.Server.IsPending && (ignoreBackoff || e.Server.NextAttemptUnix <= now);
            bool devDue = e.Developers.IsPending && (ignoreBackoff || e.Developers.NextAttemptUnix <= now);

            // Signed in to Global, the developers' copy goes to Global itself: a server's word that it forwards proves nothing.
            if (devDue && v.GlobalSlotFree && v.GlobalLinked)
                return new ReportPlan { Hop = ReportHop.Global, CarryDevelopers = true };
            if (serverDue && v.ServerSlotFree && v.ServerTakesReports && SameServer(e, v))
                return new ReportPlan { Hop = ReportHop.Server, CarryServer = true,
                                        CarryDevelopers = e.Developers.IsPending && v.ServerForwards && !v.GlobalLinked };
            if (devDue && v.ServerSlotFree && v.ServerRelays && !v.GlobalLinked)
                return new ReportPlan { Hop = ReportHop.Server, CarryDevelopers = true };
            return ReportPlan.Nothing;
        }

        public static void Begin(ReportDelivery d, long now)
        {
            d.State = ReportDeliveryStates.Sending;
            d.Attempts++;
            d.UpdatedUnix = now;
        }

        public static void Delivered(ReportDelivery d, string via, long now)
        {
            d.State = ReportDeliveryStates.Sent;
            d.Reason = ""; d.Detail = "";
            d.Via = via;
            d.NextAttemptUnix = 0;
            d.UpdatedUnix = now;
        }

        public static void Wait(ReportDelivery d, string reason, long now, long atLeastSeconds = 0)
        {
            d.State = ReportDeliveryStates.Pending;
            d.Reason = reason ?? ReportReasons.Waiting;
            d.Detail = "";
            d.NextAttemptUnix = now + Math.Max(atLeastSeconds, Backoff(d.Attempts));
            d.UpdatedUnix = now;
        }

        // For a hop that is cheap to try at once, such as Global after a server that will not forward.
        public static void WaitNoDelay(ReportDelivery d, string reason, long now)
        {
            Wait(d, reason, now);
            d.NextAttemptUnix = now;
        }

        public static void Fail(ReportDelivery d, string reason, string detail, long now)
        {
            d.State = ReportDeliveryStates.Failed;
            d.Reason = reason ?? ReportReasons.Refused;
            d.Detail = Cap(detail, 200);
            d.NextAttemptUnix = 0;
            d.UpdatedUnix = now;
        }

        public static void ApplyServerAnswer(ReportOutboxEntry e, bool carriedServer, bool carriedDevelopers, ReportResultWire r, long now)
        {
            if (e == null || r == null) return;
            if (r.Ok)
            {
                if (carriedServer) Delivered(e.Server, ReportVia.Server, now);
                // An older server names no developers' answer and forwards whatever it keeps when it says "global".
                bool forwarded = r.Developers == ReportDevelopers.Forwarding
                                 || (string.IsNullOrEmpty(r.Developers) && r.Forwarding == ReportModes.Global);
                bool devOpen = e.Developers.IsSending || e.Developers.IsPending;
                // Only a copy this send carried can be settled: a server cannot answer for one it was never given.
                if (devOpen && carriedDevelopers)
                {
                    if (forwarded) Delivered(e.Developers, ReportVia.Server, now);
                    else WaitNoDelay(e.Developers, ReportReasons.NotForwarded, now);
                }
                return;
            }

            if (carriedServer)
            {
                switch (r.Reason)
                {
                    case ReportReasons.RateLimited: Wait(e.Server, ReportReasons.RateLimited, now, RateLimitSeconds); break;
                    case ReportReasons.Storage:     Wait(e.Server, ReportReasons.Storage, now); break;
                    case ReportReasons.Invalid:     Fail(e.Server, ReportReasons.Invalid, "", now); break;
                    default:                        Fail(e.Server, ReportReasons.Refused, Plain(string.IsNullOrEmpty(r.Message) ? r.Reason : r.Message), now); break;
                }
            }
            // The developers do not depend on this server: their copy is free to go another way at once.
            if (carriedDevelopers && e.Developers.IsSending)
                WaitNoDelay(e.Developers, r.Reason == ReportReasons.RateLimited ? ReportReasons.RateLimited : ReportReasons.NotForwarded, now);
        }

        public static void ApplyNoAnswer(ReportOutboxEntry e, bool carriedServer, bool carriedDevelopers, string reason, long now)
        {
            if (e == null) return;
            if (carriedServer && e.Server.IsSending) Wait(e.Server, reason, now);
            if (carriedDevelopers && e.Developers.IsSending) Wait(e.Developers, reason, now);
        }

        public static void ApplyGlobalAnswer(ReportOutboxEntry e, bool ok, int status, string code, bool rateLimited,
                                             TimeSpan retryAfter, long now)
        {
            // Settled another way meanwhile (an older server forwarded it): this answer changes nothing.
            if (e == null || !e.Developers.IsSending) return;
            ReportDelivery d = e.Developers;
            if (ok) { Delivered(d, ReportVia.Global, now); return; }
            if (code == "KMH-RPT-782" || rateLimited)
                Wait(d, ReportReasons.GlobalLimit, now, Math.Max(GlobalLimitSeconds, (long)retryAfter.TotalSeconds));
            else if (code == "KMH-RPT-781" || status == 400 || status == 413)
                Fail(d, ReportReasons.GlobalInvalid, code, now);
            else if (status == 401 || status == 403)
                Wait(d, ReportReasons.GlobalSignIn, now, SignInSeconds);
            else if (status == 409)
                Wait(d, ReportReasons.Waiting, now, 30);
            else if (status == 0 || status >= 500)
                Wait(d, ReportReasons.GlobalDown, now);
            else
                Fail(d, ReportReasons.Refused, string.IsNullOrEmpty(code) ? status.ToString() : code, now);
        }

        // Reconnecting or linking is news worth acting on at once, unless the peer itself asked for a pause.
        public static bool Kick(ReportDelivery d, long at)
        {
            if (!d.IsPending || ReportReasons.PeerAskedToWait(d.Reason) || d.NextAttemptUnix <= at) return false;
            d.NextAttemptUnix = at;
            return true;
        }

        internal static string Cap(string s, int max) => string.IsNullOrEmpty(s) ? "" : s.Length <= max ? s : s.Substring(0, max);

        // A peer's message is kept as words; its markup never reaches a label.
        internal static string Plain(string s) => string.IsNullOrEmpty(s) ? "" : System.Text.RegularExpressions.Regex.Replace(s, "<.*?>", string.Empty);
    }

    internal static class KmhReportOutbox
    {
        internal const string StateSuffix = ".delivery.json";

        private static readonly List<ReportOutboxEntry> _entries = new List<ReportOutboxEntry>();
        private static bool _loaded;

        internal static IReadOnlyList<ReportOutboxEntry> Entries => _entries;

        internal static ReportOutboxEntry Find(string reportId)
        {
            if (string.IsNullOrEmpty(reportId)) return null;
            foreach (ReportOutboxEntry e in _entries) if (e.ReportId == reportId) return e;
            return null;
        }

        internal static void Add(ReportOutboxEntry e)
        {
            if (e == null || Find(e.ReportId) != null) return;
            _entries.Add(e);
        }

        internal static bool Loaded => _loaded;

        internal static int Load(string folder, long now)
        {
            _loaded = true;
            int n = 0;
            foreach (ReportOutboxEntry e in ReadFolder(folder, now))
                if (Find(e.ReportId) == null) { _entries.Add(e); n++; }
            _entries.Sort((a, b) => a.CreatedUnix.CompareTo(b.CreatedUnix));
            return n;
        }

        // A send cut short by a crash is still owed, so it waits again rather than counting as gone.
        internal static List<ReportOutboxEntry> ReadFolder(string folder, long now)
        {
            var found = new List<ReportOutboxEntry>();
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return found;
            foreach (string path in Directory.GetFiles(folder, "*" + StateSuffix))
            {
                try
                {
                    ReportOutboxEntry e = JsonConvert.DeserializeObject<ReportOutboxEntry>(File.ReadAllText(path));
                    if (e == null || string.IsNullOrEmpty(e.ReportId)) continue;
                    e.Stem = path.Substring(0, path.Length - StateSuffix.Length);
                    if (!File.Exists(e.Stem + ".json")) continue;
                    e.Server ??= new ReportDelivery();
                    e.Developers ??= new ReportDelivery();
                    foreach (ReportDelivery d in new[] { e.Server, e.Developers })
                        if (d.IsSending) ReportDeliveryRules.Wait(d, ReportReasons.NoAnswer, now);
                    found.Add(e);
                }
                catch (Exception ex) { KmhLog.Debug($"Report outbox: skipped '{Path.GetFileName(path)}' ({ex.Message})."); }
            }
            return found;
        }

        internal static void Save(ReportOutboxEntry e)
        {
            if (e == null || string.IsNullOrEmpty(e.Stem)) return;
            try
            {
                Persistence.KmhAtomicFile.Write(e.Stem + StateSuffix, JsonConvert.SerializeObject(e, Formatting.Indented),
                                                new Persistence.KmhAtomicFile.Options { Durable = false });
            }
            catch (Exception ex) { KmhLog.Warn($"Report {e.Reference}: could not record its delivery state ({ex.Message})."); }
        }

        // The saved copy holds no picture: it sits beside it, written once.
        internal static ReportSubmit ReadSubmit(ReportOutboxEntry e)
        {
            if (e == null || string.IsNullOrEmpty(e.Stem) || !File.Exists(e.Stem + ".json")) return null;
            try
            {
                ReportSubmit s = JsonConvert.DeserializeObject<ReportSubmit>(File.ReadAllText(e.Stem + ".json"));
                if (s == null || s.ReportId != e.ReportId) return null;
                string shot = e.Stem + ".jpg";
                if (File.Exists(shot))
                {
                    byte[] bytes = File.ReadAllBytes(shot);
                    if (bytes.Length <= ReportLimits.MaxScreenshotBytes) s.Screenshot = Convert.ToBase64String(bytes);
                }
                return s;
            }
            catch (Exception ex)
            {
                KmhLog.Warn($"Report {e.Reference}: its saved copy could not be read ({ex.Message}).");
                return null;
            }
        }

        internal static bool IsPendingState(string statePath, DateTime nowUtc)
        {
            try
            {
                if (!File.Exists(statePath)) return false;
                if (nowUtc - File.GetLastWriteTimeUtc(statePath) > ReportLimits.KeepPendingFor) return false;
                ReportOutboxEntry e = JsonConvert.DeserializeObject<ReportOutboxEntry>(File.ReadAllText(statePath));
                return e != null && (e.Server?.IsPending == true || e.Developers?.IsPending == true
                                     || e.Server?.IsSending == true || e.Developers?.IsSending == true);
            }
            catch { return false; }
        }
    }
}
