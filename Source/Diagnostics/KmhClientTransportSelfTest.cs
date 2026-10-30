using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using KMHPatch.SubProtocol;

namespace KMHPatch.Diagnostics
{
    internal static class KmhClientTransportSelfTest
    {
        private const string Kind = "kmh.selftest.client";

        public static List<(string, bool, string)> Run()
        {
            var r = new List<(string, bool, string)>();
            void Check(string name, Func<(bool ok, string detail)> probe)
            {
                try { (bool ok, string detail) = probe(); r.Add((name, ok, detail)); }
                catch (Exception ex) { r.Add((name, false, $"threw: {ex.GetType().Name}: {ex.Message}")); }
            }

            Check("Client transport: a send returns at once while the server is not reading", () =>
            {
                var (reader, writer) = Pair();
                int failures = 0;
                var q = new KmhSendQueue(writer.GetStream(), false, 64 * 1024, 64 * 1024, _ => Interlocked.Increment(ref failures));
                try
                {
                    byte[] body = Body(Noise(60_000));
                    var sw = Stopwatch.StartNew();
                    int queued = 0;
                    for (int i = 0; i < 200; i++) if (q.Enqueue(Kind, body)) queued++;
                    long ms = sw.ElapsedMilliseconds;
                    bool ok = queued == 200 && ms < 1000 && failures == 0;
                    return (ok, $"{queued} messages ({queued * body.Length / 1024} KB) handed over in {ms} ms with nobody reading");
                }
                finally { q.Close(); Close(reader, writer); }
            });

            Check("Client transport: messages arrive in the order sent, and a backlog shares writes", () =>
            {
                var (reader, writer) = Pair();
                var q = new KmhSendQueue(writer.GetStream(), false, 64 * 1024, 64 * 1024, null);
                try
                {
                    const int large = 12, small = 200;
                    long writesBefore = KmhClientTransportStats.Get("socket_writes");
                    for (int i = 0; i < large; i++) q.Enqueue(Kind, Body(Envelope(new { seq = i, pad = new string('y', 60_000) })));
                    for (int i = 0; i < small; i++) q.Enqueue(Kind, Body(Envelope(new { seq = large + i })));
                    NetworkStream s = reader.GetStream();
                    for (int i = 0; i < large + small; i++)
                    {
                        KmhEnvelope got = Read(s);
                        if (got == null) return (false, $"message {i} never arrived");
                        if (got.GetInt("seq", -1) != i) return (false, $"message {i} carried seq {got.GetInt("seq", -1)}");
                    }
                    q.WaitDrained(TimeSpan.FromSeconds(2));
                    long writes = KmhClientTransportStats.Get("socket_writes") - writesBefore;
                    return (writes < (large + small) / 2, $"{large + small} messages in order, in {writes} socket write(s)");
                }
                finally { q.Close(); Close(reader, writer); }
            });

            // A catalog packed past an older server's 64 KB unpack limit drops the link.
            Check("Client transport: packed into one frame only up to what the server unpacks", () =>
            {
                byte[] catalog = Body(Envelope(new { rows = Rows(4000) }));
                if (catalog.Length <= 64 * 1024) return (false, "the fixture is not past one frame");
                int toOlder = FramesFor(catalog, deflate: true, inflateBytes: 64 * 1024, out bool olderWhole);
                int toNewer = FramesFor(catalog, deflate: true, inflateBytes: 2 * 1024 * 1024, out bool newerWhole);
                bool ok = toOlder > 1 && olderWhole && toNewer == 1 && newerWhole;
                return (ok, $"{catalog.Length / 1024} KB to a server that unpacks 64 KB: {toOlder} frame(s), whole again: {olderWhole}; "
                          + $"to one that unpacks 2 MB: {toNewer} frame(s), whole: {newerWhole}");
            });

            Check("Client transport: an incompressible message past one frame goes as fragments the server can join", () =>
            {
                byte[] big = Body(Envelope(new { blob = Noise(300_000) }));
                int frames = FramesFor(big, deflate: true, inflateBytes: 2 * 1024 * 1024, out bool whole);
                return (frames > 1 && whole, $"{big.Length / 1024} KB as {frames} frame(s), joined back byte for byte: {whole}");
            });

            Check("Client transport: this client unpacks what it tells the server, and refuses a frame that expands past it", () =>
            {
                var (reader, writer) = Pair();
                try
                {
                    byte[] catalog = Body(Envelope(new { rows = Rows(4000) }));
                    byte[] bomb = new byte[KmhApiClient.MaxInflateBytes + 1];
                    NetworkStream w = writer.GetStream();
                    WriteRaw(w, KmhFrameCodec.Frame(KmhFrameCodec.Deflate(catalog), true));
                    WriteRaw(w, KmhFrameCodec.Frame(KmhFrameCodec.Deflate(bomb), true));
                    NetworkStream s = reader.GetStream();
                    KmhEnvelope first = KmhApiClient.ReadFrameForTest(s, CancellationToken.None).GetAwaiter().GetResult();
                    KmhEnvelope second = KmhApiClient.ReadFrameForTest(s, CancellationToken.None).GetAwaiter().GetResult();
                    bool ok = first != null && first.Kind == Kind && second == null;
                    return (ok, $"{catalog.Length / 1024} KB from one frame: {(first != null ? "read" : "REFUSED")}; "
                              + $"{bomb.Length / 1024} KB of zeroes: {(second == null ? "refused" : "INFLATED")}");
                }
                finally { Close(reader, writer); }
            });

            Check("Client transport: a server that never reads ends the link instead of growing the queue", () =>
            {
                var (reader, writer) = Pair();
                int failures = 0;
                var q = new KmhSendQueue(writer.GetStream(), false, 64 * 1024, 64 * 1024, _ => Interlocked.Increment(ref failures));
                try
                {
                    byte[] body = Body(Noise(60_000));
                    int accepted = 0;
                    bool refused = false;
                    for (int i = 0; i < 1000 && !refused; i++) { if (q.Enqueue(Kind, body)) accepted++; else refused = true; }
                    Thread.Sleep(50);
                    bool ok = refused && q.IsDead && Volatile.Read(ref failures) == 1 && !q.Enqueue(Kind, body);
                    return (ok, $"{accepted} accepted ({accepted * (long)body.Length / 1024} KB), then refused: {refused}; "
                              + $"link ended: {q.IsDead}, told once: {failures == 1}");
                }
                finally { q.Close(); Close(reader, writer); }
            });

            Check("Client transport: the hello settles the server's frame and unpack limits", () =>
            {
                var older = KmhApiClient.LimitsForTest(0, 0);
                var smaller = KmhApiClient.LimitsForTest(32, 0);
                var newer = KmhApiClient.LimitsForTest(64, 2048);
                var greedy = KmhApiClient.LimitsForTest(1024, 999_999);
                bool ok = older == (64 * 1024, 64 * 1024) && smaller == (32 * 1024, 32 * 1024)
                       && newer == (64 * 1024, 2048 * 1024) && greedy == (64 * 1024, (int)KmhFragments.MaxTransferBytes);
                return (ok, $"silent {older}, 32 KB frame {smaller}, 2 MB unpack {newer}, past every cap {greedy}");
            });

            Check("Client transport: a wait that finishes is reported, and one that does not times out", () =>
            {
                var done = new TaskCompletionSource<bool>();
                Task<bool> waiting = KmhApiClient.FinishesWithin(done.Task, 60_000, CancellationToken.None);
                done.SetResult(true);
                bool finished = waiting.Wait(5000) && waiting.Result;
                bool timedOut = !KmhApiClient.FinishesWithin(new TaskCompletionSource<bool>().Task, 30, CancellationToken.None)
                                             .GetAwaiter().GetResult();
                return (finished && timedOut, $"finished: {finished}; never finishing timed out: {timedOut}");
            });

            Check("Client transport: a large transfer is joined on the link's own thread", () =>
            {
                string wire = Envelope(new { rows = Rows(3000) });
                List<KmhEnvelope> parts = KmhFragments.Split(Kind, wire, 64 * 1024);
                if (parts == null || parts.Count < 2) return (false, "the fixture did not split");
                var assembler = new KmhFragments.Assembler();
                var handed = new List<KmhEnvelope>();
                Task.Run(() =>
                {
                    foreach (KmhEnvelope p in parts)
                    {
                        KmhEnvelope next = KmhApiClient.ForGameThread(assembler, KmhEnvelope.TryParse(p.Serialize()));
                        if (next != null) handed.Add(next);
                    }
                    KmhEnvelope pong = KmhApiClient.ForGameThread(assembler, new KmhEnvelope("kmh.pong", null));
                    if (pong != null) handed.Add(pong);
                }).GetAwaiter().GetResult();
                bool ok = handed.Count == 1 && handed[0].Kind == Kind && handed[0].Serialize() == KmhEnvelope.TryParse(wire).Serialize()
                          && assembler.OpenTransfers == 0;
                return (ok, $"{parts.Count} fragments and a heartbeat in; the game's thread is handed {handed.Count} message(s): "
                          + string.Join(", ", handed.ConvertAll(e => e.Kind).ToArray()));
            });

            Check("Main thread: a burst is spread over frames, in order", () =>
            {
                KmhMainThread.PumpForTest();
                var order = new List<int>();
                for (int i = 0; i < 40; i++) { int n = i; KmhMainThread.EnqueueForTest(() => { order.Add(n); Thread.Sleep(1); }); }
                long budget = Ticks(KmhMainThread.FrameBudgetMs);
                int first = KmhMainThread.Drain(budget);
                int frames = 1;
                while (KmhMainThread.Backlog > 0 && frames < 100) { KmhMainThread.Drain(budget); frames++; }
                bool inOrder = order.Count == 40;
                for (int i = 0; inOrder && i < 40; i++) inOrder = order[i] == i;
                return (first > 0 && first < 40 && inOrder, $"first frame ran {first} of 40 (about 1 ms each); all done over {frames} frames, in order: {inOrder}");
            });

            Check("Main thread: behind, a frame gets the catch-up budget and no more", () =>
            {
                KmhMainThread.PumpForTest();
                bool slow = true;
                int n = KmhMainThread.CatchUpAbove + 100;
                for (int i = 0; i < n; i++) KmhMainThread.EnqueueForTest(() => { if (slow) Busy(1.0); });
                var sw = Stopwatch.StartNew();
                int ran = KmhMainThread.Drain(Ticks(KmhMainThread.FrameBudgetMs), Ticks(KmhMainThread.CatchUpBudgetMs), KmhMainThread.MaxActionsPerFrame);
                double ms = sw.Elapsed.TotalMilliseconds;
                slow = false;
                KmhMainThread.PumpForTest();
                bool ok = ms >= KmhMainThread.CatchUpBudgetMs * 0.9 && ms < KmhMainThread.CatchUpBudgetMs + 5;
                return (ok, $"{n} waiting (1 ms each); one frame ran {ran} in {ms:0.0} ms - budget {KmhMainThread.FrameBudgetMs} ms, catch-up cap {KmhMainThread.CatchUpBudgetMs} ms");
            });

            Check("Main thread: no frame runs more than the per-frame cap of actions", () =>
            {
                KmhMainThread.PumpForTest();
                int n = KmhMainThread.MaxActionsPerFrame + 500;
                long cappedBefore = Stat("main_thread_frames_capped");
                for (int i = 0; i < n; i++) KmhMainThread.EnqueueForTest(() => { });
                long minute = Ticks(60_000);
                int ran = KmhMainThread.Drain(minute, minute, KmhMainThread.MaxActionsPerFrame);
                int left = KmhMainThread.Backlog;
                KmhMainThread.PumpForTest();
                bool ok = ran == KmhMainThread.MaxActionsPerFrame && left == 500 && Stat("main_thread_frames_capped") == cappedBefore + 1;
                return (ok, $"{n} cheap actions; one frame ran {ran}, left {left}");
            });

            Check("Main thread: only the newest queued snapshot of a kind is applied", () =>
            {
                KmhMainThread.PumpForTest();
                var seen = new List<string>();
                long before = Stat("main_thread_coalesced");
                for (int i = 1; i <= 5; i++) { int n = i; KmhMainThread.EnqueueLatestForTest("probe.a", -1, () => seen.Add("a" + n)); }
                KmhMainThread.EnqueueLatestForTest("probe.b", -1, () => seen.Add("b1"));
                KmhMainThread.EnqueueForTest(() => seen.Add("x1"));
                KmhMainThread.EnqueueForTest(() => seen.Add("x2"));
                KmhMainThread.PumpForTest();
                KmhMainThread.EnqueueLatestForTest("probe.a", -1, () => seen.Add("a6"));
                KmhMainThread.PumpForTest();
                string got = string.Join(",", seen.ToArray());
                long skipped = Stat("main_thread_coalesced") - before;
                bool ok = got == "a5,b1,x1,x2,a6" && skipped == 4;
                return (ok, $"applied {got}; {skipped} outdated skipped");
            });

            Check("Main thread: a late snapshot with a lower revision never displaces a newer one", () =>
            {
                KmhMainThread.PumpForTest();
                var seen = new List<string>();
                KmhMainThread.EnqueueLatestForTest("probe.r", 20, () => seen.Add("r20"));
                KmhMainThread.EnqueueLatestForTest("probe.r", 19, () => seen.Add("r19"));
                KmhMainThread.PumpForTest();
                KmhMainThread.EnqueueLatestForTest("probe.r", 21, () => seen.Add("r21"));
                KmhMainThread.EnqueueLatestForTest("probe.r", 19, () => seen.Add("r19b"));
                KmhMainThread.EnqueueLatestForTest("probe.r", 22, () => seen.Add("r22"));
                KmhMainThread.PumpForTest();
                string got = string.Join(",", seen.ToArray());
                // r19 still runs after r20 and the cache's revision guard refuses it; r21 and r19b are outdated by r22.
                bool ok = got == "r20,r19,r22";
                return (ok, $"applied {got}");
            });

            Check("Main thread: which server messages may be coalesced, and under what key", () =>
            {
                KmhEnvelope Env(string kind, string json) => KmhEnvelope.TryParse("{\"kind\":\"" + kind + "\",\"v\":1,\"data\":" + json + "}");
                string personal = KmhApiClient.LatestKey(7, Env(KmhProtocol.Kind.TreasurySnapshot, "{\"owner_key\":\"\",\"revision\":3}"));
                string guild = KmhApiClient.LatestKey(7, Env(KmhProtocol.Kind.TreasurySnapshot, "{\"owner_key\":\"Guild A\",\"revision\":3}"));
                string market7 = KmhApiClient.LatestKey(7, Env(KmhProtocol.Kind.MarketplaceSnapshot, "{}"));
                string market8 = KmhApiClient.LatestKey(8, Env(KmhProtocol.Kind.MarketplaceSnapshot, "{}"));
                string enforcement = KmhApiClient.LatestKey(7, Env(KmhProtocol.Kind.EnforcementSnapshot, "{}"));
                string chat = KmhApiClient.LatestKey(7, Env(KmhProtocol.Kind.ChatSnapshot, "{\"channel\":\"global\"}"));
                string invitables = KmhApiClient.LatestKey(7, Env(KmhProtocol.Kind.GuildInvitablesSnapshot, "{}"));
                bool ok = personal != null && guild != null && personal != guild && market7 != null && market7 != market8
                          && enforcement == null && chat == null && invitables == null;
                return (ok, $"vaults apart: {personal != guild}; links apart: {market7 != market8}; enforcement {enforcement ?? "never"}, chat {chat ?? "never"}, invitables {invitables ?? "never"}");
            });

            Check("Main thread: a failing action is counted and named, and the queue carries on", () =>
            {
                KmhMainThread.PumpForTest();
                long before = KmhMainThread.Failures;
                Action<string> real = KmhMainThread.Reporter;
                KmhMainThread.Reporter = _ => { };
                bool after = false;
                try
                {
                    KmhMainThread.EnqueueForTest(ThrowForTest);
                    KmhMainThread.EnqueueForTest(() => after = true);
                    KmhMainThread.PumpForTest();
                }
                finally { KmhMainThread.Reporter = real; }
                string last = KmhMainThread.LastFailure ?? "";
                bool ok = KmhMainThread.Failures == before + 1 && after && last.Contains("ThrowForTest") && last.Contains("probe failure")
                          && Stat("main_thread_failures") == KmhMainThread.Failures;
                return (ok, $"failures +{KmhMainThread.Failures - before}, next ran: {after}, last: {last}");
            });

            Check("Main thread: a logger that fails while reporting a failure cannot loop", () =>
            {
                KmhMainThread.PumpForTest();
                KmhMainThread.ResetFailuresForTest();
                int reports = 0;
                Action<string> real = KmhMainThread.Reporter;
                KmhMainThread.Reporter = _ => { reports++; KmhMainThread.EnqueueForTest(ThrowForTest); throw new InvalidOperationException("logger broke"); };
                int left;
                try
                {
                    KmhMainThread.EnqueueForTest(ThrowForTest);
                    long minute = Ticks(60_000);
                    for (int frame = 0; frame < 300 && KmhMainThread.Backlog > 0; frame++) KmhMainThread.Drain(minute, minute, 1);
                    left = KmhMainThread.Backlog;
                }
                finally { KmhMainThread.Reporter = real; }
                long minute2 = Ticks(60_000);
                for (int frame = 0; frame < 1000 && KmhMainThread.Backlog > 0; frame++) KmhMainThread.Drain(minute2, minute2, 1);
                int stuck = KmhMainThread.Backlog;
                KmhMainThread.ClearForTest();
                bool ok = left == 0 && stuck == 0 && reports == 5;
                return (ok, $"{reports} report(s), {left} still queued after the chain, {stuck} after the real logger took over");
            });

            Check("Main thread: a report that fails on the game thread is counted, not reported again", () =>
            {
                KmhMainThread.PumpForTest();
                KmhMainThread.ResetFailuresForTest();
                int reports = 0;
                Action<string> real = KmhMainThread.Reporter;
                // What KmhLog.Warn does on the game thread: runs the log call inline, where it can throw too.
                KmhMainThread.Reporter = _ => { reports++; KmhMainThread.RunInlineForTest(ThrowForTest); };
                try { KmhMainThread.RunInlineForTest(ThrowForTest); }
                finally { KmhMainThread.Reporter = real; }
                bool ok = reports == 1 && KmhMainThread.Failures == 2;
                return (ok, $"{reports} report(s) for {KmhMainThread.Failures} failure(s)");
            });

            Check("Main thread: falling hopelessly behind starts a new session once, instead of freezing", () =>
            {
                KmhMainThread.PumpForTest();
                KmhMainThread.ResetOverloadForTest();
                int resets = 0;
                Action realReset = KmhMainThread.OnOverload;
                Action<string> realReport = KmhMainThread.Reporter;
                KmhMainThread.OnOverload = () => resets++;
                KmhMainThread.Reporter = _ => { };
                bool quiet, tripped, cooled;
                long overloads = Stat("main_thread_overloads");
                try
                {
                    quiet = !KmhMainThread.CheckOverload();
                    for (int i = 0; i < KmhMainThread.OverloadBacklog; i++) KmhMainThread.EnqueueForTest(() => { });
                    tripped = KmhMainThread.CheckOverload();
                    cooled = !KmhMainThread.CheckOverload();
                    KmhMainThread.PumpForTest();
                }
                finally
                {
                    KmhMainThread.OnOverload = realReset;
                    KmhMainThread.Reporter = realReport;
                    KmhMainThread.ResetOverloadForTest();
                }
                bool ok = quiet && tripped && cooled && resets == 1 && Stat("main_thread_overloads") == overloads + 1;
                return (ok, $"idle tripped: {!quiet}; {KmhMainThread.OverloadBacklog} waiting tripped: {tripped}; again within the cooldown: {!cooled}; resets {resets}");
            });

            // A wait counts only from when the game thread came back, or every boot reads as "behind - reconnecting".
            Check("Main thread: a load that held the game thread is not a queue losing ground", () =>
            {
                long f = Stopwatch.Frequency;
                long enqueued = 1000 * f, resumed = enqueued + 27 * f, now = resumed + f / 2;
                double afterLoad = KmhMainThread.EffectiveAgeMs(enqueued, now, resumed);
                double steady = KmhMainThread.EffectiveAgeMs(enqueued, enqueued + 25 * f, enqueued - f);
                double unknown = KmhMainThread.EffectiveAgeMs(enqueued, enqueued + 25 * f, 0);
                bool ok = afterLoad < KmhMainThread.OverloadAgeSeconds * 1000.0
                       && steady >= KmhMainThread.OverloadAgeSeconds * 1000.0 && unknown >= KmhMainThread.OverloadAgeSeconds * 1000.0;
                return (ok, $"after a 27 s load {afterLoad:0} ms; while pumping {steady:0} ms; no resume known {unknown:0} ms");
            });

            Check("Performance: percentiles and the 1% low are read from the frames measured", () =>
            {
                KmhPerfSampler.ResetForTest();
                for (int i = 0; i < 99; i++) KmhPerfSampler.Frame(0.010f);
                KmhPerfSampler.Frame(0.100f);
                KmhPerfSampler.Snapshot s = KmhPerfSampler.Build();
                KmhPerfSampler.ResetForTest();
                bool ok = Math.Abs(s.FrameMsP50 - 10f) < 0.01f && Math.Abs(s.FrameMsWorst - 100f) < 0.01f
                       && Math.Abs(s.FrameMsP99 - 10f) < 0.01f && s.FpsAvg > 90f && s.FpsAvg < 92f && s.FramesSampled == 100
                       && Math.Abs(s.FpsLow1 - 10f) < 0.01f;
                return (ok, $"typical {s.FrameMsP50} ms, 99% {s.FrameMsP99} ms, worst {s.FrameMsWorst} ms, "
                          + $"average {s.FpsAvg:0.0} fps, slowest 1% {s.FpsLow1:0.0} fps");
            });

            Check("Performance: KMH's per-frame steps are averaged and the busiest named first", () =>
            {
                KmhPerfSampler.ResetForTest();
                string[] names = { "quiet", "busy" };
                long ms = Stopwatch.Frequency / 1000;
                for (int i = 0; i < 10; i++) KmhPerfSampler.Steps(names, new[] { ms / 10, 2 * ms });
                KmhPerfSampler.Snapshot s = KmhPerfSampler.Build();
                KmhPerfSampler.ResetForTest();
                bool ok = s.Steps.Count == 2 && s.Steps[0].Step == "busy" && Math.Abs(s.Steps[0].AvgMs - 2.0) < 0.05
                       && Math.Abs(s.KmhMsPerFrame - 2.1) < 0.05;
                return (ok, $"{s.Steps.Count} steps, busiest {(s.Steps.Count > 0 ? s.Steps[0].Step : "-")}, total {s.KmhMsPerFrame:0.00} ms");
            });

            Check("Performance: the readout says paused or no game rather than a number that means nothing", () =>
            {
                KmhPerfSampler.ResetForTest();
                for (int i = 0; i < 30; i++) KmhPerfSampler.Frame(0.016f);
                var none = UI.KmhPerfOverlay.PanelLines(KmhPerfSampler.Build(), new List<KeyValuePair<string, long>>(), 0);
                string line = UI.KmhPerfOverlay.Line(KmhPerfSampler.Build(), 0, 0);
                KmhPerfSampler.ResetForTest();
                bool ok = none.Exists(l => l.Contains("no game")) && !line.Contains("TPS") && line.Contains("fps");
                return (ok, line);
            });

            return r;
        }

