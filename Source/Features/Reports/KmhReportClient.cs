using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using KMHPatch.Diagnostics;
using KMHPatch.SubProtocol;
using KMHPatch.UI;
using Newtonsoft.Json;
using Verse;

namespace KMHPatch.Features.Reports
{
    internal enum ReportPhase { Idle, Preparing, Active }

    // The dialog's report and every queued one share one outbox; each hop carries one report at a time.
    internal static class KmhReportClient
    {
        public static string ServerMode { get; private set; } = "";
        public static int ServerReportsVersion { get; private set; }

        public static ReportPhase Phase { get; private set; } = ReportPhase.Idle;
        public static string ShownId { get; private set; } = "";
        public static string SavedCopyPath { get; private set; } = "";

        private sealed class Flight
        {
            public string ReportId = "";
            public bool CarryServer, CarryDevelopers;
            public DateTime SentAtUtc;
        }

        private static Flight _serverFlight;
        private static string _globalFlight;
        private static bool _serverRefusedForwarding;
        private static long _serverQuietUntil;
        private static readonly Dictionary<string, ReportSubmit> _memory = new Dictionary<string, ReportSubmit>();

        private static bool _opening;
        private static DateTime _openingSinceUtc;
        private static float _nextScan;
        private static bool _scanSoon;
        private static bool _wasLinked;
        private static int _identity = -1;

        internal const float ScanSeconds = 5f;
        internal const long StartupDelaySeconds = 30, HelloDelaySeconds = 8, LinkDelaySeconds = 3;

        private static long Now => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        public static void Register() => KmhDispatcher.RegisterHandler(KmhProtocol.Kind.ReportResult, OnResult);

        internal static void ApplyHello(string mode, int version)
        {
            ServerMode = mode ?? "";
            ServerReportsVersion = Math.Max(0, version);
            _serverRefusedForwarding = false;
            _serverQuietUntil = 0;
            Kick(true, true, HelloDelaySeconds);
        }

        // Network thread: fields only here, the rest on the game's thread.
        internal static void ResetSession()
        {
            ServerMode = "";
            ServerReportsVersion = 0;
            KmhMainThread.Post(() =>
            {
                _serverRefusedForwarding = false;
                _serverQuietUntil = 0;
                Flight f = _serverFlight;
                if (f == null) return;
                _serverFlight = null;
                ReportOutboxEntry e = KmhReportOutbox.Find(f.ReportId);
                if (e == null) return;
                Change(e, () => ReportDeliveryRules.ApplyNoAnswer(e, f.CarryServer, f.CarryDevelopers, ReportReasons.Disconnected, Now));
            });
        }

        private static bool GlobalLinked
        {
            get
            {
                try { return Account.KmhAccountStore.HasToken(Global.GlobalHttp.BaseUrl); }
                catch { return false; }
            }
        }

        internal static ReportRouteView CurrentView()
            => new ReportRouteView
            {
                Connected = KmhDispatcher.IsKmhServer && !Enforcement.EnforcementAdmission.Pending,
                Mode = ServerMode,
                Version = ServerReportsVersion,
                ServerId = KmhDispatcher.ServerId ?? "",
                ServerName = KmhDispatcher.ServerName ?? "",
                ServerRefusedForwarding = _serverRefusedForwarding,
                GlobalLinked = GlobalLinked,
                ServerSlotFree = _serverFlight == null && Now >= _serverQuietUntil,
                GlobalSlotFree = _globalFlight == null,
            };

        public static bool ServerAvailable => CurrentView().ServerTakesReports;

        // Which hop the developers' copy takes right now: the same choice Plan makes.
        internal static ReportHop DevelopersRoute(ReportRouteView v, bool toServer)
        {
            if (v.ServerRelays || (toServer && v.ServerForwards)) return ReportHop.Server;
            return v.GlobalLinked ? ReportHop.Global : ReportHop.None;
        }

        // The server's own name; one dressed as Colony Nexus is just "this server" when a report's route is described.
        internal static string ServerLabel(string name)
            => string.IsNullOrEmpty(name) || UI.KmhBrandWords.ClaimsAuthority(name) ? KmhText.Get("KMH_Report_ThisServer", "this server") : name;

