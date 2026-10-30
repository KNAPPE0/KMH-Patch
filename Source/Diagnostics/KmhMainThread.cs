using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Verse;

namespace KMHPatch.Diagnostics
{
    // The transport's TCP threads cannot touch Verse.Log, Messages or Find.* directly.
    internal static class KmhMainThread
    {
        private sealed class Item
        {
            public Action Action;
            public string Key;       // latest-wins key; null runs every time
            public long Seq;
            public long Revision;    // -1 when the message carries none
            public long Enqueued;    // Stopwatch ticks
        }

        private static readonly ConcurrentQueue<Item> _queue = new ConcurrentQueue<Item>();
        private static readonly ConcurrentDictionary<string, Item> _latest = new ConcurrentDictionary<string, Item>(StringComparer.Ordinal);

        // A burst - a reconnect's snapshots, a busy chat - lands over a few frames instead of in one.
        internal const double FrameBudgetMs = 4.0;
        // Behind by this much, a frame may spend more, but never more than CatchUpBudgetMs: catching up must not freeze.
        internal const int CatchUpAbove = 2000;
        internal const double CatchUpBudgetMs = 12.0;
        internal const int MaxActionsPerFrame = 2000;
        // Past either line the queue is losing for good: reconnect, which makes every queued server message a no-op.
        internal const int OverloadBacklog = 20000;
        internal const double OverloadAgeSeconds = 20.0;
        private const double OverloadCooldownSeconds = 60.0;

        private static readonly long BudgetTicks = Ms(FrameBudgetMs);
        private static readonly long CatchUpTicks = Ms(CatchUpBudgetMs);
        private static long Ms(double ms) => (long)(ms * Stopwatch.Frequency / 1000.0);

        private static int _posted;
        private static long _seq;
        private static long _coalesced, _failures, _capped, _overloads, _agePeakTicks, _lastOverload;
        private static string _lastFailure;

        // The transport registers how to start a new session; the server re-sends every snapshot on join.
        internal static Action OnOverload;

        public static void Post(Action action) => Post(action, null, -1);

        // Latest-wins per key for snapshots that replace state; a lower revision never displaces a higher one.
        internal static void PostLatest(string key, long revision, Action action) => Post(action, key, revision);

        private static void Post(Action action, string key, long revision)
        {
            if (action == null) return;
            bool onMain;
            // UnityData throws before the game is up or after it is gone; a transport thread must not die over that.
            try { onMain = UnityData.IsInMainThread; }
            catch { Enqueue(action, key, revision); return; }
            if (onMain) Run(action);
            else Enqueue(action, key, revision);
        }

        private static void Enqueue(Action action, string key, long revision)
        {
            Item item = new Item { Action = action, Key = key, Revision = revision, Seq = Interlocked.Increment(ref _seq), Enqueued = Stopwatch.GetTimestamp() };
            if (key != null) _latest.AddOrUpdate(key, item, (_, prev) => item.Revision >= prev.Revision ? item : prev);
            _queue.Enqueue(item);
            SubProtocol.KmhClientTransportStats.Backlog(Interlocked.Increment(ref _posted));
        }

        internal static int Backlog => Math.Max(0, Volatile.Read(ref _posted));

        internal static double OldestAgeMs
            => _queue.TryPeek(out Item head) ? (Stopwatch.GetTimestamp() - head.Enqueued) * 1000.0 / Stopwatch.Frequency : 0.0;

        // A long load stalls the pump; waits count only from its resume, or every load would drop the KMH link.
        internal const double StallSeconds = 2.0;
        private static long _lastPump, _pumpingSince;

        public static void Pump()
        {
            long now = Stopwatch.GetTimestamp();
            if (_lastPump == 0 || now - _lastPump > Ms(StallSeconds * 1000.0)) _pumpingSince = now;
            _lastPump = now;
            CheckOverload();
            Drain(BudgetTicks, CatchUpTicks, MaxActionsPerFrame);
        }

        // Counted from no earlier than the pump's last resume; pumpingSince 0 means none is known.
        internal static double EffectiveAgeMs(long oldestEnqueued, long now, long pumpingSince)
        {
            long from = pumpingSince > oldestEnqueued ? pumpingSince : oldestEnqueued;
            return Math.Max(0L, now - from) * 1000.0 / Stopwatch.Frequency;
        }

        internal static int Drain(long budgetTicks) => Drain(budgetTicks, budgetTicks * 3, MaxActionsPerFrame);

        // One action always runs, so a slow handler still progresses; skipped items count toward the time, not the cap.
        internal static int Drain(long budgetTicks, long catchUpTicks, int maxActions)
        {
            if (_queue.IsEmpty) return 0;
            long start = Stopwatch.GetTimestamp();
            int ran = 0;
            while (_queue.TryDequeue(out Item item))
            {
                Interlocked.Decrement(ref _posted);
                long waited = start - item.Enqueued;
                if (waited > Interlocked.Read(ref _agePeakTicks)) Interlocked.Exchange(ref _agePeakTicks, waited);
                if (Skip(item)) { if (Over(start, budgetTicks, catchUpTicks)) break; continue; }
                Run(item.Action);
                ran++;
                if (ran >= maxActions) { Interlocked.Increment(ref _capped); break; }
                if (Over(start, budgetTicks, catchUpTicks)) break;
            }
            return ran;
        }

