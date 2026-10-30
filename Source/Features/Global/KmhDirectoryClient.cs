using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KMHPatch.Diagnostics;

namespace KMHPatch.Features.Global
{
    // RateLimited and Degraded both mean the directory answered; folding them into Offline misreported outages.
    internal enum GlobalLinkState { Unknown, Connecting, Online, RateLimited, Degraded, Offline }

    // Never awaited from a draw call: the UI reads what the last completed refresh left, so it cannot stall.
    internal static class KmhDirectoryClient
    {
        public static GlobalLinkState State { get; private set; } = GlobalLinkState.Unknown;
        public static DateTime LastRefreshUtc => _gate.LastSuccessUtc;
        public static int ServerCount { get; private set; }
        public static int PlayerCount { get; private set; }
        public static string LastErrorCode { get; private set; } = "";

        // Guards only the reference swap: a published list is never mutated, so readers enumerate it unlocked.
        private static readonly object _lock = new object();
        private static List<ServerCardDto> _cards = new List<ServerCardDto>();
        private static string _nextCursor;

        // Bumped on a directory switch so an answer still in flight from the old one is dropped.
        private static int _generation;
        internal static int Generation => Volatile.Read(ref _generation);

        private static readonly object _saveLock = new object();
        private static DateTime _lastSaveUtc;
        internal static readonly TimeSpan SaveEvery = TimeSpan.FromSeconds(30);

        // Must stay ABOVE _gate: static initializers run in textual order, or the gate captures TimeSpan.Zero.
        public static readonly TimeSpan AutoRefreshEvery = TimeSpan.FromSeconds(20);

        private static readonly KmhPollGate _gate = new KmhPollGate(AutoRefreshEvery);

        public static bool Busy => _gate.Busy;

        public static bool ShowingCache { get; private set; }

        public static IReadOnlyList<ServerCardDto> Cards
        {
            get { lock (_lock) return _cards; }
        }

        public static string NextCursor
        {
            get { lock (_lock) return _nextCursor; }
        }

        // Kept apart from the ranked page so a refresh cannot drop a card ranked past it.
        private static Dictionary<string, ServerCardDto> _byId = new Dictionary<string, ServerCardDto>(StringComparer.Ordinal);
        // Per id set, so friends, favourites and preparation refresh on their own clocks without replacing each other.
        private static readonly Dictionary<string, DateTime> _idsAskedAt = new Dictionary<string, DateTime>(StringComparer.Ordinal);
        internal static readonly TimeSpan IdRefresh = TimeSpan.FromSeconds(30);
        private static bool _idsInFlight;

        public static ServerCardDto CardById(string serverId)
        {
            if (string.IsNullOrEmpty(serverId)) return null;
            lock (_lock)
            {
                foreach (ServerCardDto c in _cards) if (string.Equals(c.ServerId, serverId, StringComparison.Ordinal)) return c;
                return _byId.TryGetValue(serverId, out ServerCardDto byId) ? byId : null;
            }
        }

        // An older Global ignores ids and answers its first page, so only the ids asked for are kept.
        public static void EnsureCardsFor(IEnumerable<string> serverIds)
        {
            var want = new SortedSet<string>(StringComparer.Ordinal);
            foreach (string id in serverIds) if (!string.IsNullOrEmpty(id) && want.Count < 50) want.Add(id);
            if (want.Count == 0) return;
            string key = string.Join(",", want);
            DateTime now = DateTime.UtcNow;
            lock (_lock)
            {
                if (_idsInFlight) return;
                if (_idsAskedAt.TryGetValue(key, out DateTime at) && now - at < IdRefresh) return;
                _idsInFlight = true;
                _idsAskedAt[key] = now;
                if (_idsAskedAt.Count > 16) _idsAskedAt.Remove(_idsAskedAt.OrderBy(kv => kv.Value).First().Key);
            }
            int generation = Generation;
            Task.Run(async () =>
            {
                try
                {
                    GlobalHttp.Result r = await GlobalHttp.GetAsync("/api/v1/directory/servers?ids=" + Uri.EscapeDataString(key),
                                                                    CancellationToken.None).ConfigureAwait(false);
                    List<ServerCardDto> parsed = r.Ok ? DirectoryParse.Cards(r.Body, out _) : null;
                    if (parsed == null) { lock (_lock) _idsAskedAt[key] = DateTime.UtcNow - IdRefresh + TimeSpan.FromSeconds(10); return; }
                    var found = new Dictionary<string, ServerCardDto>(StringComparer.Ordinal);
                    foreach (ServerCardDto c in parsed) if (want.Contains(c.ServerId)) found[c.ServerId] = c;
                    lock (_lock)
                    {
                        if (generation != Generation) return;
                        var merged = new Dictionary<string, ServerCardDto>(_byId, StringComparer.Ordinal);
                        foreach (string id in want)
                            if (found.TryGetValue(id, out ServerCardDto c)) merged[id] = c; else merged.Remove(id);
                        _byId = merged;
                    }
                }
                catch (Exception ex) { KmhLog.Debug($"Global: server lookup by id failed: {ex.Message}"); }
                finally { lock (_lock) _idsInFlight = false; }
            });
        }