        internal static string HowText(ReportRouteView v, bool toServer, bool toDevelopers)
        {
            string server = ServerLabel(v.ServerName);
            var parts = new List<string>();
            if (toServer && v.ServerTakesReports)
                parts.Add(KmhText.Get("KMH_Report_HowItem", "{0}: {1}", KmhText.Get("KMH_Report_ToStaff", "This server's staff"),
                                      KmhText.Get("KMH_Report_HowViaServer", "via {0}", server)));
            if (toDevelopers)
            {
                ReportHop hop = DevelopersRoute(v, toServer);
                string how = hop == ReportHop.Server ? KmhText.Get("KMH_Report_HowViaServer", "via {0}", server)
                           : hop == ReportHop.Global ? KmhText.Get("KMH_Report_HowDirect", "directly with your linked Colony Nexus account")
                           : KmhText.Get("KMH_Report_HowLater", "saved and sent automatically as soon as possible");
                parts.Add(KmhText.Get("KMH_Report_HowItem", "{0}: {1}", KmhText.Get("KMH_Report_ToDevelopers", "Colony Nexus developers"), how));
            }
            string line = string.Join("   ·   ", parts.ToArray());
            // An older server forwards everything it keeps, whatever the player ticked.
            if (toServer && !toDevelopers && v.ServerForwards && v.Version < ReportModes.RoutingVersion)
                line += "  " + KmhText.Get("KMH_Report_OldServerForwards", "{0} passes every report on to the Colony Nexus developers too.", server);
            return line;
        }

        // Captured before the window opens, so the screenshot shows the game, not the window.
        public static void Open(string kind = ReportKinds.Bug, string title = "", string body = "")
        {
            // A capture that never called back must not leave the button dead.
            if (_opening && (DateTime.UtcNow - _openingSinceUtc).TotalSeconds < 5) return;
            if (Find.WindowStack?.WindowOfType<Dialog_KMHReport>() != null) return;
            _opening = true;
            _openingSinceUtc = DateTime.UtcNow;
            try
            {
                KmhScreenshot.Capture((jpg, why) =>
                {
                    _opening = false;
                    if (Find.WindowStack?.WindowOfType<Dialog_KMHReport>() != null) return;   // a late capture after a retry
                    KmhReportDiagnostics.Captured captured = null;
                    try { captured = KmhReportDiagnostics.CaptureOnMainThread(); }
                    catch (Exception ex) { KmhLog.Warn($"Report: diagnostics could not be gathered: {ex.Message}"); }
                    Find.WindowStack.Add(new Dialog_KMHReport(kind, title, body, jpg, why, captured));
                });
            }
            catch (Exception ex)
            {
                _opening = false;
                KmhLog.Warn($"Report: could not start: {ex.Message}");
                Find.WindowStack.Add(new Dialog_KMHReport(kind, title, body, null, ex.Message, null));
            }
        }

        public static ReportOutboxEntry Shown => KmhReportOutbox.Find(ShownId);

        internal static void Forget()
        {
            if (Phase == ReportPhase.Preparing) return;
            Phase = ReportPhase.Idle;
            ShownId = "";
            SavedCopyPath = "";
        }

