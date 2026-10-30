using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using KMHPatch.Diagnostics;
using Newtonsoft.Json;
using Verse;

namespace KMHPatch.Features.Servers
{
    // Keyed on the permanent id, and kept with the save data: player state must survive a cleared log folder.
    internal static class SeenServersStore
    {
        public class Entry
        {
            [JsonProperty("server_id")]  public string ServerId  { get; set; } = "";
            [JsonProperty("name")]       public string Name      { get; set; } = "";
            [JsonProperty("endpoint")]   public string Endpoint  { get; set; } = "";
            [JsonProperty("kmh")]        public string KmhVersion { get; set; } = "";
            [JsonProperty("rwt")]        public string RwtVersion { get; set; } = "";
            // Not underscore_cased: this file never leaves the machine, and the wire-drift heuristic watches that shape.
            [JsonProperty("lastSeen")]   public long   LastSeenTicks { get; set; }

            // Local and authoritative while offline; a Global account may later mirror it, never replace it.
            [JsonProperty("favorite")]   public bool   Favorite { get; set; }

            // False for an entry from an older build, or from a server too old to advertise its id.
            [JsonIgnore] public bool IdentityKnown => !string.IsNullOrEmpty(ServerId);

            // A server's own words, drawn beside others; id and endpoint are keys, so they are made inert only where drawn.
            internal void MakeInert()
            {
                Name       = UI.KmhDisplayText.InertLine(Name ?? "", SubProtocol.KmhDispatcher.MaxServerName);
                KmhVersion = UI.KmhDisplayText.Inert(KmhVersion ?? "", SubProtocol.KmhDispatcher.MaxServerBuild);
                RwtVersion = UI.KmhDisplayText.Inert(RwtVersion ?? "", SubProtocol.KmhDispatcher.MaxServerBuild);
            }
        }

        private sealed class State
        {
            [JsonProperty("servers")] public List<Entry> Servers { get; set; } = new List<Entry>();
        }

        // Bounded, or every server ever touched stays listed for ever; favourites are exempt from both limits.
        internal static readonly TimeSpan ForgetAfter = TimeSpan.FromDays(60);
        internal const int MaxRemembered = 100;

        private static readonly object _lock = new object();
        private static List<Entry> _entries;

        // GenFilePaths reaches into Unity, which the offline self-test runner has no engine for.
        internal static string RootOverrideForTest;

        private static string Root => RootOverrideForTest
            ?? Persistence.KmhDataFolder.Root;

        private static string FilePath       => Path.Combine(Root, "seen-servers.json");
        private static string LegacyFilePath => Path.Combine(KmhLog.LogFolderPath, "seen-servers.txt");

        // The hello's id is only the far end's claim: kept only when a listed card at the dialled address backs it.
        internal static string TrustedId(string claimedId, string dialled, IEnumerable<Global.ServerCardDto> cards)
        {
            if (string.IsNullOrEmpty(claimedId) || cards == null) return "";
            foreach (Global.ServerCardDto c in cards)
                if (c != null && c.FromPublicDirectory && string.Equals(c.ServerId, claimedId, StringComparison.Ordinal)
                    && Discord.KmhRichPresence.SameEndpoint(c.JoinEndpoint, dialled)) return claimedId;
            return "";
        }

        // An empty serverId stays endpoint-keyed until a later connection teaches it, then upgrades in place.
        public static void Record(string serverId, string name, string endpoint, string kmhVersion, string rwtVersion)
        {
            serverId = (serverId ?? "").Trim();
            endpoint = (endpoint ?? "").Trim();
            if (serverId.Length == 0 && endpoint.Length == 0) return;

            lock (_lock)
            {
                LoadNoLock();

                Entry entry = serverId.Length > 0
                    ? _entries.Find(x => string.Equals(x.ServerId, serverId, StringComparison.OrdinalIgnoreCase))
                    : null;

                // Adopt the endpoint-keyed record this connection just identified, or a stale twin stays behind;
                // with no trusted id, the record already kept for this address is the one updated.
                if (entry == null && endpoint.Length > 0)
                    entry = _entries.Find(x => (serverId.Length == 0 || !x.IdentityKnown)
                                            && string.Equals(x.Endpoint, endpoint, StringComparison.OrdinalIgnoreCase));

                if (entry == null) { entry = new Entry(); _entries.Add(entry); }

                if (serverId.Length > 0) entry.ServerId = serverId;
                if (endpoint.Length > 0) entry.Endpoint = endpoint;
                if (!string.IsNullOrEmpty(name)) entry.Name = name;
                entry.KmhVersion    = kmhVersion ?? "";
                entry.RwtVersion    = rwtVersion ?? "";
                entry.LastSeenTicks = DateTime.UtcNow.Ticks;
                entry.MakeInert();

                PruneNoLock(DateTime.UtcNow);
                SaveNoLock();
            }
        }

