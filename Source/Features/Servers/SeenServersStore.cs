using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using KMHPatch.Diagnostics;

namespace KMHPatch.Features.Servers
{
    // Local, client-only history of KMH servers the player has joined.
    internal static class SeenServersStore
    {
        public class Entry
        {
            public string Endpoint { get; set; } = "";
            public string KmhVersion { get; set; } = "";
            public string RwtVersion { get; set; } = "";
            public long LastSeenTicks { get; set; }
        }

        private static readonly object _lock = new object();
        private static List<Entry> _entries;

        private static string FilePath => Path.Combine(KmhLog.LogFolderPath, "seen-servers.txt");

        public static void Record(string endpoint, string kmhVersion, string rwtVersion)
        {
            endpoint = (endpoint ?? "").Trim();
            if (endpoint.Length == 0) return;

            lock (_lock)
            {
                LoadNoLock();

                Entry entry = _entries.Find(x => string.Equals(x.Endpoint, endpoint, StringComparison.OrdinalIgnoreCase));
                if (entry == null)
                {
                    entry = new Entry { Endpoint = endpoint };
                    _entries.Add(entry);
                }

                entry.KmhVersion = kmhVersion ?? "";
                entry.RwtVersion = rwtVersion ?? "";
                entry.LastSeenTicks = DateTime.UtcNow.Ticks;

                SaveNoLock();
            }
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

        public static void Forget(string endpoint)
        {
            endpoint = (endpoint ?? "").Trim();
            if (endpoint.Length == 0) return;

            lock (_lock)
            {
                LoadNoLock();
                _entries.RemoveAll(x => string.Equals(x.Endpoint, endpoint, StringComparison.OrdinalIgnoreCase));
                SaveNoLock();
            }
        }

        private static void LoadNoLock()
        {
            if (_entries != null) return;

            _entries = new List<Entry>();

            try
            {
                if (!File.Exists(FilePath)) return;

                foreach (string line in File.ReadAllLines(FilePath))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    string[] parts = line.Split('\t');
                    if (parts.Length < 4) continue;

                    if (!long.TryParse(parts[3], out long ticks))
                    {
                        ticks = 0;
                    }

                    string endpoint = Decode(parts[0]);
                    if (endpoint.Length == 0) continue;

                    _entries.Add(new Entry
                    {
                        Endpoint = endpoint,
                        KmhVersion = Decode(parts[1]),
                        RwtVersion = Decode(parts[2]),
                        LastSeenTicks = ticks
                    });
                }
            }
            catch (Exception ex)
            {
                KmhLog.Warn($"Seen-servers load failed: {ex.Message}");
                _entries = new List<Entry>();
            }
        }

        private static void SaveNoLock()
        {
            try
            {
                string dir = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                List<string> lines = new List<string>();

                foreach (Entry entry in _entries)
                {
                    if (string.IsNullOrEmpty(entry.Endpoint)) continue;

                    lines.Add(
                        $"{Encode(entry.Endpoint)}\t{Encode(entry.KmhVersion)}\t{Encode(entry.RwtVersion)}\t{entry.LastSeenTicks}");
                }

                File.WriteAllLines(FilePath, lines.ToArray());
            }
            catch (Exception ex)
            {
                KmhLog.Warn($"Seen-servers save failed: {ex.Message}");
            }
        }

        private static string Encode(string value)
        {
            value = value ?? "";
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
        }

        private static string Decode(string value)
        {
            try
            {
                if (string.IsNullOrEmpty(value)) return "";
                return Encoding.UTF8.GetString(Convert.FromBase64String(value));
            }
            catch
            {
                return "";
            }
        }
    }
}