        public static void Send(ReportFields fields, string body, byte[] screenshot, KmhReportDiagnostics.Captured diagnostics, bool attachDiagnostics)
        {
            if (Phase != ReportPhase.Idle || fields == null) return;
            ReportFields f = fields.Normalized();
            long now = Now;
            ReportRouteView v = CurrentView();
            var submit = new ReportSubmit
            {
                ReportId      = Guid.NewGuid().ToString("N"),
                Kind          = f.Kind,
                Title         = KmhReportDiagnostics.Cap((f.Title ?? "").Trim(), ReportLimits.MaxTitleChars),
                Body          = KmhReportDiagnostics.Cap((body ?? "").Trim(), ReportLimits.MaxBodyChars),
                ClientVersion = KmhProtocol.DisplayVersion + "+" + KmhProtocol.UiBuildTag,
                CreatedAtUnix = now,
                Destinations  = ReportDestinations.Of(f.ToServer, f.ToDevelopers),
                Area          = f.Area,
                Severity      = f.Severity,
                Frequency     = f.Frequency,
            };
            bool toServer = f.ToServer && v.ServerTakesReports;
            var entry = new ReportOutboxEntry
            {
                ReportId    = submit.ReportId,
                Title       = submit.Title,
                Kind        = submit.Kind,
                ServerId    = toServer ? v.ServerId : "",
                ServerName  = toServer ? v.ServerName : "",
                CreatedUnix = now,
                Server      = ReportDelivery.For(toServer, now),
                Developers  = ReportDelivery.For(f.ToDevelopers, now),
            };
            string delivery = HowText(v, toServer, f.ToDevelopers);
            string folder = CopiesFolder;
            Phase = ReportPhase.Preparing;
            ShownId = submit.ReportId;
            SavedCopyPath = "";

            Task.Run(() =>
            {
                string saved = "";
                try
                {
                    submit.ClientDiagnostics = KmhReportDiagnostics.Complete(diagnostics, f, attachDiagnostics, delivery, ReportLimits.MaxDiagnosticsChars);
                    submit.Screenshot = screenshot != null && screenshot.Length <= ReportLimits.MaxScreenshotBytes
                        ? Convert.ToBase64String(screenshot) : "";
                    saved = SaveCopy(folder, submit, screenshot);
                }
                catch (Exception ex) { KmhLog.Warn($"Report: preparing failed part-way: {ex.Message}"); }
                KmhMainThread.Post(() => Begin(entry, submit, saved));
            });
        }

        private static void Begin(ReportOutboxEntry e, ReportSubmit s, string saved)
        {
            e.Stem = string.IsNullOrEmpty(saved) ? "" : saved.Substring(0, saved.Length - ".json".Length);
            if (ShownId == e.ReportId)
            {
                SavedCopyPath = saved ?? "";
                Phase = ReportPhase.Active;
            }
            _memory[e.ReportId] = s;
            KmhReportOutbox.Add(e);
            KmhReportOutbox.Save(e);
            KmhLog.Info($"Report {e.Reference}: {e.Kind} \"{e.Title}\" for "
                      + $"{(e.Server.Chosen ? "server staff" : "")}{(e.Server.Chosen && e.Developers.Chosen ? " and " : "")}"
                      + $"{(e.Developers.Chosen ? "the developers" : "")}" + (e.Stem.Length == 0 ? " (no copy could be saved)" : ""));
            Pump(e, true);
            MarkQueued(e);
        }

        // Sent again by hand: the same id, so the server or Global keeps one copy.
        public static void Resend()
        {
            ReportOutboxEntry e = Shown;
            if (e == null) return;
            Pump(e, true);
            MarkQueued(e);
        }

        public static bool CanResend
        {
            get
            {
                ReportOutboxEntry e = Shown;
                return Phase == ReportPhase.Active && e != null && e.Waiting
                       && ReportDeliveryRules.Plan(e, CurrentView(), Now, true).Hop != ReportHop.None;
            }
        }

        // Waiting only for a free slot, not for a way to get there.
        private static void MarkQueued(ReportOutboxEntry e)
        {
            ReportRouteView open = CurrentView();
            open.ServerSlotFree = true;
            open.GlobalSlotFree = true;
            ReportPlan p = ReportDeliveryRules.Plan(e, open, Now, true);
            if (p.Hop == ReportHop.None) return;
            if (p.CarryServer && e.Server.IsPending && e.Server.Reason == ReportReasons.Waiting) e.Server.Reason = ReportReasons.Queued;
            if (p.CarryDevelopers && e.Developers.IsPending && e.Developers.Reason == ReportReasons.Waiting) e.Developers.Reason = ReportReasons.Queued;
        }