        public static void LoadCacheIfEmpty()
        {
            lock (_lock)
            {
                if (_cards.Count > 0) return;
                List<ServerCardDto> cached = KmhDirectoryCache.Load(out DateTime at);
                if (cached == null || cached.Count == 0) return;
                _cards = cached;
                ShowingCache = true;
                // A cache read from disk is data of a known age, not a refresh that never happened.
                _gate.LastSuccessUtc = at;
            }
        }

        public static bool AutoRefreshDue(DateTime nowUtc) => _gate.Due(nowUtc);

        public static void RefreshIfDue()
        {
            if (AutoRefreshDue(DateTime.UtcNow)) RefreshSummaryAsync();
        }

        public static void RefreshSummaryAsync()
        {
            if (!_gate.TryEnter()) return;
            State = State == GlobalLinkState.Online ? GlobalLinkState.Online : GlobalLinkState.Connecting;
            Task.Run(async () =>
            {
                try { await RefreshAsync(null, CancellationToken.None).ConfigureAwait(false); }
                catch (Exception ex) { KmhLog.Debug($"Global: directory refresh failed: {ex.Message}"); }
                finally { _gate.Exit(); }
            });
        }

        // Returns when the directory is as current as it will get, not when the refresh succeeded.
        public static async Task RefreshNowAsync(TimeSpan timeout)
        {
            if (!_gate.TryEnter()) return;
            try
            {
                using (var cts = new CancellationTokenSource(timeout))
                    await RefreshAsync(null, cts.Token).ConfigureAwait(false);
            }
            catch (Exception ex) { KmhLog.Debug($"Global: directory refresh failed: {ex.Message}"); }
            finally { _gate.Exit(); }
        }

        public static async Task RefreshAsync(string cursor, CancellationToken ct)
        {
            int generation = Generation;
            GlobalHttp.Result summary = await GlobalHttp.GetAsync("/api/v1/directory/counters", ct).ConfigureAwait(false);
            if (!summary.Ok) { MarkFailure(summary, generation); return; }

            GlobalHttp.Result page = await GlobalHttp.GetAsync(
                "/api/v1/directory/servers?limit=50" + (string.IsNullOrEmpty(cursor) ? "" : "&cursor=" + Uri.EscapeDataString(cursor)),
                ct).ConfigureAwait(false);
            if (!page.Ok) { MarkFailure(page, generation); return; }

            List<ServerCardDto> parsed = DirectoryParse.Cards(page.Body, out string next);
            if (parsed == null) { MarkFailure(page, generation, "KMH-SEC-503"); return; }

            List<ServerCardDto> published;
            lock (_lock)
            {
                if (generation != Generation) return;
                if (string.IsNullOrEmpty(cursor)) published = parsed;
                else
                {
                    published = new List<ServerCardDto>(_cards);
                    HashSet<string> have = new HashSet<string>(StringComparer.Ordinal);
                    foreach (ServerCardDto c in published) have.Add(c.ServerId);
                    foreach (ServerCardDto c in parsed) if (have.Add(c.ServerId)) published.Add(c);
                }
                _cards = published;
                _nextCursor = next;
                ServerCount = DirectoryParse.Int(summary.Body, "server_count");
                PlayerCount = DirectoryParse.Int(summary.Body, "player_count");
                MarkLive(cardsAreLive: true);
            }

            Persist(generation, force: true);
            try { if (generation == Generation) SeenServersStoreBridge.AdoptNames(published); }
            catch (Exception ex) { KmhLog.Debug($"Browser: refreshing local history failed: {ex.Message}"); }

            // Discord presence names the server from this list; without this it says "a private server" until the next push.
            Features.Discord.KmhRichPresence.Invalidate();

            // A first page replaces the list, so pages loaded with "Load more" are fetched again with it.
            if (string.IsNullOrEmpty(cursor))
                for (int p = 1; p < Volatile.Read(ref _pagesWanted) && !string.IsNullOrEmpty(next) && generation == Generation; p++)
                {
                    await RefreshAsync(next, ct).ConfigureAwait(false);
                    string after = NextCursor;
                    if (after == next) break;
                    next = after;
                }
        }

        internal const int MaxPages = 10;
        private static int _pagesWanted = 1;
        public static bool HasMore => !string.IsNullOrEmpty(NextCursor) && Volatile.Read(ref _pagesWanted) < MaxPages;

