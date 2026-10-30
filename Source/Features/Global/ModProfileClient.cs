using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using KMHPatch.Diagnostics;

namespace KMHPatch.Features.Global
{
    internal static class ModProfileClient
    {
        // Written by the fetch thread and read by the UI: each field is one reference or flag, published last-first.
        internal sealed class Entry
        {
            public volatile bool Pending;
            public long Revision = -1;
            public volatile ModProfileDto Profile;
            public volatile ModComparison Comparison;
            public volatile string ErrorCode = "";
            public int Failures;
            public DateTime RetryAtUtc;
        }

        // Backed off so a server whose profile route is down is not polled every frame.
        internal static TimeSpan RetryDelay(int failures)
            => failures <= 1 ? TimeSpan.FromSeconds(10) : failures == 2 ? TimeSpan.FromSeconds(30)
             : TimeSpan.FromSeconds(Math.Min(300, 60 * Math.Pow(2, Math.Min(failures - 3, 3))));

        private static readonly object _lock = new object();
        private static readonly Dictionary<string, Entry> _byServer = new Dictionary<string, Entry>(StringComparer.Ordinal);

        public static Entry Peek(string serverId)
        {
            if (string.IsNullOrEmpty(serverId)) return null;
            lock (_lock) return _byServer.TryGetValue(serverId, out Entry e) ? e : null;
        }

        // Capped by concurrent fetches, not a lifetime count, which left rows past the first dozen on "Checking" forever.
        internal const int MaxListFetches = 4;
        private static int _listFetches;

        public static void EnsureFetchedForList(ServerCardDto card)
        {
            if (card == null || string.IsNullOrEmpty(card.ServerId)) return;
            if (ModPolicyNames.EnforcesNothing(card.ModPolicy)) return;
            lock (_lock)
            {
                if (_byServer.TryGetValue(card.ServerId, out Entry known) && !Due(known, card)) return;
                if (_listFetches >= MaxListFetches) return;
            }
            EnsureFetched(card, fromList: true);
        }

        private static bool Due(Entry e, ServerCardDto card)
            => !e.Pending && (e.Revision != card.ManifestRevision || (e.Profile == null && DateTime.UtcNow >= e.RetryAtUtc));

        // Called from the details pane every frame, so everything past the first call is a dictionary lookup.
        public static void EnsureFetched(ServerCardDto card) => EnsureFetched(card, fromList: false);

        private static void EnsureFetched(ServerCardDto card, bool fromList)
        {
            if (card == null || string.IsNullOrEmpty(card.ServerId)) return;
            if (ModPolicyNames.EnforcesNothing(card.ModPolicy)) return;

            Entry entry;
            int failures = 0;
            lock (_lock)
            {
                if (_byServer.TryGetValue(card.ServerId, out entry))
                {
                    if (!Due(entry, card)) return;
                    if (entry.Revision == card.ManifestRevision) failures = entry.Failures;
                }
                entry = new Entry { Pending = true, Revision = card.ManifestRevision, Failures = failures };
                _byServer[card.ServerId] = entry;
                if (fromList) _listFetches++;
            }

            string id = card.ServerId;
            Task.Run(async () =>
            {
                try { await FetchAsync(id, entry, CancellationToken.None).ConfigureAwait(false); }
                catch (Exception ex)
                {
                    entry.ErrorCode = "KMH-NET-001";
                    KmhLog.Debug($"Global: mod profile fetch failed: {ex.Message}");
                }
                finally
                {
                    if (entry.Profile == null) { entry.Failures++; entry.RetryAtUtc = DateTime.UtcNow + RetryDelay(entry.Failures); }
                    entry.Pending = false;
                    if (fromList) lock (_lock) if (_listFetches > 0) _listFetches--;
                }
            });
        }

        private static async Task FetchAsync(string serverId, Entry entry, CancellationToken ct)
        {
            GlobalHttp.Result r = await GlobalHttp.GetAsync(
                "/api/v1/directory/servers/" + Uri.EscapeDataString(serverId) + "/mod-profile", ct).ConfigureAwait(false);

            if (!r.Ok) { entry.ErrorCode = r.ErrorCode ?? ""; return; }

            ModProfileDto profile = ModProfileDto.Parse(r.Body);
            if (profile == null) { entry.ErrorCode = "KMH-SEC-503"; return; }
            // Another server's list is never this one's answer, whatever routed it here.
            if (!string.IsNullOrEmpty(profile.ServerId) && !string.Equals(profile.ServerId, serverId, StringComparison.Ordinal))
            { entry.ErrorCode = "KMH-SEC-503"; return; }

            // Comparison first: a reader that sees the profile must also see its comparison.
            entry.Comparison = ModComparison.Compare(profile, KmhLocalMods.ActiveNames());
            entry.ErrorCode = "";
            entry.Profile = profile;
        }

        public static void Invalidate()
        {
            lock (_lock) _byServer.Clear();
        }

        internal static void SeedForTest(string serverId, Entry entry)
        {
            lock (_lock) _byServer[serverId] = entry;
        }

        internal static void ResetForTest()
        {
            lock (_lock) { _byServer.Clear(); _listFetches = 0; }
        }

        internal static int ListFetchesForTest { get { lock (_lock) return _listFetches; } }
    }
}
