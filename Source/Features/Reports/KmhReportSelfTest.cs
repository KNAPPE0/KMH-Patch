using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KMHPatch.SubProtocol;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace KMHPatch.Features.Reports
{
    // Fixtures only: no check reads or changes the live outbox, connection or dialog.
    internal static class KmhReportSelfTest
    {
        private const long T0 = 1_760_000_000;

        public static List<(string, bool, string)> Run()
        {
            var r = new List<(string, bool, string)>();
            void Check(string name, Func<(bool ok, string detail)> probe)
            {
                try { (bool ok, string detail) = probe(); r.Add((name, ok, detail)); }
                catch (Exception ex) { r.Add((name, false, $"threw: {ex.GetType().Name}: {ex.Message}")); }
            }

            Check("Report: the wire names are the server's and Global's", () =>
            {
                var names = JObject.FromObject(new ReportSubmit()).Properties().Select(p => p.Name).OrderBy(n => n).ToArray();
                string[] want = { "area", "body", "client_diagnostics", "client_version", "created_at", "destinations", "frequency", "kind",
                                  "report_id", "screenshot", "severity", "title" };
                var answer = JObject.FromObject(new ReportResultWire()).Properties().Select(p => p.Name).OrderBy(n => n).ToArray();
                string[] wantAnswer = { "developers", "forwarding", "message", "ok", "reason", "reference", "report_id" };
                bool ok = names.SequenceEqual(want) && answer.SequenceEqual(wantAnswer);
                return (ok, string.Join(",", names) + " | " + string.Join(",", answer));
            });

            Check("Report: the server's answer settles only the report it names", () =>
            {
                ReportOutboxEntry e = Entry(server: true, developers: true);
                ReportDeliveryRules.Begin(e.Server, T0);
                ReportDeliveryRules.Begin(e.Developers, T0);
                bool ignored = !KmhReportClient.AcceptAnswer(e, true, true, true, Answer("someone-elses-id", true, "global", ReportDevelopers.Forwarding),
                                                             T0, out _, out _)
                               && e.Server.IsSending && e.Developers.IsSending;
                bool taken = KmhReportClient.AcceptAnswer(e, true, true, true, Answer(e.ReportId, true, "global", ReportDevelopers.Forwarding), T0,
                                                          out bool stop, out _);
                bool sent = taken && e.Server.State == ReportDeliveryStates.Sent && e.Developers.State == ReportDeliveryStates.Sent
                            && e.Developers.Via == ReportVia.Server && !stop;
                return (ignored && sent, $"another id ignored: {ignored}; its own id: server {e.Server.State}, developers {e.Developers.State} via {e.Developers.Via}");
            });

            Check("Report: a refusal says why in words, and a server's markup reads as text", () =>
            {
                ReportOutboxEntry e = Entry(server: true, developers: false);
                ReportDeliveryRules.Begin(e.Server, T0);
                ReportDeliveryRules.ApplyServerAnswer(e, true, false, new ReportResultWire { ReportId = e.ReportId, Reason = "new_reason",
                                                      Message = "<color=red>Nope</color>" }, T0);
                string refused = KmhReportClient.ReasonText(e.Server);
                string limited = KmhReportClient.ReasonText(new ReportDelivery { Reason = ReportReasons.RateLimited });
                string signIn = KmhReportClient.ReasonText(new ReportDelivery { Reason = ReportReasons.GlobalSignIn });
                bool ok = e.Server.State == ReportDeliveryStates.Failed && refused.Contains("Nope") && !refused.Contains("<color")
                          && limited == UI.KmhText.Get("KMH_Report_Why_RateLimited", "several reports in a short time - the server asked for a pause")
                          && signIn == UI.KmhText.Get("KMH_Report_Why_GlobalSignIn", "Global did not accept your account - link it again under Account & links")
                          && limited != signIn;
                return (ok, $"unknown refusal: \"{refused}\" ({e.Server.State}); rate limit: \"{limited}\"");
            });

            Check("Report: an answer that never comes becomes a wait with a retry time, not a spinner", () =>
            {
                ReportOutboxEntry e = Entry(server: true, developers: true);
                ReportDeliveryRules.Begin(e.Server, T0);
                ReportDeliveryRules.Begin(e.Developers, T0);
                ReportDeliveryRules.ApplyNoAnswer(e, true, true, ReportReasons.NoAnswer, T0 + 45);
                bool ok = e.Server.IsPending && e.Developers.IsPending && e.Server.Reason == ReportReasons.NoAnswer
                          && e.Server.NextAttemptUnix > T0 + 45 && e.Server.Attempts == 1;
                return (ok, $"server {e.Server.State}/{e.Server.Reason} next in {e.Server.NextAttemptUnix - T0 - 45}s; developers {e.Developers.State}");
            });

            Check("Report: Global's answers become states - a limit waits, an incomplete report is final, an outage retries", () =>
            {
                ReportDelivery Run1(bool ok, int status, string code, bool limited)
                {
                    ReportOutboxEntry e = Entry(server: false, developers: true);
                    ReportDeliveryRules.Begin(e.Developers, T0);
                    ReportDeliveryRules.ApplyGlobalAnswer(e, ok, status, code, limited, TimeSpan.Zero, T0);
                    return e.Developers;
                }
                ReportDelivery sent = Run1(true, 201, "", false);
                ReportDelivery limit = Run1(false, 429, "KMH-RPT-782", true);
                ReportDelivery invalid = Run1(false, 400, "KMH-RPT-781", false);
                ReportDelivery down = Run1(false, 0, "KMH-NET-001", false);
                ReportDelivery signIn = Run1(false, 401, "", false);
                bool ok = sent.State == ReportDeliveryStates.Sent && sent.Via == ReportVia.Global
                          && limit.IsPending && limit.NextAttemptUnix - T0 >= ReportDeliveryRules.GlobalLimitSeconds
                          && invalid.State == ReportDeliveryStates.Failed
                          && down.IsPending && down.Reason == ReportReasons.GlobalDown
                          && signIn.IsPending && signIn.Reason == ReportReasons.GlobalSignIn;
                return (ok, $"201 {sent.State}; 429 {limit.State} for {limit.NextAttemptUnix - T0}s; 400 {invalid.State}; 0 {down.State}; 401 {signIn.State}");
            });

            Check("Report: a staff-only report never goes to Global or rides to the developers", () =>
            {
                ReportOutboxEntry e = Entry(server: true, developers: false);
                ReportRouteView v = View(ReportModes.Global, ReportModes.RoutingVersion, linked: true);
                ReportPlan p = ReportDeliveryRules.Plan(e, v, T0, true);
                ReportSubmit copy = KmhReportClient.ServerCopy(Submit(e.ReportId, ReportKinds.Bug), p, v.Version);
                ReportDeliveryRules.Begin(e.Server, T0);
                ReportPlan then = ReportDeliveryRules.Plan(e, v, T0, true);
                ReportDeliveryRules.ApplyNoAnswer(e, true, false, ReportReasons.NoAnswer, T0);
                v.Connected = false;
                ReportPlan offline = ReportDeliveryRules.Plan(e, v, T0 + 100_000, true);
                bool ok = p.Hop == ReportHop.Server && p.CarryServer && !p.CarryDevelopers
                          && copy.Destinations.SequenceEqual(new[] { ReportDestinations.Server })
                          && then.Hop == ReportHop.None && offline.Hop == ReportHop.None;
                return (ok, $"first hop {p.Hop} (developers carried: {p.CarryDevelopers}, destinations {string.Join(",", copy.Destinations)}); "
                          + $"while sending: {then.Hop}; offline but linked: {offline.Hop}");
            });

            Check("Report: a developers-only report is never handed to a server that would show it to its staff", () =>
            {
                ReportOutboxEntry e = Entry(server: false, developers: true);
                ReportPlan older = ReportDeliveryRules.Plan(e, View(ReportModes.Global, 0, linked: true), T0, true);
                ReportPlan olderUnlinked = ReportDeliveryRules.Plan(e, View(ReportModes.Global, 0, linked: false), T0, true);
                ReportPlan relay = ReportDeliveryRules.Plan(e, View(ReportModes.Global, ReportModes.RoutingVersion, linked: false), T0, true);
                ReportPlan local = ReportDeliveryRules.Plan(e, View(ReportModes.Local, ReportModes.RoutingVersion, linked: false), T0, true);
                ReportSubmit copy = KmhReportClient.ServerCopy(Submit(e.ReportId, ReportKinds.Bug), relay, ReportModes.RoutingVersion);
                bool ok = older.Hop == ReportHop.Global && olderUnlinked.Hop == ReportHop.None
                          && relay.Hop == ReportHop.Server && !relay.CarryServer && relay.CarryDevelopers
                          && copy.Destinations.SequenceEqual(new[] { ReportDestinations.Developers }) && local.Hop == ReportHop.None;
                return (ok, $"older server, linked: {older.Hop}; older, unlinked: {olderUnlinked.Hop}; forwarding server: {relay.Hop}; local-only server: {local.Hop}");
            });

            Check("Report: the destination line follows the server's word and the account link", () =>
            {
                ReportRouteView fwd = View(ReportModes.Global, ReportModes.RoutingVersion, linked: false);
                ReportRouteView localLinked = View(ReportModes.Local, ReportModes.RoutingVersion, linked: true);
                ReportRouteView nothing = View(ReportModes.Off, ReportModes.RoutingVersion, linked: false);
                ReportRouteView olderFwd = View(ReportModes.Global, 0, linked: false);
                string a = KmhReportClient.HowText(fwd, true, true);
                string b = KmhReportClient.HowText(localLinked, true, true);
                string c = KmhReportClient.HowText(nothing, false, true);
                string d = KmhReportClient.HowText(olderFwd, true, false);
                string via = UI.KmhText.Get("KMH_Report_HowViaServer", "via {0}", "Test Colony");
                string direct = UI.KmhText.Get("KMH_Report_HowDirect", "directly with your linked Colony Nexus account");
                string later = UI.KmhText.Get("KMH_Report_HowLater", "saved and sent automatically as soon as possible");
                string older = UI.KmhText.Get("KMH_Report_OldServerForwards", "{0} passes every report on to the Colony Nexus developers too.", "Test Colony");
                bool ok = a.Split(new[] { via }, StringSplitOptions.None).Length == 3 && !a.Contains(direct) && b.Contains(direct)
                          && c.Contains(later) && !c.Contains(via) && !nothing.ServerTakesReports && d.Contains(older);
                return (ok, $"forwarding: \"{a}\" | local + linked: \"{b}\" | nobody: \"{c}\"");
            });

            Check("Report: delivery states go pending, sending, sent - and only a refusal no retry can change is final", () =>
            {
                ReportOutboxEntry e = Entry(server: true, developers: true);
                bool startPending = e.Server.IsPending && e.Developers.IsPending;
                ReportDeliveryRules.Begin(e.Server, T0);
                ReportDeliveryRules.Begin(e.Developers, T0);
                ReportDeliveryRules.ApplyServerAnswer(e, true, true, new ReportResultWire { ReportId = e.ReportId, Reason = ReportReasons.RateLimited }, T0);
                bool limited = e.Server.IsPending && e.Server.NextAttemptUnix - T0 >= ReportDeliveryRules.RateLimitSeconds
                               && e.Developers.IsPending && e.Developers.NextAttemptUnix == T0;
                bool kickKept = !ReportDeliveryRules.Kick(e.Server, T0 + 5) && e.Server.NextAttemptUnix - T0 >= ReportDeliveryRules.RateLimitSeconds;
                ReportDeliveryRules.Begin(e.Server, T0 + 700);
                ReportDeliveryRules.ApplyServerAnswer(e, true, false, new ReportResultWire { ReportId = e.ReportId, Reason = ReportReasons.Storage }, T0 + 700);
                bool storage = e.Server.IsPending && e.Server.Reason == ReportReasons.Storage;
                ReportDeliveryRules.Begin(e.Server, T0 + 800);
                ReportDeliveryRules.ApplyServerAnswer(e, true, false, new ReportResultWire { ReportId = e.ReportId, Reason = ReportReasons.Invalid }, T0 + 800);
                bool final = e.Server.State == ReportDeliveryStates.Failed
                             && ReportDeliveryRules.Plan(e, View(ReportModes.Local, 2, false), T0 + 100_000, true).Hop == ReportHop.None;
                bool backoff = ReportDeliveryRules.Backoff(1) == 60 && ReportDeliveryRules.Backoff(3) == 240
                               && ReportDeliveryRules.Backoff(40) == ReportDeliveryRules.MaxBackoffSeconds;
                bool ok = startPending && limited && kickKept && storage && final && backoff;
                return (ok, $"pending at first: {startPending}; rate limit waits and frees the developers' copy: {limited}; "
                          + $"a reconnect keeps the server's pause: {kickKept}; storage retries: {storage}; invalid is final: {final}; backoff: {backoff}");
            });

            Check("Report: a server that will not forward sends the developers' copy another way at once", () =>
            {
                ReportOutboxEntry e = Entry(server: true, developers: true);
                ReportDeliveryRules.Begin(e.Server, T0);
                ReportDeliveryRules.Begin(e.Developers, T0);
                bool applied = KmhReportClient.AcceptAnswer(e, true, true, true,
                    Answer(e.ReportId, true, "local", ReportDevelopers.NotForwarded), T0, out bool stop, out _);
                ReportRouteView v = View(ReportModes.Global, ReportModes.RoutingVersion, linked: true);
                v.ServerRefusedForwarding = stop;
                ReportPlan next = ReportDeliveryRules.Plan(e, v, T0, false);
                // An older server names no developers' answer: "global" means it forwards whatever it keeps.
                ReportOutboxEntry old = Entry(server: true, developers: true);
                ReportDeliveryRules.Begin(old.Server, T0);
                ReportDeliveryRules.Begin(old.Developers, T0);
                ReportDeliveryRules.ApplyServerAnswer(old, true, true, Answer(old.ReportId, true, "global", ""), T0);
                bool ok = applied && stop && e.Server.State == ReportDeliveryStates.Sent && e.Developers.IsPending
                          && e.Developers.Reason == ReportReasons.NotForwarded && next.Hop == ReportHop.Global
                          && old.Developers.State == ReportDeliveryStates.Sent && old.Developers.Via == ReportVia.Server;
                return (ok, $"staff {e.Server.State}; developers {e.Developers.State}/{e.Developers.Reason}, next hop {next.Hop}; "
                          + $"older forwarding server counts as delivered: {old.Developers.State}");
            });

            Check("Report: signed in to Global, the developers' copy goes to Global itself and no server can settle it", () =>
            {
                ReportOutboxEntry e = Entry(server: true, developers: true);
                ReportRouteView v = View(ReportModes.Global, ReportModes.RoutingVersion, linked: true);
                ReportPlan first = ReportDeliveryRules.Plan(e, v, T0, true);
                ReportDeliveryRules.Begin(e.Developers, T0);
                ReportPlan meanwhile = ReportDeliveryRules.Plan(e, v, T0, true);
                ReportDeliveryRules.Begin(e.Server, T0);
                ReportDeliveryRules.ApplyServerAnswer(e, true, false, Answer(e.ReportId, true, "global", ReportDevelopers.Forwarding), T0);
                bool untouched = e.Server.State == ReportDeliveryStates.Sent && e.Developers.IsSending;

                ReportOutboxEntry late = Entry(server: true, developers: true);
                KmhReportClient.AcceptAnswer(late, false, false, false, Answer(late.ReportId, true, "global", ""), T0, out _, out _);
                bool lateKept = late.Server.State == ReportDeliveryStates.Sent && late.Developers.IsPending;

                // Global busy with another upload: the copy still waits for it rather than riding with the server's.
                v.GlobalSlotFree = false;
                ReportPlan busy = ReportDeliveryRules.Plan(Entry(server: true, developers: true), v, T0, true);
                ReportPlan devOnly = ReportDeliveryRules.Plan(Entry(server: false, developers: true), v, T0, true);
                bool ok = first.Hop == ReportHop.Global && first.CarryDevelopers && !first.CarryServer
                          && meanwhile.Hop == ReportHop.Server && meanwhile.CarryServer && !meanwhile.CarryDevelopers
                          && untouched && lateKept && busy.Hop == ReportHop.Server && !busy.CarryDevelopers && devOnly.Hop == ReportHop.None;
                return (ok, $"first {first.Hop}; meanwhile {meanwhile.Hop} (developers {meanwhile.CarryDevelopers}); server's word on a copy it never had: "
                          + $"{e.Developers.State}; late answer: {late.Developers.State}; Global busy: {busy.Hop}/{busy.CarryDevelopers}, developers-only {devOnly.Hop}");
            });

            Check("Report: an older server and Global are sent only the kinds they know", () =>
            {
                ReportSubmit q = Submit("qqqq0000", ReportKinds.Question);
                ReportPlan plan = new ReportPlan { Hop = ReportHop.Server, CarryServer = true };
                string older = KmhReportClient.ServerCopy(q, plan, 0).Kind;
                string newer = KmhReportClient.ServerCopy(q, plan, ReportModes.RoutingVersion).Kind;
                string global = KmhReportClient.GlobalCopy(q).Kind;
                bool kept = q.Kind == ReportKinds.Question && ReportKinds.Normalize("rant") == ReportKinds.Bug;
                bool ok = older == ReportKinds.Suggestion && newer == ReportKinds.Question && global == ReportKinds.Suggestion && kept;
                return (ok, $"older server gets '{older}', a newer one '{newer}', Global '{global}'; the saved copy keeps '{q.Kind}'");
            });

            Check("Report: the fingerprint ignores numbers, ids and paths, and changes with the version or the fault", () =>
            {
                string a = KmhGameLogWatch.Signature("<color=#f00>Exception</color> in Tick 4512 at C:\\Profiles\\Ann\\Mods\\x.dll id=0xDEADBEEF01\n  at Frame()");
                string b = KmhGameLogWatch.Signature("Exception in Tick 99 at D:\\Steam\\Mods\\x.dll id=0x1234567890");
                string fa = KmhGameLogWatch.Fingerprint("1.3.1", a, "whatever");
                string fb = KmhGameLogWatch.Fingerprint("1.3.1", b, "other title");
                string fv = KmhGameLogWatch.Fingerprint("1.3.2", a, "whatever");
                string ff = KmhGameLogWatch.Fingerprint("1.3.1", KmhGameLogWatch.Signature("NullReferenceException in Draw"), "whatever");
                string t1 = KmhGameLogWatch.Fingerprint("1.3.1", "", "Market  Freezes ");
                string t2 = KmhGameLogWatch.Fingerprint("1.3.1", "", "market freezes");
                bool ok = a == b && fa == fb && fa != fv && fa != ff && t1 == t2 && fa.Length == 12 && !a.Contains("Ann") && !a.Contains("4512");
                return (ok, $"signature \"{a}\"; same fault {fa}={fb}; other version {fv}; other fault {ff}; title {t1}={t2}");
            });

            Check("Report: the player's choices reach client_diagnostics.summary under the names Global reads", () =>
            {
                var fields = new ReportFields { Kind = ReportKinds.Suggestion, Area = "marketplace", Severity = ReportSeverities.Blocks,
                                                Frequency = ReportFrequencies.Once, ToServer = true, ToDevelopers = true, Title = "Sort by price" };
                var captured = new KmhReportDiagnostics.Captured
                {
                    Root = null, TopError = "",
                    Versions = new JObject { ["kmh_version"] = "1.3.1", ["build"] = "rel-279", ["protocol"] = 3, ["rimworld"] = "1.7", ["rimworld_together"] = "26.10.4.1" },
                };
                JObject minimal = JObject.Parse(KmhReportDiagnostics.Complete(captured, fields, false, "how", ReportLimits.MaxDiagnosticsChars));
                var s = (JObject)minimal["summary"];
                var bug = new JObject { ["summary"] = new JObject { ["kmh_version"] = "1.3.1" }, ["mods"] = new JArray() };
                KmhReportDiagnostics.Stamp(bug, new ReportFields { Kind = "bug", Area = "nowhere", Severity = "minor", Frequency = "always",
                                                                   ToDevelopers = true, Title = "x" }, "", "");
                var bs = (JObject)bug["summary"];
                bool names = new[] { "report_kind", "area", "severity", "frequency", "destinations", "fingerprint",
                                     "kmh_version", "build", "protocol", "rimworld", "rimworld_together" }.All(k => s[k] != null);
                bool values = (string)s["report_kind"] == "suggestion" && (string)s["area"] == "marketplace" && (string)s["severity"] == ""
                              && (string)s["frequency"] == "" && (string)s["destinations"] == "server,developers"
                              && ((string)s["fingerprint"]).Length == 12 && minimal.Properties().Count() == 1;
                bool normalized = (string)bs["area"] == "general" && (string)bs["severity"] == "minor" && (string)bs["frequency"] == "always"
                                  && (string)bs["destinations"] == "developers" && (string)bs["kmh_version"] == "1.3.1";
                return (names && values && normalized, minimal.ToString(Formatting.None));
            });

            Check("Report: recent requests pair answers by op id and by feature, keep no content, and stop at twenty", () =>
            {
                var log = new KmhRequestLog();
                long f = System.Diagnostics.Stopwatch.Frequency;
                log.Sent(KmhProtocol.Kind.MarketplaceBuy, "op-1", true, DateTime.Now, 0);
                log.Sent(KmhProtocol.Kind.Ping, null, true, DateTime.Now, 0);
                log.Sent(KmhProtocol.Kind.ReportSubmit, "rep-1", true, DateTime.Now, 0);
                log.Sent(KmhProtocol.Kind.QuestRequest, null, false, DateTime.Now, 0);
                log.Received(Env(KmhProtocol.Kind.ReportResult, new JObject { ["ok"] = false, ["reason"] = "rate_limited", ["message"] = "secret words" }), f / 4);
                log.Received(Env(KmhProtocol.Kind.OpResult, new JObject { ["op"] = "op-1" }), f / 2);
                JArray rows = log.Snapshot(f * 60);
                string all = rows.ToString(Formatting.None);
                bool shape = rows.Count == 3 && (string)rows[0]["kind"] == KmhProtocol.Kind.MarketplaceBuy && (long)rows[0]["ms"] == 500
                             && (string)rows[1]["answered_by"] == KmhProtocol.Kind.ReportResult && (long)rows[1]["ms"] == 250
                             && (string)rows[1]["code"] == "rate_limited" && (bool)rows[1]["ok"] == false
                             && (string)rows[2]["result"] == "not sent" && !all.Contains("secret") && !all.Contains("kmh.ping");
                for (int i = 0; i < 30; i++) log.Sent(KmhProtocol.Kind.SiteRequest, null, true, DateTime.Now, 0);
                bool capped = log.Count == KmhRequestLog.Kept;
                return (shape && capped, $"{all}; after 33 sends {log.Count} kept");
            });

            Check("Report: repeated warnings and errors fold into one group with a count and first and last sightings", () =>
            {
                var table = new KmhGameLogWatch.GroupTable(3);
                DateTime t = new DateTime(2026, 10, 15, 12, 0, 0);
                table.Observe("error", "Could not load pawn 41 from C:\\Saves\\a.rws", t, 1);
                table.Observe("error", "Could not load pawn 7 from D:\\Other\\b.rws", t.AddMinutes(5), 3);
                table.Observe("warning", "Slow tick 120ms", t.AddMinutes(1), 1);
                table.Observe("warning", "Another 1", t.AddMinutes(2), 1);
                table.Observe("warning", "Third kind", t.AddMinutes(3), 1);
                List<KmhGameLogWatch.Group> top = table.Top(10);
                KmhGameLogWatch.Group first = top.FirstOrDefault();
                bool ok = table.Count == 3 && first != null && first.Level == "error" && first.Count == 4 && first.First == t
                          && first.Last == t.AddMinutes(5) && table.TopErrorSignature() == first.Signature
                          && !top.Any(g => g.Signature.StartsWith("Slow tick"));
                JArray section = table.Section(10);
                return (ok && (string)section[0]["first_seen"] == "2026-10-15 12:00:00",
                        $"{table.Count} group(s); top {first?.Level} x{first?.Count} '{first?.Signature}' {first?.First:HH:mm}-{first?.Last:HH:mm}");
            });

            Check("Report: a report waiting for delivery outlives the twenty-copy limit and survives a restart", () =>
            {
                string dir = Path.Combine(Path.GetTempPath(), "kmh-report-outbox-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                try
                {
                    var waiting = new List<string>();
                    for (int i = 0; i < ReportLimits.KeepLocalCopies + 3; i++)
                    {
                        ReportSubmit s = Submit(Guid.NewGuid().ToString("N"), ReportKinds.Bug);
                        string saved = KmhReportClient.SaveCopy(dir, s, null);
                        if (string.IsNullOrEmpty(saved)) return (false, $"copy {i} was not saved");
                        File.SetLastWriteTimeUtc(saved, DateTime.UtcNow.AddMinutes(-100 + i));
                        if (i < 2)
                        {
                            ReportOutboxEntry e = Entry(server: false, developers: true);
                            e.ReportId = s.ReportId;
                            e.Stem = saved.Substring(0, saved.Length - ".json".Length);
                            if (i == 0) ReportDeliveryRules.Begin(e.Developers, T0);
                            KmhReportOutbox.Save(e);
                            waiting.Add(s.ReportId);
                        }
                    }
                    KmhReportClient.Prune(dir, ReportLimits.KeepLocalCopies);
                    List<ReportOutboxEntry> back = KmhReportOutbox.ReadFolder(dir, T0 + 10);
                    int reports = Directory.GetFiles(dir, "*.json").Count(p => !p.EndsWith(KmhReportOutbox.StateSuffix));
                    bool kept = waiting.All(id => back.Any(b => b.ReportId == id));
                    bool resumed = back.All(b => b.Developers.IsPending) && back.Any(b => b.Developers.Reason == ReportReasons.NoAnswer);
                    bool ok = kept && resumed && reports == ReportLimits.KeepLocalCopies + 2 && back.Count == 2;
                    return (ok, $"{reports} copies kept (limit {ReportLimits.KeepLocalCopies} plus 2 waiting); reloaded {back.Count}, "
                              + $"the one cut off mid-send waits again: {resumed}");
                }
                finally { try { Directory.Delete(dir, true); } catch { } }
            });

            Check("Report: the user's folder and stored credentials are scrubbed from every string", () =>
            {
                const string token = "kmh_pt_ABCDEFGHIJKLMNOPQRSTUV";
                var redact = new KmhReportDiagnostics.Redactor(@"D:\Profiles\Alice", new[] { token, "short" });
                var root = new JObject
                {
                    ["summary"] = new JObject { ["path"] = @"D:\Profiles\Alice\AppData\LocalLow\Ludeon" },
                    ["kmh_log"] = new JArray("bearer " + token + " at d:/profiles/alice/x", @"json D:\\Profiles\\Alice\\y", "short stays"),
                    ["nested"] = new JArray(new JObject { ["deep"] = new JArray(@"d:\PROFILES\ALICE\z") }),
                };
                KmhReportDiagnostics.Scrub(root, redact);
                string all = root.ToString(Formatting.None);
                bool ok = all.IndexOf("alice", StringComparison.OrdinalIgnoreCase) < 0 && !all.Contains(token)
                          && all.Contains("[redacted]") && all.Contains("short stays");
                return (ok, all.Length > 220 ? all.Substring(0, 220) + "…" : all);
            });

            Check("Report: diagnostics are cut to what the server keeps, newest log lines kept", () =>
            {
                var lines = new JArray();
                for (int i = 0; i < 5000; i++) lines.Add($"2026-10-06 12:00:00.000 [INFO] line {i:D4} " + new string('x', 80));
                var root = new JObject
                {
                    ["summary"] = new JObject { ["kmh_version"] = "1.3.1" },
                    ["mods"] = new JArray(new JObject { ["name"] = "Harmony" }),
                    ["kmh_log"] = lines,
                };
                string json = KmhReportDiagnostics.Fit(root, ReportLimits.MaxDiagnosticsChars);
                JObject back = JObject.Parse(json);
                var kept = (JArray)back["kmh_log"];
                bool ok = json.Length <= ReportLimits.MaxDiagnosticsChars && kept.Count > 0 && kept.Count < 5000
                          && ((string)kept[kept.Count - 1]).Contains("line 4999")
                          && ((string)back["summary"]["trimmed_to_fit"]).Contains("kmh_log 5000 ->")
                          && back["mods"] is JArray;
                return (ok, $"{json.Length:N0} characters; log 5000 -> {kept.Count}, newest kept");
            });

            Check("Report: a section too big on its own is dropped whole, and the summary stays", () =>
            {
                var root = new JObject
                {
                    ["summary"] = new JObject { ["kmh_version"] = "1.3.1" },
                    ["system"] = new JObject { ["blob"] = new string('z', 300_000) },
                };
                string json = KmhReportDiagnostics.Fit(root, ReportLimits.MaxDiagnosticsChars);
                JObject back = JObject.Parse(json);
                bool ok = json.Length <= ReportLimits.MaxDiagnosticsChars && back["system"] == null && back["summary"] != null
                          && ((string)back["summary"]["trimmed_to_fit"]).Contains("system removed");
                return (ok, json.Length > 160 ? json.Substring(0, 160) : json);
            });

            Check("Report: the log tail is read from a file that is still being written", () =>
            {
                string dir = Path.Combine(Path.GetTempPath(), "kmh-report-selftest-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                Directory.CreateDirectory(dir);
                try
                {
                    string path = Path.Combine(dir, "kmh-patch.log");
                    File.WriteAllLines(path, Enumerable.Range(0, 1000).Select(i => "line " + i));
                    List<string> tail;
                    using (new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite))   // the writer holds it
                        tail = KmhReportDiagnostics.TailLines(path, 300, 512 * 1024);
                    List<string> near = KmhReportDiagnostics.TailLines(path, 300, 2048);
                    bool ok = tail.Count == 300 && tail[0] == "line 700" && tail[299] == "line 999"
                              && near.Count > 0 && near.Count < 300 && near[near.Count - 1] == "line 999" && near[0].StartsWith("line ");
                    return (ok, $"300 of 1000 lines, from {tail[0]} to {tail[tail.Count - 1]}; a 2 KB window starts on a whole line: {near[0]}");
                }
                finally { try { Directory.Delete(dir, true); } catch { } }
            });

            Check("Report: a game error keeps its first stack lines, says how often it repeated and when it was seen", () =>
            {
                string stack = string.Join("\n", Enumerable.Range(0, 15).Select(i => $"at Frame{i}()"));
                string entry = KmhReportDiagnostics.LogEntry(true, 3, "seen 12:01:05", "Exception ticking Pawn", stack);
                string[] lines = entry.Split('\n');
                bool ok = lines[0] == "[error x3 seen 12:01:05] Exception ticking Pawn" && lines.Length == 1 + KmhReportDiagnostics.StackLines + 1
                          && lines[lines.Length - 1].Trim() == "...";
                return (ok, $"{lines.Length} lines: {lines[0]}");
            });

            Check("Report: copies kept on this computer stop at twenty, each picture with its report", () =>
            {
                string dir = Path.Combine(Path.GetTempPath(), "kmh-report-copies-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                try
                {
                    for (int i = 0; i < ReportLimits.KeepLocalCopies + 3; i++)
                    {
                        var s = new ReportSubmit { ReportId = Guid.NewGuid().ToString("N"), Kind = ReportKinds.Bug, Title = "t" + i, Screenshot = "AAAA" };
                        string saved = KmhReportClient.SaveCopy(dir, s, new byte[] { 0xFF, 0xD8, 0xFF, 0x00 });
                        if (string.IsNullOrEmpty(saved)) return (false, $"copy {i} was not saved");
                        if (File.ReadAllText(saved).Contains("\"screenshot\": \"AAAA\"")) return (false, "the picture was written into the JSON as well");
                    }
                    string[] reports = Directory.GetFiles(dir, "*.json");
                    string[] shots = Directory.GetFiles(dir, "*.jpg");
                    bool paired = shots.All(p => File.Exists(Path.ChangeExtension(p, ".json")));
                    return (reports.Length == ReportLimits.KeepLocalCopies && shots.Length == reports.Length && paired,
                            $"{reports.Length} reports, {shots.Length} pictures, every picture with its report: {paired}");
                }
                finally { try { Directory.Delete(dir, true); } catch { } }
            });

            Check("Report: a screenshot is scaled to the cap with its shape kept", () =>
            {
                var big = KmhScreenshot.ScaledSize(3840, 2160, KmhScreenshot.MaxWidth);
                var fits = KmhScreenshot.ScaledSize(1280, 720, KmhScreenshot.MaxWidth);
                var broken = KmhScreenshot.ScaledSize(0, 0, KmhScreenshot.MaxWidth);
                bool ok = big == (1600, 900) && fits == (1280, 720) && broken.Width >= 1 && broken.Height >= 1;
                return (ok, $"3840x2160 -> {big}, 1280x720 -> {fits}");
            });

            return r;
        }

        private static ReportOutboxEntry Entry(bool server, bool developers)
            => new ReportOutboxEntry
            {
                ReportId = Guid.NewGuid().ToString("N"), Title = "fixture", Kind = ReportKinds.Bug,
                ServerId = server ? "srv-test" : "", ServerName = server ? "Test Colony" : "", CreatedUnix = T0,
                Server = ReportDelivery.For(server, T0), Developers = ReportDelivery.For(developers, T0),
            };

        private static ReportRouteView View(string mode, int version, bool linked)
            => new ReportRouteView
            {
                Connected = true, Mode = mode, Version = version, ServerId = "srv-test", ServerName = "Test Colony",
                GlobalLinked = linked, ServerSlotFree = true, GlobalSlotFree = true,
            };

        private static ReportSubmit Submit(string id, string kind)
            => new ReportSubmit { ReportId = id, Kind = kind, Title = "fixture", Body = "body", ClientVersion = "1.3.1+rel-279",
                                  Destinations = ReportDestinations.Of(true, true), Area = ReportAreas.General };

        private static ReportResultWire Answer(string id, bool ok, string forwarding, string developers)
            => new ReportResultWire { ReportId = id, Ok = ok, Forwarding = forwarding, Developers = developers, Reference = KmhReportClient.Short(id) };

        private static KmhEnvelope Env(string kind, JObject data)
            => KmhEnvelope.TryParse(new KmhEnvelope(kind, data).Serialize());
    }
}
