using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using Verse;

namespace KMHPatch.Features.Reports
{
    // The game keeps no time per log message, so this notes when each warning and error was first and last seen.
    internal static class KmhGameLogWatch
    {
        internal const float EverySeconds = 2f;
        internal const int MaxGroups = 200;
        internal const int ReportedGroups = 30;
        internal const int MaxSignatureChars = 160;

        private sealed class Seen { public DateTime First, Last; public int Repeats; }

        internal sealed class Group
        {
            public string Level = "", Signature = "", Sample = "";
            public int Count;
            public DateTime First, Last;
        }

        internal sealed class GroupTable
        {
            private readonly object _lock = new object();
            private readonly Dictionary<string, Group> _groups = new Dictionary<string, Group>(StringComparer.Ordinal);
            private readonly int _max;

            public GroupTable(int max) { _max = Math.Max(1, max); }

            public int Count { get { lock (_lock) return _groups.Count; } }

            public void Observe(string level, string text, DateTime at, int count)
            {
                string sig = Signature(text);
                if (sig.Length == 0 || count <= 0) return;
                string key = level + "|" + sig;
                lock (_lock)
                {
                    if (!_groups.TryGetValue(key, out Group g))
                    {
                        if (_groups.Count >= _max) EvictOldestLocked();
                        g = new Group { Level = level, Signature = sig, Sample = KmhReportDiagnostics.Cap(FirstLine(StripTags(text)), 300), First = at };
                        _groups[key] = g;
                    }
                    g.Count += count;
                    if (at > g.Last) g.Last = at;
                }
            }

            private void EvictOldestLocked()
            {
                string oldest = null;
                DateTime at = DateTime.MaxValue;
                foreach (KeyValuePair<string, Group> kv in _groups)
                    if (kv.Value.Last < at) { at = kv.Value.Last; oldest = kv.Key; }
                if (oldest != null) _groups.Remove(oldest);
            }

            public List<Group> Top(int max)
            {
                List<Group> list;
                lock (_lock) list = new List<Group>(_groups.Values);
                list.Sort((a, b) => a.Count != b.Count ? b.Count.CompareTo(a.Count) : b.Last.CompareTo(a.Last));
                if (list.Count > max) list.RemoveRange(max, list.Count - max);
                return list;
            }

            public string TopErrorSignature()
            {
                foreach (Group g in Top(_max))
                    if (g.Level == "error") return g.Signature;
                return "";
            }

            public JArray Section(int max)
            {
                var arr = new JArray();
                foreach (Group g in Top(max))
                    arr.Add(new JObject
                    {
                        ["level"]      = g.Level,
                        ["count"]      = g.Count,
                        ["first_seen"] = g.First.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                        ["last_seen"]  = g.Last.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                        ["signature"]  = g.Signature,
                        ["sample"]     = g.Sample,
                    });
                return arr;
            }
        }

        private sealed class ByReference : IEqualityComparer<LogMessage>
        {
            public bool Equals(LogMessage a, LogMessage b) => ReferenceEquals(a, b);
            public int GetHashCode(LogMessage m) => RuntimeHelpers.GetHashCode(m);
        }

        internal static readonly GroupTable Live = new GroupTable(MaxGroups);

        private static readonly object _seenLock = new object();
        private static readonly Dictionary<LogMessage, Seen> _seen = new Dictionary<LogMessage, Seen>(new ByReference());
        private static float _next;

        public static void Tick()
        {
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (now < _next) return;
            _next = now + EverySeconds;
            Sample(DateTime.Now);
        }

        // Retried: the game can append to its log while the list is copied. Null after three failed tries.
        internal static List<LogMessage> CopyGameLog()
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try { return new List<LogMessage>(Log.Messages); }
                catch (InvalidOperationException) { }
            }
            return null;
        }

        private static void Sample(DateTime now)
        {
            List<LogMessage> all = CopyGameLog();
            if (all == null) return;
            lock (_seenLock)
            {
                foreach (LogMessage m in all)
                {
                    if (m == null || m.type == LogMessageType.Message) continue;
                    string level = m.type == LogMessageType.Error ? "error" : "warning";
                    if (!_seen.TryGetValue(m, out Seen s))
                    {
                        _seen[m] = new Seen { First = now, Last = now, Repeats = m.repeats };
                        Live.Observe(level, m.text, now, Math.Max(1, m.repeats));
                    }
                    else if (m.repeats > s.Repeats)
                    {
                        Live.Observe(level, m.text, now, m.repeats - s.Repeats);
                        s.Repeats = m.repeats;
                        s.Last = now;
                    }
                }
                // Messages the game dropped from its queue are not coming back.
                if (_seen.Count > all.Count + 100)
                {
                    var live = new HashSet<LogMessage>(all, new ByReference());
                    var gone = new List<LogMessage>();
                    foreach (LogMessage k in _seen.Keys) if (!live.Contains(k)) gone.Add(k);
                    foreach (LogMessage k in gone) _seen.Remove(k);
                }
            }
        }

        internal static string SeenAt(LogMessage m)
        {
            lock (_seenLock)
                return m != null && _seen.TryGetValue(m, out Seen s) ? "seen " + s.First.ToString("HH:mm:ss", CultureInfo.InvariantCulture) : "";
        }

        private static readonly Regex Tags = new Regex(@"</?(?:color|b|i|size)(?:=[^>]*)?>", RegexOptions.IgnoreCase);
        private static readonly Regex WindowsPath = new Regex(@"[A-Za-z]:[\\/][^\s'""<>|]*");
        private static readonly Regex UnixPath = new Regex(@"(?<![\w.:/])(?:~|/)[\w.\-]+(?:[\\/][\w.\-]+)+");
        private static readonly Regex HexRun = new Regex(@"\b(?:0x)?[0-9a-fA-F]{8,}\b");
        private static readonly Regex Number = new Regex(@"\d+");
        private static readonly Regex Space = new Regex(@"\s+");

        // What stays the same when the same fault happens again on another machine: no numbers, ids or paths.
        internal static string Signature(string text)
        {
            string line = FirstLine(StripTags(text));
            if (line.Length == 0) return "";
            line = WindowsPath.Replace(line, "<path>");
            line = UnixPath.Replace(line, "<path>");
            line = HexRun.Replace(line, "#");
            line = Number.Replace(line, "#");
            line = Space.Replace(line, " ").Trim();
            return line.Length <= MaxSignatureChars ? line : line.Substring(0, MaxSignatureChars);
        }

        // Twelve hex characters: the same fault on the same version groups together on Global.
        internal static string Fingerprint(string kmhVersion, string topErrorSignature, string title)
        {
            string basis = string.IsNullOrEmpty(topErrorSignature)
                ? "title:" + Space.Replace((title ?? "").ToLowerInvariant(), " ").Trim()
                : "error:" + topErrorSignature;
            byte[] digest;
            using (SHA256 sha = SHA256.Create()) digest = sha.ComputeHash(Encoding.UTF8.GetBytes((kmhVersion ?? "") + "|" + basis));
            var sb = new StringBuilder(12);
            for (int i = 0; i < 6; i++) sb.Append(digest[i].ToString("x2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        private static string StripTags(string s) => string.IsNullOrEmpty(s) ? "" : Tags.Replace(s, "");

        private static string FirstLine(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            foreach (string part in s.Replace("\r\n", "\n").Split('\n'))
                if (part.Trim().Length > 0) return part.Trim();
            return "";
        }
    }
}