        // A renamed or moved server would otherwise keep its old name here until the player next joined it.
        public static int RefreshFromDirectory(IList<Entry> current)
        {
            if (current == null || current.Count == 0) return 0;
            lock (_lock)
            {
                LoadNoLock();
                int changed = 0;
                foreach (Entry fresh in current)
                {
                    if (fresh == null || string.IsNullOrEmpty(fresh.ServerId)) continue;
                    Entry mine = _entries.Find(x => string.Equals(x.ServerId, fresh.ServerId, StringComparison.OrdinalIgnoreCase));
                    if (mine == null) continue;

                    if (!string.IsNullOrEmpty(fresh.Name) && mine.Name != fresh.Name) { mine.Name = fresh.Name; changed++; }
                    if (!string.IsNullOrEmpty(fresh.Endpoint) && mine.Endpoint != fresh.Endpoint) { mine.Endpoint = fresh.Endpoint; changed++; }
                    if (!string.IsNullOrEmpty(fresh.RwtVersion) && mine.RwtVersion != fresh.RwtVersion) { mine.RwtVersion = fresh.RwtVersion; changed++; }
                    if (!string.IsNullOrEmpty(fresh.KmhVersion) && mine.KmhVersion != fresh.KmhVersion) { mine.KmhVersion = fresh.KmhVersion; changed++; }
                    mine.MakeInert();
                }
                if (changed > 0) SaveNoLock();
                return changed;
            }
        }

        // Favourites are never dropped, however old: the player said to keep them.
        internal static int PruneNoLock(DateTime nowUtc)
        {
            long cutoff = (nowUtc - ForgetAfter).Ticks;
            int before = _entries.Count;

            _entries.RemoveAll(x => x == null
                                 || (!x.IdentityKnown && string.IsNullOrEmpty(x.Endpoint))
                                 || (!x.Favorite && x.LastSeenTicks < cutoff));

            if (_entries.Count > MaxRemembered)
            {
                List<Entry> keep = new List<Entry>();
                List<Entry> rest = new List<Entry>();
                foreach (Entry e in _entries) (e.Favorite ? keep : rest).Add(e);
                rest.Sort((a, b) => b.LastSeenTicks.CompareTo(a.LastSeenTicks));
                for (int i = 0; i < rest.Count && keep.Count < MaxRemembered; i++) keep.Add(rest[i]);
                _entries = keep;
            }

            return before - _entries.Count;
        }

        public static List<Entry> All()
        {
            lock (_lock)
            {
                LoadNoLock();
                List<Entry> copy = new List<Entry>(_entries);
                copy.Sort((a, b) => b.LastSeenTicks.CompareTo(a.LastSeenTicks));
                return copy;
            }
        }

        public static bool IsFavorite(string serverId)
        {
            if (string.IsNullOrEmpty(serverId)) return false;
            lock (_lock)
            {
                LoadNoLock();
                foreach (Entry e in _entries) if (e.ServerId == serverId) return e.Favorite;
                return false;
            }
        }

        public static bool IsKnown(string serverId)
        {
            if (string.IsNullOrEmpty(serverId)) return false;
            lock (_lock)
            {
                LoadNoLock();
                foreach (Entry e in _entries) if (e.ServerId == serverId) return true;
                return false;
            }
        }

        // Favouriting from the browser before any join creates the row rather than dropping the click.
        public static void SetFavorite(string serverId, string name, bool favorite)
        {
            if (string.IsNullOrEmpty(serverId)) return;
            lock (_lock)
            {
                LoadNoLock();
                foreach (Entry e in _entries)
                    if (e.ServerId == serverId) { e.Favorite = favorite; SaveNoLock(); return; }

                if (!favorite) return;
                _entries.Add(new Entry { ServerId = serverId, Name = name ?? "", Favorite = true });
                SaveNoLock();
            }
        }

