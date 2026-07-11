using System;
using System.IO;
using Verse;

namespace KMHPatch.Diagnostics
{
    // Logs to Verse.Log + a KMH-only file (kmh-patch.log) so bug reports aren't buried in other mods' Player.log noise.
    // File-write failures are swallowed (logged once) so a disk problem can't break the in-game logger.
    internal static class KmhLog
    {
        private static readonly object FileLock = new object();
        private static string _logFilePath;
        private static bool   _fileSinkFailed;

        // gold [KMH-Patch] mark so KMH lines stand out in the dev console (file sink stays plain)
        private const string Gold = "#F4B83C";
        private const string Body = "#E6DCC2";   // normal message text
        private const string Dim  = "#94A0B0";   // verbose/secondary
        private static readonly string Mark =
            "<color=#6E7689>[</color><color=" + Gold + "><b>KMH</b></color><color=#B5872F>-Patch</color><color=#6E7689>]</color>";

        private static string Con(string body, string hex) => $"{Mark} <color={hex}>{body}</color>";

        public static string LogFilePath
        {
            get
            {
                if (_logFilePath == null)
                {
                    string dir = Path.Combine(GenFilePaths.SaveDataFolderPath, "KMH-Patch");
                    try { Directory.CreateDirectory(dir); } catch { /* handled at write time */ }
                    _logFilePath = Path.Combine(dir, "kmh-patch.log");
                }
                return _logFilePath;
            }
        }

        public static string LogFolderPath => Path.GetDirectoryName(LogFilePath);

        public static void Info(string message)
        {
            WriteToFile(Format("INFO", message));
            KmhMainThread.Post(() => Log.Message(Con(message, Body)));   // Verse.Log is main-thread only
        }

        public static void Warn(string message)
        {
            // RimWorld tints the whole line yellow; the gold KMH mark still reads inside it.
            WriteToFile(Format("WARN", message));
            KmhMainThread.Post(() => Log.Warning($"{Mark} {message}"));
        }

        public static void Error(string message)
        {
            WriteToFile(Format("ERROR", message));
            KmhMainThread.Post(() => Log.Error($"{Mark} {message}"));
        }

        public static void Error(string message, Exception ex)
        {
            Error($"{message}: {ex}");
        }

        // milestone - green in console, plain in file
        public static void Success(string message)
        {
            WriteToFile(Format("OK", message));
            KmhMainThread.Post(() => Log.Message(Con(message, "#6EC06E")));
        }

        // verbose; off unless DebugEnabled
        public static void Debug(string message)
        {
            if (!DebugEnabled) return;
            WriteToFile(Format("DEBUG", message));
            KmhMainThread.Post(() => Log.Message(Con(message, Dim)));
        }

        // per-packet trace; gated by DebugEnabled
        public static void Protocol(string message)
        {
            if (!DebugEnabled) return;
            WriteToFile(Format("PROTOCOL", message));
            KmhMainThread.Post(() => Log.Message($"{Mark} <color=#9BB8E0>[protocol]</color> <color={Dim}>{message}</color>"));
        }

        // gates Debug/Protocol
        public static bool DebugEnabled { get; set; }

        private static string Format(string level, string message)
        {
            return $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
        }

        private static void WriteToFile(string line)
        {
            try { KmhDebugUplink.Enqueue(line); } catch { }   // mirror to the server when the uplink is active

            if (_fileSinkFailed) return;

            try
            {
                lock (FileLock)
                {
                    File.AppendAllText(LogFilePath, line + Environment.NewLine);
                }
            }
            catch (Exception ex)
            {
                _fileSinkFailed = true;
                KmhMainThread.Post(() => Log.Warning(
                    $"{Mark} File sink disabled - could not write to '{LogFilePath}': {ex.Message}"
                ));
            }
        }
    }
}
