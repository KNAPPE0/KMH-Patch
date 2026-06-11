using System;
using System.IO;
using Verse;

namespace KMHPatch.Diagnostics
{
    // Dual-sink logger for the patch mod.
    //
    // Every entry goes to:
    //   1. Verse.Log (visible in-game via Ctrl+F12, also Player.log)
    //   2. A dedicated file under <RimWorld user data>/KMH-Patch/kmh-patch.log
    //
    // The file sink survives across launches and is the easier thing to attach to a bug report - Player.log gets
    // noisy fast with vanilla / other mod chatter, while ours is KMH-only
    //
    // Failures writing to the file are silently swallowed (logged once to Verse.Log) so a disk problem can never
    // break the in-game logger
    internal static class KmhLog
    {
        private static readonly object FileLock = new object();
        private static string _logFilePath;
        private static bool   _fileSinkFailed;

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
            string line = Format("INFO", message);
            Log.Message($"{Constants.LogPrefix} {message}");
            WriteToFile(line);
        }

        public static void Warn(string message)
        {
            string line = Format("WARN", message);
            Log.Warning($"{Constants.LogPrefix} {message}");
            WriteToFile(line);
        }

        public static void Error(string message)
        {
            string line = Format("ERROR", message);
            Log.Error($"{Constants.LogPrefix} {message}");
            WriteToFile(line);
        }

        public static void Error(string message, Exception ex)
        {
            Error($"{message}: {ex}");
        }

        private static string Format(string level, string message)
        {
            return $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
        }

        private static void WriteToFile(string line)
        {
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
                Log.Warning(
                    $"{Constants.LogPrefix} File sink disabled - could not write to '{LogFilePath}': {ex.Message}"
                );
            }
        }
    }
}
