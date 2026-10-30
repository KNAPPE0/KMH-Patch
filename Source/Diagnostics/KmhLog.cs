using System;
using System.IO;
using Verse;

namespace KMHPatch.Diagnostics
{
    // File-write failures are swallowed, or a disk problem takes the in-game logger down with it.
    internal static class KmhLog
    {
        private static readonly object FileLock = new object();
        private static string _logFilePath;

        private const int MaxSinkBackoffSeconds = 300;
        private const int SinkGiveUpAfter       = 6;
        private static int  _sinkFailures;
        private static long _sinkRetryAtTicks;

        private const string Gold = "#F4B83C";
        private const string Body = "#E6DCC2";
        private const string Dim  = "#94A0B0";
        private static readonly string Mark =
            "<color=#6E7689>[</color><color=" + Gold + "><b>Colony</b></color><color=#B5872F> Nexus</color><color=#6E7689>]</color>";

        private static string Con(string body, string hex) => $"{Mark} <color={hex}>{body}</color>";

        // One file per session, or a long debug run grows unbounded with no way to tell one run from the next.
        private const long MaxLogBytes  = 10L * 1024 * 1024;
        private const int  MaxLogFiles  = 10;
        private const int  RetentionDays = 14;

        private static long   _written;
        private static int    _rollover;
        private static string _sessionStamp;
        private static string _sessionId;

        // Ties a client log, its uploaded copy and the server's own log to one run.
        public static string SessionId
        {
            get
            {
                if (_sessionId == null)
                {
                    _sessionStamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                    _sessionId    = _sessionStamp + "-" + Guid.NewGuid().ToString("N").Substring(0, 4);
                }
                return _sessionId;
            }
        }

        public static string LogFolderPath
        {
            get
            {
                string dir = Path.Combine(Persistence.KmhDataFolder.Root, "Logs");
                try { Directory.CreateDirectory(dir); } catch { /* handled at write time */ }
                return dir;
            }
        }

        public static string LogFilePath
        {
            get
            {
                if (_logFilePath == null)
                {
                    string unused = SessionId;   // fixes the stamp before it is used in the name
                    _logFilePath = Path.Combine(LogFolderPath, $"{FilePrefix}{_sessionStamp}.log");
                    StartSession();
                }
                return _logFilePath;
            }
        }

        // Header first, so a file found later says which build wrote it without anyone having to infer it.
        private static void StartSession()
        {
            try
            {
                Prune();
                File.AppendAllText(_logFilePath, string.Join(Environment.NewLine, new[]
                {
                    "=== KMH: Colony Nexus diagnostic log ===",
                    $"Build       : {SubProtocol.KmhProtocol.DisplayVersion} {SubProtocol.KmhProtocol.UiBuildTag}",
                    $"Protocol    : v{SubProtocol.KmhProtocol.CurrentVersion}",
                    $"RimWorld    : {Compat.KmhGameVersion.Full}",
                    $"SessionId   : {SessionId}",
                    $"StartedUtc  : {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}Z",
                    "",
                }) + Environment.NewLine);
            }
            catch { /* the write path reports a broken sink */ }
        }

        // Old logs are diagnostics, not history: drop them by age, then by count.
        private static void Prune()
        {
            try
            {
                var dir = new DirectoryInfo(LogFolderPath);
                if (!dir.Exists) return;
                FileInfo[] files = Logs(dir);
                DateTime cutoff = DateTime.Now.AddDays(-RetentionDays);
                foreach (FileInfo f in files)
                    if (f.LastWriteTime < cutoff) { try { f.Delete(); } catch { } }

                files = Logs(dir);
                if (files.Length <= MaxLogFiles) return;
                Array.Sort(files, (a, b) => a.LastWriteTime.CompareTo(b.LastWriteTime));
                for (int i = 0; i < files.Length - MaxLogFiles; i++) { try { files[i].Delete(); } catch { } }
            }
            catch { }
        }

        private const string FilePrefix = "colony-nexus_", LegacyFilePrefix = "kmh-patch_";

        // Legacy kmh-patch_ logs came over with the data folder and age out under the same rules.
        private static FileInfo[] Logs(DirectoryInfo dir)
        {
            FileInfo[] now = dir.GetFiles(FilePrefix + "*.log"), old = dir.GetFiles(LegacyFilePrefix + "*.log");
            FileInfo[] all = new FileInfo[now.Length + old.Length];
            now.CopyTo(all, 0);
            old.CopyTo(all, now.Length);
            return all;
        }

        // One session must not be able to fill the disk either, so it rolls at a bounded size.
        private static void RollIfNeeded()
        {
            if (_written < MaxLogBytes) return;
            _rollover++;
            _written = 0;
            _logFilePath = Path.Combine(LogFolderPath, $"{FilePrefix}{_sessionStamp}_{_rollover + 1:D3}.log");
            try
            {
                File.AppendAllText(_logFilePath,
                    $"=== continued from part {_rollover} · session {SessionId} ==={Environment.NewLine}");
            }
            catch { }
        }

        // Held while the self-test runs: it provokes failures on purpose, and those read as real ones in Player.log.
        internal static bool Muted;

        public static void Info(string message)
        {
            if (Muted) return;
            message = OneLine(message);
            WriteToFile(Format("INFO", message));
            KmhMainThread.Post(() => Log.Message(Con(message, Body)));   // Verse.Log is main-thread only
        }