        private static void Pump(ReportOutboxEntry e, bool ignoreBackoff)
        {
            for (int hops = 0; hops < 2; hops++)
            {
                ReportPlan p = ReportDeliveryRules.Plan(e, CurrentView(), Now, ignoreBackoff);
                if (p.Hop == ReportHop.Server) StartServer(e, p);
                else if (p.Hop == ReportHop.Global) StartGlobal(e);
                else return;
            }
        }

        private static ReportSubmit SubmitFor(ReportOutboxEntry e)
            => _memory.TryGetValue(e.ReportId, out ReportSubmit s) ? s : KmhReportOutbox.ReadSubmit(e);

        private static void StartServer(ReportOutboxEntry e, ReportPlan p)
        {
            ReportSubmit s = SubmitFor(e);
            long now = Now;
            if (s == null)
            {
                Change(e, () =>
                {
                    if (p.CarryServer) ReportDeliveryRules.Fail(e.Server, ReportReasons.Missing, "", now);
                    if (p.CarryDevelopers) ReportDeliveryRules.Fail(e.Developers, ReportReasons.Missing, "", now);
                });
                return;
            }
            ReportSubmit copy = ServerCopy(s, p, ServerReportsVersion);

            Change(e, () =>
            {
                if (p.CarryServer) ReportDeliveryRules.Begin(e.Server, now);
                if (p.CarryDevelopers) ReportDeliveryRules.Begin(e.Developers, now);
            });
            _serverFlight = new Flight { ReportId = e.ReportId, CarryServer = p.CarryServer, CarryDevelopers = p.CarryDevelopers, SentAtUtc = DateTime.UtcNow };
            // The op id is the report id, so a chat retry is answered as the first.
            if (KmhDispatcher.Send(KmhProtocol.Kind.ReportSubmit, copy, e.ReportId)) return;
            _serverFlight = null;
            Change(e, () => ReportDeliveryRules.ApplyNoAnswer(e, p.CarryServer, p.CarryDevelopers, ReportReasons.CouldNotSend, now));
        }

        private static void StartGlobal(ReportOutboxEntry e)
        {
            ReportSubmit s = SubmitFor(e);
            long now = Now;
            if (s == null)
            {
                Change(e, () => ReportDeliveryRules.Fail(e.Developers, ReportReasons.Missing, "", now));
                return;
            }
            ReportSubmit copy = GlobalCopy(s);
            string token = "";
            ulong steam = 0;
            try
            {
                token = Account.KmhAccountStore.TokenFor(Global.GlobalHttp.BaseUrl);
                steam = Account.KmhSteamPersona.SteamId64();
            }
            catch { }
            Change(e, () => ReportDeliveryRules.Begin(e.Developers, now));
            _globalFlight = e.ReportId;
            string json = JsonConvert.SerializeObject(copy);
            string id = e.ReportId;
            Task.Run(() => PostToGlobalAsync(id, json, token, steam));
        }

        // Destinations name only what this hop carries; a server older than reports_v 2 refuses a kind it does not know.
        internal static ReportSubmit ServerCopy(ReportSubmit s, ReportPlan p, int serverVersion)
        {
            ReportSubmit copy = s.Copy(withScreenshot: true);
            copy.Destinations = ReportDestinations.Of(p.CarryServer, p.CarryDevelopers);
            if (serverVersion < ReportModes.RoutingVersion) copy.Kind = ReportKinds.ForOlderPeer(copy.Kind);
            return copy;
        }

        // Global reads the true kind from the summary; its own field knows only the first two.
        internal static ReportSubmit GlobalCopy(ReportSubmit s)
        {
            ReportSubmit copy = s.Copy(withScreenshot: true);
            copy.Kind = ReportKinds.ForOlderPeer(copy.Kind);
            copy.Destinations = ReportDestinations.Of(false, true);
            return copy;
        }