        private static void ThrowForTest() => throw new InvalidOperationException("probe failure");

        private static long Ticks(double ms) => (long)(ms * Stopwatch.Frequency / 1000.0);

        private static void Busy(double ms)
        {
            long until = Stopwatch.GetTimestamp() + Ticks(ms);
            while (Stopwatch.GetTimestamp() < until) { }
        }

        private static long Stat(string name)
        {
            foreach (KeyValuePair<string, long> kv in KmhClientTransportStats.Snapshot()) if (kv.Key == name) return kv.Value;
            return -1;
        }

        private static int FramesFor(byte[] body, bool deflate, int inflateBytes, out bool whole)
        {
            whole = false;
            var (reader, writer) = Pair();
            var q = new KmhSendQueue(writer.GetStream(), deflate, 64 * 1024, inflateBytes, null);
            try
            {
                q.Enqueue(Kind, body);
                NetworkStream s = reader.GetStream();
                var assembler = new KmhFragments.Assembler();
                int frames = 0;
                while (frames < 600)
                {
                    KmhEnvelope got = KmhApiClient.ReadFrameForTest(s, CancellationToken.None).GetAwaiter().GetResult();
                    if (got == null) return frames;
                    frames++;
                    if (KmhFragments.IsFragment(got.Kind)) got = assembler.Accept(got);
                    if (got == null) continue;
                    whole = got.Kind == Kind && got.Serialize() == KmhEnvelope.TryParse(Encoding.UTF8.GetString(body)).Serialize();
                    return frames;
                }
                return frames;
            }
            finally { q.Close(); Close(reader, writer); }
        }