        public static void LoadMore()
        {
            string cursor = NextCursor;
            if (string.IsNullOrEmpty(cursor) || Volatile.Read(ref _pagesWanted) >= MaxPages) return;
            if (!_gate.TryEnter()) return;
            Interlocked.Increment(ref _pagesWanted);
            Task.Run(async () =>
            {
                try { await RefreshAsync(cursor, CancellationToken.None).ConfigureAwait(false); }
                catch (Exception ex) { KmhLog.Debug($"Global: loading more servers failed: {ex.Message}"); }
                finally { _gate.Exit(); }
            });
        }

        // Throttled for pushes (every heartbeat is one); writes the newest list so racing writers never leave an older one.
        private static void Persist(int generation, bool force)
        {
            lock (_saveLock)
            {
                if (generation != Generation) return;
                DateTime now = DateTime.UtcNow;
                if (!force && now - _lastSaveUtc < SaveEvery) return;
                _lastSaveUtc = now;
                KmhDirectoryCache.Save(Cards as List<ServerCardDto>);
            }
        }

        // Stamps the success clock so the poll stays quiet while the stream carries; racing paths polled every frame.
        internal static void ApplyPushedCard(ServerCardDto card, int generation)
        {
            if (card == null || string.IsNullOrEmpty(card.ServerId)) return;
            List<ServerCardDto> published;
            lock (_lock)
            {
                if (generation != Generation) return;
                published = new List<ServerCardDto>(_cards);
                int at = published.FindIndex(c => string.Equals(c.ServerId, card.ServerId, StringComparison.Ordinal));
                if (at >= 0) published[at] = card;
                else published.Add(card);
                _cards = published;

                MarkLive(cardsAreLive: true);
            }
            Persist(generation, force: false);
        }

        internal static void ApplyPushedCounters(int servers, int players, int generation)
        {
            lock (_lock)
            {
                if (generation != Generation) return;
                ServerCount = servers;
                PlayerCount = players;
                // Counts are not rows: the cards on screen may still be the ones loaded from disk.
                MarkLive(cardsAreLive: false);
            }
        }

        // Caller holds _lock.
        private static void MarkLive(bool cardsAreLive)
        {
            _gate.Succeeded();
            State = GlobalLinkState.Online;
            LastErrorCode = "";
            if (cardsAreLive) ShowingCache = false;
        }

        public static DateTime HoldingUntilUtc => _gate.HoldingUntilUtc;

        private static void MarkFailure(GlobalHttp.Result result, int generation, string code = null)
        {
            lock (_lock)
            {
                if (generation != Generation) return;
                LastErrorCode = code ?? result?.ErrorCode ?? "";

                if (result != null && result.RateLimited)
                {
                    // Not a failure: counting it would stack the client's own backoff on the delay the directory named.
                    _gate.HoldUntil(DateTime.UtcNow, result.RetryAfter);
                    State = GlobalLinkState.RateLimited;
                    return;
                }

                _gate.Failed();
                // Unreachable is the only thing that is actually offline; a 4xx means it answered and refused.
                State = result == null || result.Unreachable ? GlobalLinkState.Offline : GlobalLinkState.Degraded;
                // The cards already on screen stay, relabelled as cache; an empty list is worse than stale data.
                if (_cards.Count > 0) ShowingCache = true;
            }
        }

        public static void ForgetDirectory()
        {
            // Socket first, or it keeps pushing the old directory's rows into the new one's list.
            KmhDirectoryStream.Reset();
            // Under the save lock, so a write already under way lands before the delete rather than after it.
            lock (_saveLock)
            {
                Interlocked.Increment(ref _generation);
                _lastSaveUtc = default;
                KmhDirectoryCache.Clear();
            }
            ResetForTest();
            ModProfileClient.Invalidate();
            // Credentials are per directory; the old chat socket kept appending into the new directory's log.
            KmhIdentityTeardown.ForgetIdentity();
        }

        internal static void ResetForTest()
        {
            lock (_saveLock) _lastSaveUtc = default;
            lock (_lock)
            {
                _cards = new List<ServerCardDto>();
                _byId = new Dictionary<string, ServerCardDto>(StringComparer.Ordinal);
                _idsAskedAt.Clear();
                _idsInFlight = false;
                _pagesWanted = 1;
                _nextCursor = null;
                State = GlobalLinkState.Unknown;
                ShowingCache = false;
                ServerCount = PlayerCount = 0;
                LastErrorCode = "";
                _gate.Reset();
            }
        }

        internal static void SeedForTest(List<ServerCardDto> cards, GlobalLinkState state, bool cache)
        {
            lock (_lock) { _cards = cards ?? new List<ServerCardDto>(); State = state; ShowingCache = cache; }
        }
    }
}