        private static async Task PostToGlobalAsync(string reportId, string json, string token, ulong steam)
        {
            string path = "/api/v1/player/reports" + (steam > 0 ? "?steam_id=" + steam : "");
            Global.GlobalHttp.Result r;
            try { r = await Global.GlobalHttp.PostUploadAsync(path, json, token, CancellationToken.None).ConfigureAwait(false); }
            catch (Exception ex) { r = new Global.GlobalHttp.Result { Status = 0, ErrorCode = ex.GetType().Name }; }

            KmhMainThread.Post(() =>
            {
                if (_globalFlight == reportId) _globalFlight = null;
                ReportOutboxEntry e = KmhReportOutbox.Find(reportId);
                if (e == null) return;
                Change(e, () => ReportDeliveryRules.ApplyGlobalAnswer(e, r.Ok, r.Status, r.ErrorCode, r.RateLimited, r.RetryAfter, Now));
                _scanSoon = true;
            });
        }

        private static void OnResult(KmhEnvelope env)
        {
            ReportResultWire r = env?.DataAs<ReportResultWire>();
            if (r == null || string.IsNullOrEmpty(r.ReportId)) return;
            ReportOutboxEntry e = KmhReportOutbox.Find(r.ReportId);
            if (e == null) return;
            Flight f = _serverFlight;
            bool mine = f != null && f.ReportId == r.ReportId;
            if (mine) _serverFlight = null;
            long now = Now;
            if (AcceptAnswer(e, mine, mine && f.CarryServer, mine && f.CarryDevelopers, r, now, out bool stopRelaying, out long quiet))
            {
                if (stopRelaying) _serverRefusedForwarding = true;
                if (quiet > 0) _serverQuietUntil = now + quiet;
                Pump(e, false);
                _scanSoon = true;
            }
        }

        // A late answer, after the wait ran out, still says the server kept the report.
        internal static bool AcceptAnswer(ReportOutboxEntry e, bool inFlight, bool carryServer, bool carryDevelopers, ReportResultWire r,
                                          long now, out bool stopRelaying, out long quietSeconds)
        {
            stopRelaying = false;
            quietSeconds = 0;
            if (e == null || r == null || r.ReportId != e.ReportId) return false;
            if (!inFlight)
            {
                if (!r.Ok || !e.Server.IsPending) return false;
                Change(e, () => ReportDeliveryRules.ApplyServerAnswer(e, true, false, r, now));
                return true;
            }
            Change(e, () => ReportDeliveryRules.ApplyServerAnswer(e, carryServer, carryDevelopers, r, now));
            if (carryDevelopers && (!r.Ok || r.Developers == ReportDevelopers.NotForwarded)) stopRelaying = true;
            if (!r.Ok) quietSeconds = r.Reason == ReportReasons.RateLimited ? ReportDeliveryRules.RateLimitSeconds : 60;
            return true;
        }

        public static void Tick()
        {
            float t = UnityEngine.Time.realtimeSinceStartup;
            if (!_scanSoon && t < _nextScan) return;
            _scanSoon = false;
            _nextScan = t + ScanSeconds;

            long now = Now;
            if (!KmhReportOutbox.Loaded)
            {
                int n = KmhReportOutbox.Load(CopiesFolder, now);
                Kick(true, true, StartupDelaySeconds);
                if (n > 0) KmhLog.Info($"Reports: {n} saved report(s) found; any still waiting is sent automatically.");
            }
            CheckTimeout(DateTime.UtcNow);
            WatchLink();
            var all = new List<ReportOutboxEntry>(KmhReportOutbox.Entries);
            foreach (ReportOutboxEntry e in all) if (e.Waiting) Pump(e, false);
        }

        private static void WatchLink()
        {
            bool linked = GlobalLinked;
            int generation = Global.KmhIdentityGeneration.Current;
            if (linked && (!_wasLinked || generation != _identity)) Kick(false, true, LinkDelaySeconds);
            _wasLinked = linked;
            _identity = generation;
        }

        private static void Kick(bool server, bool developers, long delaySeconds)
        {
            long at = Now + delaySeconds;
            foreach (ReportOutboxEntry e in KmhReportOutbox.Entries)
            {
                if (server) ReportDeliveryRules.Kick(e.Server, at);
                if (developers) ReportDeliveryRules.Kick(e.Developers, at);
            }
        }

