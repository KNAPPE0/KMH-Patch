using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using KMHPatch.Diagnostics;

namespace KMHPatch.Features.Enforcement
{
    // RimWorld owns this file: only <activeMods> changes, and any failure restores the pre-write backup.
    internal static class ModSyncApply
    {
        internal sealed class Result
        {
            public bool Applied;
            public bool Changed;
            public string Hash = "";
            public string Failure = "";
            public bool RolledBack;
        }

        internal static string PathOverrideForTest;
        internal static Action<string> BeforeReplaceForTest;
        internal static Action<string> AfterReplaceForTest;
        internal static Func<string, string> ReadBackOverrideForTest;
        internal static Action FailBackupForTest;
        internal static Action BeforeBackupForTest;
        // Fires mid-write: the only moment that tells an atomic write from one truncating the real file in place.
        internal static Action DuringWriteForTest;

        internal static string ConfigPath =>
            PathOverrideForTest ?? Verse.GenFilePaths.ModsConfigFilePath;

        internal static string BackupPathFor(string path) => path + ".kmh-backup";

        public static Result Apply(IReadOnlyList<string> wantedOrder)
        {
            Result result = new Result();
            string path = ConfigPath;
            // Set once this call has its own recovery copy: only then can anything it does need undoing.
            string original = null;
            bool backedUp = false;

            try
            {
                if (wantedOrder == null || wantedOrder.Count == 0)
                { result.Failure = "no order to apply"; return result; }

                if (!File.Exists(path))
                { result.Failure = "RimWorld's ModsConfig.xml is not where RimWorld says it is"; return result; }

                original = File.ReadAllText(path);
                string rebuilt = Rebuild(original, wantedOrder, out string buildError);
                if (buildError != null) { result.Failure = buildError; return result; }

                // Already correct: touching the file would risk a working install for no change.
                if (string.Equals(rebuilt, original, StringComparison.Ordinal))
                {
                    result.Applied = true;
                    result.Changed = false;
                    result.Hash = Sha256Upper(Encoding.UTF8.GetBytes(original));
                    return result;
                }

                // Validated before anything is written: a document that will not parse must never reach the file.
                if (!ActiveModsOf(rebuilt, out List<string> check, out string parseError) || parseError != null)
                { result.Failure = "the rewritten mod list would not parse: " + (parseError ?? "unknown"); return result; }
                if (!SameSequence(check, wantedOrder))
                { result.Failure = "the rewritten mod list does not match the order it was built from"; return result; }

                BeforeBackupForTest?.Invoke();
                if (!Backup(path, original, out string backupError))
                { result.Failure = backupError; return result; }
                backedUp = true;

                WriteAtomic(path, rebuilt, isTarget: true);

                // Read back from disk, not from memory: the point is to prove what a restart will actually load.
                string onDisk = ReadBackOverrideForTest != null ? ReadBackOverrideForTest(path) : File.ReadAllText(path);
                string expected = Sha256Upper(Encoding.UTF8.GetBytes(rebuilt));
                string actual = Sha256Upper(Encoding.UTF8.GetBytes(onDisk ?? ""));

                if (!string.Equals(expected, actual, StringComparison.Ordinal)
                    || !ActiveModsOf(onDisk, out List<string> finalIds, out _)
                    || !SameSequence(finalIds, wantedOrder))
                {
                    result.Failure = "the mod list on disk is not the one Colony Nexus wrote";
                    result.RolledBack = RollbackTo(path, original);
                    return result;
                }

                result.Applied = true;
                result.Changed = true;
                result.Hash = actual;
                return result;
            }
            catch (Exception ex)
            {
                result.Failure = ex.GetType().Name + ": " + ex.Message;
                // Roll back only after this call's own backup; never restore an older backup a previous apply left.
                result.RolledBack = backedUp && RollbackTo(path, original);
                KmhLog.Warn($"ModSync: applying the mod order failed ({result.Failure})"
                          + (!backedUp ? " - nothing was changed." : result.RolledBack ? " - the previous mod list was restored." : " - RESTORE ALSO FAILED."));
                return result;
            }
        }

        // Everything outside <activeMods>, including whatever a future RimWorld adds, is carried through.
        internal static string Rebuild(string originalXml, IReadOnlyList<string> wantedOrder, out string error)
        {
            error = null;
            try
            {
                XmlDocument doc = new XmlDocument { XmlResolver = null };
                using (XmlReader reader = XmlReader.Create(new StringReader(originalXml), new XmlReaderSettings
                { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }))
                    doc.Load(reader);

                XmlNode active = doc.SelectSingleNode("/ModsConfigData/activeMods");
                if (active == null) { error = "the existing mod list has no activeMods section"; return null; }

                while (active.FirstChild != null) active.RemoveChild(active.FirstChild);
                foreach (string id in wantedOrder)
                {
                    XmlElement li = doc.CreateElement("li");
                    li.InnerText = id;
                    active.AppendChild(li);
                }

                var settings = new XmlWriterSettings { Indent = true, IndentChars = "  ", Encoding = new UTF8Encoding(false) };
                var sb = new StringBuilder();
                using (XmlWriter writer = XmlWriter.Create(sb, settings)) doc.Save(writer);
                return sb.ToString();
            }
            catch (Exception ex)
            {
                // A malformed existing file is a refusal, not something to overwrite with a guess.
                error = "the existing mod list could not be read (" + ex.GetType().Name + ")";
                return null;
            }
        }