        // Takes whichever key the caller has; an unidentified entry can still only be named by its endpoint.
        public static void Forget(string serverIdOrEndpoint)
        {
            string key = (serverIdOrEndpoint ?? "").Trim();
            if (key.Length == 0) return;

            lock (_lock)
            {
                LoadNoLock();
                _entries.RemoveAll(x => string.Equals(x.ServerId, key, StringComparison.OrdinalIgnoreCase)
                                     || (!x.IdentityKnown && string.Equals(x.Endpoint, key, StringComparison.OrdinalIgnoreCase)));
                SaveNoLock();
            }
        }

        private static void LoadNoLock()
        {
            if (_entries != null) return;
            _entries = new List<Entry>();

            try
            {
                if (File.Exists(FilePath))
                {
                    State s = JsonConvert.DeserializeObject<State>(File.ReadAllText(FilePath));
                    if (s?.Servers != null) _entries.AddRange(s.Servers);
                    // Re-inerted rather than trusted: written by older builds that kept the hello raw.
                    foreach (Entry e in _entries) e?.MakeInert();
                    // Applied on load, so an oversized history is tidied once, quietly.
                    int dropped = PruneNoLock(DateTime.UtcNow);
                    if (dropped > 0)
                    {
                        KmhLog.Info($"Seen-servers: forgot {dropped} server(s) not seen in "
                                  + $"{(int)ForgetAfter.TotalDays} days. Favourites are kept.");
                        SaveNoLock();
                    }
                    return;
                }
            }
            catch (Exception ex) { KmhLog.Warn($"Seen-servers load failed: {ex.Message}"); _entries = new List<Entry>(); }

            MigrateLegacyNoLock();
        }

        // Imported, not abandoned, and the original is retired only once the new file is actually on disk.
        private static void MigrateLegacyNoLock()
        {
            string legacy;
            try { legacy = LegacyFilePath; } catch { return; }   // no log folder outside the game

            try
            {
                if (!File.Exists(legacy)) return;

                int imported = 0;
                foreach (string line in File.ReadAllLines(legacy))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    string[] parts = line.Split('\t');
                    if (parts.Length < 4) continue;
                    if (!long.TryParse(parts[3], out long ticks)) ticks = 0;

                    string endpoint = Decode(parts[0]);
                    if (endpoint.Length == 0) continue;

                    _entries.Add(new Entry
                    {
                        ServerId      = "",          // unknown until this server is next connected to
                        Endpoint      = endpoint,
                        KmhVersion    = Decode(parts[1]),
                        RwtVersion    = Decode(parts[2]),
                        LastSeenTicks = ticks,
                    });
                    imported++;
                }

                if (imported == 0) return;
                if (!SaveNoLock())
                {
                    // Left where it is: losing the old file and failing to write the new one loses the history.
                    KmhLog.Warn("Seen-servers: could not write the migrated history - keeping the old file.");
                    return;
                }
                try { File.Move(legacy, legacy + ".migrated"); }
                catch (Exception ex) { KmhLog.Warn($"Seen-servers: migrated, but could not retire the old file: {ex.Message}"); }
                KmhLog.Info($"Seen-servers: imported {imported} server(s) from the old log-folder history.");
            }
            catch (Exception ex) { KmhLog.Warn($"Seen-servers legacy import failed: {ex.Message}"); }
        }

        private static bool SaveNoLock()
            => Persistence.KmhAtomicFile.TryWrite(
                FilePath, JsonConvert.SerializeObject(new State { Servers = _entries }, Formatting.Indented),
                "Seen-servers");

        private static string Decode(string value)
        {
            try
            {
                if (string.IsNullOrEmpty(value)) return "";
                return Encoding.UTF8.GetString(Convert.FromBase64String(value));
            }
            catch { return ""; }
        }

        internal static void ResetForTest()
        {
            lock (_lock) { _entries = null; try { if (File.Exists(FilePath)) File.Delete(FilePath); } catch { } }
        }

        // Drops what is held so the next read comes from the file, as a fresh launch would.
        internal static void ReloadForTest()
        {
            lock (_lock) _entries = null;
        }

        // The live list, so a test can age an entry rather than wait sixty days for one.
        internal static List<Entry> AllForTest()
        {
            lock (_lock) { LoadNoLock(); return _entries; }
        }

        internal static int PruneForTest(DateTime nowUtc)
        {
            lock (_lock) { LoadNoLock(); int n = PruneNoLock(nowUtc); SaveNoLock(); return n; }
        }
    }
}