        internal static void CheckTimeout(DateTime nowUtc)
        {
            Flight f = _serverFlight;
            if (f == null || (nowUtc - f.SentAtUtc).TotalSeconds < ReportLimits.ReplySeconds) return;
            _serverFlight = null;
            ReportOutboxEntry e = KmhReportOutbox.Find(f.ReportId);
            if (e == null) return;
            Change(e, () => ReportDeliveryRules.ApplyNoAnswer(e, f.CarryServer, f.CarryDevelopers, ReportReasons.NoAnswer, Now));
        }

        // Every state change must go through here, or it is never saved or announced.
        private static void Change(ReportOutboxEntry e, Action apply)
        {
            string server = e.Server.State, developers = e.Developers.State;
            apply();
            KmhReportOutbox.Save(e);
            if ((!e.Server.Chosen || e.Server.Settled) && (!e.Developers.Chosen || e.Developers.Settled)) _memory.Remove(e.ReportId);
            if (server != e.Server.State) Note(e, e.Server, true);
            if (developers != e.Developers.State) Note(e, e.Developers, false);
        }

        private static void Note(ReportOutboxEntry e, ReportDelivery d, bool staff)
        {
            string who = staff ? "the staff of " + ServerLabel(e.ServerName) : "the Colony Nexus developers";
            if (d.State == ReportDeliveryStates.Sent) KmhLog.Info($"Report {e.Reference} reached {who} (via {d.Via}).");
            else if (d.State == ReportDeliveryStates.Failed) KmhLog.Warn($"Report {e.Reference} could not be delivered to {who}: {d.Reason} {d.Detail}".TrimEnd());
            else if (d.State == ReportDeliveryStates.Pending) KmhLog.Debug($"Report {e.Reference} for {who} waits: {d.Reason}.");

            if (KmhLog.Muted || (d.State != ReportDeliveryStates.Sent && d.State != ReportDeliveryStates.Failed) || ShownInDialog(e)) return;
            string name = staff ? KmhText.Get("KMH_Report_StaffOf", "the staff of {0}", ServerLabel(e.ServerName))
                                : KmhText.Get("KMH_Report_TheDevelopers", "the Colony Nexus developers");
            try
            {
                // A server relaying the developers' copy is taken at its word, and the toast says only what is known.
                if (d.State == ReportDeliveryStates.Sent && !staff && d.Via == ReportVia.Server)
                    Notifications.KmhNotifications.Positive(KmhText.Get("KMH_Report_RelayedToast", "Your report {0} was handed to {1}, which says it passes reports on to the Colony Nexus developers.",
                                                                        e.Reference, ServerLabel(e.ServerName)));
                else if (d.State == ReportDeliveryStates.Sent)
                    Notifications.KmhNotifications.Positive(KmhText.Get("KMH_Report_DeliveredToast", "Your report {0} reached {1}.", e.Reference, name));
                else
                    Notifications.KmhNotifications.Negative(KmhText.Get("KMH_Report_FailedToast", "Your report {0} could not be delivered to {1}: {2}.",
                                                                        e.Reference, name, ReasonText(d)));
            }
            catch { }
        }

        private static bool ShownInDialog(ReportOutboxEntry e)
        {
            if (e.ReportId != ShownId) return false;
            try { return Find.WindowStack?.WindowOfType<Dialog_KMHReport>() != null; } catch { return false; }
        }