        internal static bool ActiveModsOf(string xml, out List<string> ids, out string error)
        {
            ids = new List<string>();
            error = null;
            if (string.IsNullOrEmpty(xml)) { error = "empty document"; return false; }
            try
            {
                using (XmlReader reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings
                { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, IgnoreWhitespace = true }))
                {
                    bool inActive = false, advance = true;
                    while (true)
                    {
                        if (advance && !reader.Read()) break;
                        advance = true;
                        if (reader.NodeType == XmlNodeType.Element && reader.Name == "activeMods") inActive = true;
                        else if (reader.NodeType == XmlNodeType.EndElement && reader.Name == "activeMods") break;
                        else if (inActive && reader.NodeType == XmlNodeType.Element && reader.Name == "li")
                        {
                            string id = (reader.ReadElementContentAsString() ?? "").Trim();
                            advance = false;
                            if (id.Length > 0) ids.Add(id);
                        }
                    }
                }
                return true;
            }
            catch (Exception ex) { error = ex.GetType().Name; return false; }
        }

        internal static bool SameSequence(IReadOnlyList<string> a, IReadOnlyList<string> b)
        {
            if (a == null || b == null || a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
                if (!string.Equals(a[i], b[i], StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }

        // Kept after the write, so a crash between write and readback is recoverable on the next boot.
        private static bool Backup(string path, string original, out string error)
        {
            error = null;
            try
            {
                FailBackupForTest?.Invoke();
                WriteAtomic(BackupPathFor(path), original, isTarget: false);
                string check = File.ReadAllText(BackupPathFor(path));
                if (!string.Equals(check, original, StringComparison.Ordinal))
                { error = "the recovery copy of the mod list did not read back"; return false; }
                return true;
            }
            catch (Exception ex)
            {
                // No backup means no rollback, so nothing is written at all.
                error = "could not make a recovery copy of the mod list (" + ex.GetType().Name + ")";
                return false;
            }
        }

        // Puts back the exact text this call read, not whatever a .kmh-backup on disk says.
        private static bool RollbackTo(string path, string original)
        {
            if (original == null) return false;
            try
            {
                WriteAtomic(path, original, isTarget: false);
                return string.Equals(File.ReadAllText(path), original, StringComparison.Ordinal);
            }
            catch (Exception ex)
            {
                KmhLog.Error($"ModSync: restoring the previous mod list failed: {ex.Message}. A copy is at {BackupPathFor(path)}.");
                return false;
            }
        }

        internal static bool Rollback(string path)
        {
            try
            {
                string backup = BackupPathFor(path);
                if (!File.Exists(backup)) return false;
                WriteAtomic(path, File.ReadAllText(backup), isTarget: false);
                return string.Equals(File.ReadAllText(path), File.ReadAllText(backup), StringComparison.Ordinal);
            }
            catch (Exception ex)
            {
                KmhLog.Error($"ModSync: restoring the previous mod list failed: {ex.Message}. "
                           + $"A copy is at {BackupPathFor(path)}.");
                return false;
            }
        }

        // isTarget scopes the test hooks to the mod list write, so a simulated crash cannot pass by hitting another.
        private static void WriteAtomic(string path, string content, bool isTarget)
            => Persistence.KmhAtomicFile.Write(path, content, isTarget
                ? new Persistence.KmhAtomicFile.Options
                  {
                      DuringWrite   = () => DuringWriteForTest?.Invoke(),
                      BeforeReplace = t => BeforeReplaceForTest?.Invoke(t),
                      AfterReplace  = p => AfterReplaceForTest?.Invoke(p),
                  }
                : null);

        internal static string Sha256Upper(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] h = sha.ComputeHash(bytes);
                StringBuilder hex = new StringBuilder(h.Length * 2);
                foreach (byte b in h) hex.Append(b.ToString("X2"));
                return hex.ToString();
            }
        }

        internal static void ResetForTest()
        {
            PathOverrideForTest = null;
            BeforeReplaceForTest = null;
            AfterReplaceForTest = null;
            ReadBackOverrideForTest = null;
            FailBackupForTest = null;
            BeforeBackupForTest = null;
            DuringWriteForTest = null;
        }
    }
}