        public static void Warn(string message)
        {
            if (Muted) return;
            message = OneLine(message);
            // RimWorld tints the whole line yellow; the gold KMH mark still reads inside it.
            string line = Format("WARN", message);
            NoteProblem(line);
            WriteToFile(line);
            KmhMainThread.Post(() => Log.Warning($"{Mark} {message}"));
        }

        // Counted, so the report offer notices a new error without scanning the problem queue every frame.
        internal static long ErrorCount;
        internal static volatile string LastError = "";

        public static void Error(string message)
        {
            message = OneLine(message);
            string line = Format("ERROR", message);
            LastError = message;
            System.Threading.Interlocked.Increment(ref ErrorCount);
            NoteProblem(line);
            WriteToFile(line);
            KmhMainThread.Post(() => Log.Error($"{Mark} {message}"));
        }

        internal const int RecentProblemsKept = 60;
        private static readonly System.Collections.Generic.Queue<string> _recentProblems = new System.Collections.Generic.Queue<string>();

        private static void NoteProblem(string line)
        {
            lock (_recentProblems)
            {
                if (_recentProblems.Count >= RecentProblemsKept) _recentProblems.Dequeue();
                _recentProblems.Enqueue(line);
            }
        }

        internal static System.Collections.Generic.List<string> RecentProblems()
        {
            lock (_recentProblems) return new System.Collections.Generic.List<string>(_recentProblems);
        }

        public static void Error(string message, Exception ex)
        {
            Error($"{message}: {ex}");
        }

        public static void Success(string message)
        {
            message = OneLine(message);
            WriteToFile(Format("OK", message));
            KmhMainThread.Post(() => Log.Message(Con(message, "#6EC06E")));
        }

        public static void Debug(string message)
        {
            if (Muted || !DebugEnabled) return;
            message = OneLine(message);
            WriteToFile(Format("DEBUG", message));
            KmhMainThread.Post(() => Log.Message(Con(message, Dim)));
        }

        public static void Protocol(string message)
        {
            if (!ProtocolEnabled) return;
            message = OneLine(message);
            WriteToFile(Format("PROTOCOL", message));
            KmhMainThread.Post(() => Log.Message($"{Mark} <color=#9BB8E0>[protocol]</color> <color={Dim}>{message}</color>"));
        }

        private static int      _beats;
        private static DateTime _beatsSince = DateTime.UtcNow;

        // Counted, never printed per beat: a pong is a routine success, and a missing one shows up as a disconnect.
        public static void Heartbeat()
        {
            if (!DebugEnabled && !ProtocolEnabled) return;
            _beats++;
            DateTime now = DateTime.UtcNow;
            if ((now - _beatsSince).TotalMinutes < 60d) return;
            int n = _beats; _beats = 0; _beatsSince = now;
            Debug($"KMH API heartbeat healthy - {n} pong(s) received over the last hour.");
        }

        public static bool DebugEnabled { get; set; }

        // Separate switch: turning on useful debugging should not also produce megabytes of packet chatter.
        public static bool ProtocolEnabled { get; set; }

        private static string Format(string level, string message)
        {
            return $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
        }

        private const char Delete = (char)0x7F;

        // One record, one line: an OS error's FormatMessage tail can carry CRLF and NUL bytes into file and uplink.
        internal static string OneLine(string message)
        {
            if (string.IsNullOrEmpty(message)) return message ?? "";
            var sb = new System.Text.StringBuilder(message.Length);
            bool broke = false;
            foreach (char c in message)
            {
                if (c == '\r' || c == '\n') { broke = true; continue; }
                if (c < ' ' || c == Delete) continue;   // NUL and the rest of C0; nothing KMH logs needs one
                if (broke && sb.Length > 0) sb.Append(" | ");
                broke = false;
                sb.Append(c);
            }
            return sb.ToString().TrimEnd();
        }

        private static void WriteToFile(string line)
        {
            try { KmhDebugUplink.Enqueue(line); } catch { }

            // Back off on IO errors rather than give up: a momentary failure must not lose the rest of the session.
            if (DateTime.UtcNow.Ticks < _sinkRetryAtTicks) return;

            try
            {
                lock (FileLock)
                {
                    string path = LogFilePath;   // also starts the session on first use
                    RollIfNeeded();
                    string text = line + Environment.NewLine;
                    File.AppendAllText(_logFilePath ?? path, text);
                    // Bytes, not chars: the file is UTF-8, so a char count rolls late on non-ASCII content.
                    _written += System.Text.Encoding.UTF8.GetByteCount(text);
                }
                if (_sinkFailures > 0)
                {
                    int had = _sinkFailures;
                    _sinkFailures = 0;
                    KmhMainThread.Post(() => Log.Message($"{Mark} File sink recovered after {had} failed write(s)."));
                }
            }
            catch (Exception ex)
            {
                _sinkFailures++;
                int seconds = Math.Min(MaxSinkBackoffSeconds, 1 << Math.Min(6, _sinkFailures));
                _sinkRetryAtTicks = DateTime.UtcNow.Ticks + TimeSpan.FromSeconds(seconds).Ticks;
                // One line per escalation, not one per attempt, or a disk problem becomes a console flood.
                if (_sinkFailures == 1 || _sinkFailures == SinkGiveUpAfter)
                {
                    string why = OneLine(ex.Message);
                    bool giving = _sinkFailures >= SinkGiveUpAfter;
                    KmhMainThread.Post(() => Log.Warning(
                        $"{Mark} Could not write '{LogFilePath}': {why}"
                        + (giving ? " - retrying every few minutes; in-game logging continues." : " - retrying shortly.")));
                }
            }
        }
    }
}