        private static bool Over(long start, long budgetTicks, long catchUpTicks)
        {
            if (budgetTicks <= 0) return false;
            long spent = Stopwatch.GetTimestamp() - start;
            return spent >= (Volatile.Read(ref _posted) >= CatchUpAbove ? catchUpTicks : budgetTicks);
        }

        private static bool Skip(Item item)
        {
            if (item.Key == null || !_latest.TryGetValue(item.Key, out Item latest)) return false;
            // Only an item queued after this one, with at least its revision, makes it outdated.
            if (!ReferenceEquals(latest, item) && latest.Seq > item.Seq) { Interlocked.Increment(ref _coalesced); return true; }
            if (ReferenceEquals(latest, item)) ((ICollection<KeyValuePair<string, Item>>)_latest).Remove(new KeyValuePair<string, Item>(item.Key, item));
            return false;
        }

        // Local work still runs after the reconnect; server messages check their session and fall through.
        internal static bool CheckOverload()
        {
            int backlog = Backlog;
            double age = _queue.TryPeek(out Item head) ? EffectiveAgeMs(head.Enqueued, Stopwatch.GetTimestamp(), _pumpingSince) / 1000.0 : 0.0;
            if (backlog < OverloadBacklog && age < OverloadAgeSeconds) return false;
            long now = Stopwatch.GetTimestamp();
            long last = Interlocked.Read(ref _lastOverload);
            if (last != 0 && now - last < Ms(OverloadCooldownSeconds * 1000.0)) return false;
            Interlocked.Exchange(ref _lastOverload, now);
            Interlocked.Increment(ref _overloads);
            Report($"KMH: the game thread fell {backlog} message(s) and {age:0.0} s behind - reconnecting to the server so the game does not freeze.");
            try { OnOverload?.Invoke(); } catch (Exception ex) { NoteFailure("overload handler", ex); }
            return true;
        }

        // Stands in for a call site's generation re-check, to prove the queue drops work from an ended session.
        internal static void PostForTest(int generation, Action action)
            => Enqueue(() => { if (generation == SubProtocol.KmhDispatcher.SessionGeneration) Run(action); }, null, -1);

        internal static void EnqueueForTest(Action action) => Enqueue(action, null, -1);
        internal static void EnqueueLatestForTest(string key, long revision, Action action) => Enqueue(action, key, revision);

        // Bounded, so a regression that makes actions re-queue themselves fails a check instead of hanging the suite.
        internal static void PumpForTest() => Drain(0, 0, 100_000);
        internal static void ClearForTest() { while (_queue.TryDequeue(out _)) Interlocked.Decrement(ref _posted); }

        internal static void ResetOverloadForTest() => Interlocked.Exchange(ref _lastOverload, 0);
        internal static void ResetFailuresForTest() => Interlocked.Exchange(ref _failures, 0);
        internal static void RunInlineForTest(Action action) => Run(action);

        private static void Run(Action action)
        {
            try { action(); }
            catch (Exception ex) { NoteFailure(Describe(action), ex); }
        }

        internal static long Failures => Interlocked.Read(ref _failures);
        internal static string LastFailure => Volatile.Read(ref _lastFailure);

        // Replaced in tests; the real one writes the KMH log, which itself posts here.
        internal static Action<string> Reporter = message => KmhLog.Warn(message);

        [ThreadStatic] private static bool _reporting;

        // Counted always; reported for the first few and then every 100th, and never from inside a report.
        private static void NoteFailure(string where, Exception ex)
        {
            long n = Interlocked.Increment(ref _failures);
            Volatile.Write(ref _lastFailure, $"{where}: {ex.GetType().Name}: {KmhLog.OneLine(ex.Message)}");
            if (n > 5 && n % 100 != 0) return;
            Report($"KMH: a game-thread action failed ({where}: {ex.GetType().Name}: {KmhLog.OneLine(ex.Message)}) - failure #{n}.");
        }

        private static void Report(string message)
        {
            if (_reporting) return;
            _reporting = true;
            try { Reporter?.Invoke(message); } catch { /* counted by the caller; a broken logger must not loop */ }
            finally { _reporting = false; }
        }

        private static string Describe(Action action)
        {
            try
            {
                System.Reflection.MethodInfo m = action.Method;
                string name = m.Name;
                // A lambda compiles to "<Outer>b__3_0"; the outer method is the useful part.
                if (name.StartsWith("<", StringComparison.Ordinal) && name.IndexOf('>') > 1) name = name.Substring(1, name.IndexOf('>') - 1) + " (lambda)";
                return (m.DeclaringType?.DeclaringType ?? m.DeclaringType)?.Name + "." + name;
            }
            catch { return "action"; }
        }

        internal static List<KeyValuePair<string, long>> Stats() => new List<KeyValuePair<string, long>>
        {
            new KeyValuePair<string, long>("main_thread_backlog", Backlog),
            new KeyValuePair<string, long>("main_thread_oldest_ms", (long)OldestAgeMs),
            new KeyValuePair<string, long>("main_thread_wait_peak_ms", Interlocked.Read(ref _agePeakTicks) * 1000 / Stopwatch.Frequency),
            new KeyValuePair<string, long>("main_thread_coalesced", Interlocked.Read(ref _coalesced)),
            new KeyValuePair<string, long>("main_thread_frames_capped", Interlocked.Read(ref _capped)),
            new KeyValuePair<string, long>("main_thread_overloads", Interlocked.Read(ref _overloads)),
            new KeyValuePair<string, long>("main_thread_failures", Interlocked.Read(ref _failures)),
        };
    }
}