        internal static string ReasonText(ReportDelivery d)
        {
            switch (d?.Reason ?? "")
            {
                case ReportReasons.Queued:        return KmhText.Get("KMH_Report_Why_Queued", "waiting for another report to finish");
                case ReportReasons.NoAnswer:      return KmhText.Get("KMH_Report_Why_NoAnswer", "the server did not answer");
                case ReportReasons.Disconnected:  return KmhText.Get("KMH_Report_Why_Disconnected", "the connection ended before the server answered");
                case ReportReasons.CouldNotSend:  return KmhText.Get("KMH_Report_Why_CouldNotSend", "it could not be sent just now");
                case ReportReasons.RateLimited:   return KmhText.Get("KMH_Report_Why_RateLimited", "several reports in a short time - the server asked for a pause");
                case ReportReasons.Storage:       return KmhText.Get("KMH_Report_Why_Storage", "the server could not save it");
                case ReportReasons.Invalid:       return KmhText.Get("KMH_Report_Why_Invalid", "refused as incomplete");
                case ReportReasons.NotForwarded:  return KmhText.Get("KMH_Report_Why_NotForwarded", "this server does not pass reports on");
                case ReportReasons.GlobalDown:    return KmhText.Get("KMH_Report_Why_GlobalDown", "Colony Nexus Global did not answer");
                case ReportReasons.GlobalLimit:   return KmhText.Get("KMH_Report_Why_GlobalLimit", "Global's limit for today is reached");
                case ReportReasons.GlobalSignIn:  return KmhText.Get("KMH_Report_Why_GlobalSignIn", "Global did not accept your account - link it again under Account & links");
                case ReportReasons.GlobalInvalid: return KmhText.Get("KMH_Report_Why_GlobalInvalid", "Global refused it as incomplete");
                case ReportReasons.Missing:       return KmhText.Get("KMH_Report_Why_Missing", "its saved copy is gone from this computer");
                case ReportReasons.Refused:
                    return KmhText.Get("KMH_Report_Why_Refused", "refused: {0}",
                                       string.IsNullOrEmpty(d.Detail) ? "?" : KmhDisplayText.Inert(ReportDeliveryRules.Plain(d.Detail), 200));
                default:                          return KmhText.Get("KMH_Report_Why_Waiting", "nobody can take it right now");
            }
        }

        internal static string Short(string id) => string.IsNullOrEmpty(id) ? "" : id.Length <= 8 ? id : id.Substring(0, 8);

        public static string CopiesFolder
        {
            get
            {
                try { return Path.Combine(Persistence.KmhDataFolder.Root, "Reports"); }
                catch { return ""; }
            }
        }

        // So nothing typed is lost to a refused send; the picture sits beside it, never inside.
        internal static string SaveCopy(string folder, ReportSubmit s, byte[] screenshot)
        {
            if (string.IsNullOrEmpty(folder)) return "";
            try
            {
                Directory.CreateDirectory(folder);
                string stem = DateTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture) + "-" + Short(s.ReportId);
                string json = Path.Combine(folder, stem + ".json");
                // Written whole, but not worth a forced flush.
                var notDurable = new Persistence.KmhAtomicFile.Options { Durable = false };
                Persistence.KmhAtomicFile.Write(json, JsonConvert.SerializeObject(s.Copy(withScreenshot: false), Formatting.Indented), notDurable);
                if (screenshot != null && screenshot.Length > 0)
                    Persistence.KmhAtomicFile.WriteBytes(Path.Combine(folder, stem + ".jpg"), screenshot, notDurable);
                Prune(folder, ReportLimits.KeepLocalCopies);
                return json;
            }
            catch (Exception ex)
            {
                KmhLog.Warn($"Report: could not save a copy in '{folder}': {ex.Message}");
                return "";
            }
        }

        // A copy still owed to someone outlives the keep limit until it is delivered or a month old.
        internal static void Prune(string folder, int keep)
        {
            var reports = new List<FileInfo>();
            foreach (FileInfo f in new DirectoryInfo(folder).GetFiles("*.json"))
                if (!f.Name.EndsWith(KmhReportOutbox.StateSuffix, StringComparison.OrdinalIgnoreCase)) reports.Add(f);
            if (reports.Count <= keep) return;
            reports.Sort((a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));
            DateTime now = DateTime.UtcNow;
            for (int i = keep; i < reports.Count; i++)
            {
                string stem = reports[i].FullName.Substring(0, reports[i].FullName.Length - ".json".Length);
                if (KmhReportOutbox.IsPendingState(stem + KmhReportOutbox.StateSuffix, now)) continue;
                try
                {
                    foreach (string extra in new[] { stem + ".jpg", stem + KmhReportOutbox.StateSuffix })
                        if (File.Exists(extra)) File.Delete(extra);
                    reports[i].Delete();
                }
                catch { }
            }
        }
    }
}
