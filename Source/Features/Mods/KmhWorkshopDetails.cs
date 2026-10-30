using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using KMHPatch.Diagnostics;
using Newtonsoft.Json.Linq;

namespace KMHPatch.Features.Mods
{
    // Steam's own record of a Workshop item. A server names its mods itself, so the name it shows can sit on any item.
    internal static class KmhWorkshopDetails
    {
        internal const long RimWorldAppId = 294100;
        internal const int MaxBodyBytes = 1 << 20;
        internal const int MaxBatch = 100;
        internal static readonly TimeSpan NewItemAge = TimeSpan.FromDays(14);
        private const string Endpoint = "https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/";

        internal sealed class Item
        {
            public string Id = "";
            public bool Found;
            public string Title = "";
            public long AppId;
            public int Visibility;
            public long CreatedUnix;
            public bool Banned;

            // Refused outright: Steam has no such item, it belongs to another game, or Steam has banned it.
            public bool Refused => !Found || AppId != RimWorldAppId || Banned;
            public bool Unlisted => Found && Visibility != 0;
            public bool IsNew(DateTime nowUtc) => Found && CreatedUnix > 0
                && nowUtc - DateTimeOffset.FromUnixTimeSeconds(CreatedUnix).UtcDateTime < NewItemAge;
        }

        private static readonly object _lock = new object();
        private static readonly Dictionary<string, Item> _items = new Dictionary<string, Item>(StringComparer.Ordinal);
        private static readonly HashSet<string> _inFlight = new HashSet<string>(StringComparer.Ordinal);
        private static DateTime _retryAfterUtc = DateTime.MinValue;
        private static readonly HttpClient Http = Global.KmhHttps.Client(TimeSpan.FromSeconds(20));

        public static bool LastFailed { get; private set; }

        public static Item Get(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            lock (_lock) return _items.TryGetValue(id, out Item i) ? i : null;
        }

        public static bool Pending(IEnumerable<string> ids)
        {
            lock (_lock)
                foreach (string id in ids) if (KmhWorkshop.IsWorkshopId(id) && !_items.ContainsKey(id) && !LastFailed) return true;
            return false;
        }

        // Starts one background request for the ids not yet known; safe to call every frame.
        public static void Ensure(IEnumerable<string> ids)
        {
            var want = new List<string>();
            lock (_lock)
            {
                if (DateTime.UtcNow < _retryAfterUtc) return;
                foreach (string id in ids)
                    if (KmhWorkshop.IsWorkshopId(id) && !_items.ContainsKey(id) && !_inFlight.Contains(id) && want.Count < MaxBatch)
                        want.Add(id);
                if (want.Count == 0) return;
                foreach (string id in want) _inFlight.Add(id);
            }
            Task.Run(() => FetchAsync(want));
        }

        private static async Task FetchAsync(List<string> ids)
        {
            List<Item> got;
            try
            {
                var form = new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>("itemcount", ids.Count.ToString()) };
                for (int i = 0; i < ids.Count; i++) form.Add(new KeyValuePair<string, string>($"publishedfileids[{i}]", ids[i]));
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20)))
                using (HttpResponseMessage res = await Http.PostAsync(Endpoint, new FormUrlEncodedContent(form), cts.Token).ConfigureAwait(false))
                {
                    if (!res.IsSuccessStatusCode) throw new HttpRequestException("Steam answered " + (int)res.StatusCode);
                    string body = await Rwt.RwtReleaseCatalog.ReadBoundedAsync(res, MaxBodyBytes, cts.Token).ConfigureAwait(false);
                    got = Parse(body, ids);
                }
                lock (_lock) { foreach (Item i in got) _items[i.Id] = i; LastFailed = false; }
            }
            catch (Exception ex)
            {
                KmhLog.Warn($"Workshop: Steam's own details for {ids.Count} item(s) could not be read: {ex.Message}");
                lock (_lock) { LastFailed = true; _retryAfterUtc = DateTime.UtcNow + TimeSpan.FromSeconds(60); }
            }
            finally { lock (_lock) foreach (string id in ids) _inFlight.Remove(id); }
        }

        // Every asked-for id gets an answer: one Steam does not return is an item Steam does not have.
        internal static List<Item> Parse(string body, IList<string> asked)
        {
            var byId = new Dictionary<string, Item>(StringComparer.Ordinal);
            JArray details = JObject.Parse(body)?["response"]?["publishedfiledetails"] as JArray;
            foreach (JToken t in details ?? new JArray())
            {
                string id = (string)t["publishedfileid"] ?? "";
                if (!KmhWorkshop.IsWorkshopId(id)) continue;
                byId[id] = new Item
                {
                    Id = id,
                    Found = (int?)t["result"] == 1,
                    Title = ((string)t["title"] ?? "").Trim(),
                    AppId = (long?)t["consumer_app_id"] ?? 0,
                    Visibility = (int?)t["visibility"] ?? 0,
                    CreatedUnix = (long?)t["time_created"] ?? 0,
                    Banned = ((int?)t["banned"] ?? 0) != 0,
                };
            }
            var list = new List<Item>();
            foreach (string id in asked) list.Add(byId.TryGetValue(id, out Item i) ? i : new Item { Id = id, Found = false });
            return list;
        }

        // A server's name for an item that Steam titles differently is shown, so the player sees the difference.
        internal static bool NameDiffers(string serverName, string steamTitle)
        {
            string a = Norm(serverName), b = Norm(steamTitle);
            return a.Length > 0 && b.Length > 0 && b.IndexOf(a, StringComparison.Ordinal) < 0 && a.IndexOf(b, StringComparison.Ordinal) < 0;
        }

        private static string Norm(string s)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char c in s ?? "") if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
            return sb.ToString();
        }

        internal static void ResetForTest() { lock (_lock) { _items.Clear(); _inFlight.Clear(); _retryAfterUtc = DateTime.MinValue; LastFailed = false; } }

        internal static void SeedForTest(IEnumerable<Item> items) { lock (_lock) foreach (Item i in items) _items[i.Id] = i; }
    }
}