        private static string Envelope(object data) => new KmhEnvelope(Kind, data).Serialize();
        private static byte[] Body(string wire) => Encoding.UTF8.GetBytes(wire);
        private static byte[] Body(KmhEnvelope env) => Encoding.UTF8.GetBytes(env.Serialize());

        // Base64 of random bytes: valid JSON text that deflate barely shrinks.
        private static KmhEnvelope Noise(int chars)
        {
            byte[] random = new byte[chars * 3 / 4];
            new Random(42).NextBytes(random);
            return new KmhEnvelope(Kind, new { noise = Convert.ToBase64String(random) });
        }

        private static List<object> Rows(int n)
        {
            var rows = new List<object>(n);
            for (int i = 0; i < n; i++)
                rows.Add(new { id = i, defName = "Steel", stuff = (string)null, qty = i % 97, seller = "player_" + (i % 13), price = "12.50" });
            return rows;
        }

        private static KmhEnvelope Read(NetworkStream s)
        {
            Task<KmhEnvelope> read = KmhApiClient.ReadFrameForTest(s, CancellationToken.None);
            return read.Wait(5000) ? read.Result : null;
        }

        private static void WriteRaw(NetworkStream s, byte[] frame) => s.Write(frame, 0, frame.Length);

        private static (TcpClient Reader, TcpClient Writer) Pair()
        {
            TcpListener l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            TcpClient reader = new TcpClient { ReceiveBufferSize = 64 * 1024 };
            reader.Connect(IPAddress.Loopback, ((IPEndPoint)l.LocalEndpoint).Port);
            TcpClient writer = l.AcceptTcpClient();
            l.Stop();
            writer.SendBufferSize = 64 * 1024;
            writer.NoDelay = true;
            return (reader, writer);
        }

        private static void Close(params TcpClient[] clients)
        {
            foreach (TcpClient c in clients) { try { c.Close(); } catch { } }
        }
    }
}
