using System;
using System.IO;
using System.Text;
using KMHPatch.Diagnostics;

namespace KMHPatch.Persistence
{
    // The one shared replace-in-place: delete-then-move copies lost the whole file if the process stopped in between.
    internal static class KmhAtomicFile
    {
        // Counted, not timed: a durable flush takes 300ms-4s depending on machine load, so a time budget is flaky.
        internal static int DurableFlushCount;

        // Self-test only, restored in a finally: its assertions are about atomicity, which File.Replace gives alone.
        internal static bool SkipDurableFlushForTest;

        internal sealed class Options
        {
            // Off for a UI preference; on for anything a crash must not lose.
            public bool Durable = true;
            // Fires mid-write, the only moment that tells an atomic write from an in-place truncate.
            public Action DuringWrite;
            public Action<string> BeforeReplace;
            public Action<string> AfterReplace;
        }

        private static readonly Options Default = new Options();

        // Throws, so a caller whose failure path is "do not acknowledge this" can let it propagate.
        public static void Write(string path, string content, Options options = null)
            => WriteBytes(path, new UTF8Encoding(false).GetBytes(content ?? ""), options);

        // Raw bytes: a restored player config must match exactly, and re-encoding changes the BOM and line endings.
        public static void WriteBytes(string path, byte[] bytes, Options options = null)
        {
            Options o = options ?? Default;
            bytes = bytes ?? Array.Empty<byte>();
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            // Unique, so two writers racing on the same file cannot adopt each other's half-written temp.
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (FileStream stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    o.DuringWrite?.Invoke();
                    if (!o.Durable || SkipDurableFlushForTest) stream.Flush();
                    else { DurableFlushCount++; stream.Flush(true); }
                }

                o.BeforeReplace?.Invoke(temp);
                Promote(temp, path);
                o.AfterReplace?.Invoke(path);
            }
            catch
            {
                // A leftover temp is reported by the integrity check; cleanup must never raise a second, different failure.
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
                throw;
            }
        }

        // Replace, never delete-then-move: the old file survives until the new one is in place.
        public static void Promote(string tempPath, string path)
        {
            if (File.Exists(path)) File.Replace(tempPath, path, null);
            else File.Move(tempPath, path);
        }

        // False instead of throwing, for callers whose failure path is "say so and keep holding the data".
        public static bool TryWrite(string path, string content, string what, Options options = null)
        {
            try { Write(path, content, options); return true; }
            catch (Exception ex) { KmhLog.Warn($"{what}: could not write '{path}': {ex.Message}"); return false; }
        }

        internal static void ResetForTest()
        {
            SkipDurableFlushForTest = false;
            DurableFlushCount = 0;
        }
    }
}
