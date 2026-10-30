using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using KMHPatch.Diagnostics;
using Newtonsoft.Json;

namespace KMHPatch.Features.Enforcement
{
    // ModsConfig.xml is outside the Config journal's managed set, so its bytes and the player's list live here.
    internal static class ModsConfigJournal
    {
        private sealed class Record
        {
            public string Generation = "";
            public string Bytes = "";
            public List<string> Written = new List<string>();
            public string SavedUtc = "";
            public string Reason = "";
            // Written by this build when it saves the player's own list; older records carry no such word.
            public bool Own;
        }

        internal enum RestoreOutcome { Nothing, Restored, KeptYourChange, SavedCopyWasAServerList }

        private static string JournalPath(string root) => Path.Combine(root, "KMH_MODSCONFIG_JOURNAL.json");
        private static string OriginalPath(string root) => Path.Combine(root, "KMH_MODSCONFIG_ORIGINAL.json");
        // Every list Colony Nexus stops keeping as "yours" lands here first, so nothing it held is ever thrown away.
        private static string ArchiveDir(string root) => Path.Combine(root, "ConfigKMH_ModLists");
        internal const int ArchiveKept = 12;

        // A saved copy that is itself a server's list (older builds replaced the player's) restores nothing.
        internal static bool HasOriginal(string root)
        {
            Record r = Read(OriginalPath(root));
            return r != null && !IsServerShaped(r);
        }

        internal static bool HasDamagedOriginal(string root)
        {
            Record r = Read(OriginalPath(root));
            return r != null && IsServerShaped(r);
        }

        // Durable before the write it protects. A missing file is left to ModSync, which refuses to invent one.
        public static void Begin(string root, string generation)
        {
            string path = ModSyncApply.ConfigPath;
            if (!File.Exists(path)) { Discard(root); return; }
            Write(JournalPath(root), new Record
            {
                Generation = generation ?? "",
                Bytes = Convert.ToBase64String(File.ReadAllBytes(path)),
            });
        }

        public static void Discard(string root)
        {
            string journal = JournalPath(root);
            if (File.Exists(journal)) File.Delete(journal);
        }

        // True when there was nothing to undo, or the file is back to the exact bytes it had.
        public static bool RollBack(string root)
        {
            try
            {
                Record journal = Read(JournalPath(root));
                if (journal == null) return true;
                if (!PutBack(Convert.FromBase64String(journal.Bytes))) return false;
                Discard(root);
                return true;
            }
            catch (Exception ex)
            {
                KmhLog.Error($"ModSync: the mod list from before this apply could not be put back: {ex.Message}");
                return false;
            }
        }

        // Only once the apply is durable. Never throws: by then the apply has happened and must not be undone.
        public static void Commit(string root, IList<string> written)
        {
            try
            {
                Record journal = Read(JournalPath(root));
                if (journal == null) return;
                Record original = Read(OriginalPath(root));
                // RimWorld re-sorts a list and renames "_steam" copies on restart, so a list KMH wrote is known by its
                // mods, not their order. Only a different set of mods is the player's own; the older copy is archived.
                bool kmhList = original != null && IsKmhWritten(Convert.FromBase64String(journal.Bytes), original.Written);
                if (!kmhList)
                {
                    if (original != null) Archive(root, original, "superseded");
                    original = new Record { Bytes = journal.Bytes, SavedUtc = DateTime.UtcNow.ToString("o"), Own = true };
                }
                original.Written = new List<string>(written ?? new List<string>());
                Write(OriginalPath(root), original);
                Discard(root);
            }
            catch (Exception ex) { KmhLog.Warn($"ModSync: could not record which mod list was your own: {ex.Message}"); }
        }

        // An apply that never reached its durable state is undone; one that did is finished.
        public static void Recover(string root, string committedGeneration)
        {
            Record journal = Read(JournalPath(root));
            if (journal == null) return;
            if (journal.Generation.Length > 0 && journal.Generation == committedGeneration)
            {
                ModSyncApply.ActiveModsOf(ReadText(ModSyncApply.ConfigPath), out List<string> onDisk, out _);
                Commit(root, onDisk);
                return;
            }
            if (!RollBack(root)) throw new IOException("the mod list an interrupted apply replaced could not be put back");
            KmhLog.Warn("ModSync: put back the mod list an interrupted apply had replaced.");
        }

        public static bool RestoreOriginal(string root, out string why) => RestoreOriginal(root, out why, out _);

        // A player who has changed the list since KMH wrote it keeps their change; the saved copy is archived, not lost.
        public static bool RestoreOriginal(string root, out string why, out RestoreOutcome outcome)
        {
            why = "";
            outcome = RestoreOutcome.Nothing;
            try
            {
                Record original = Read(OriginalPath(root));
                if (original == null) return true;
                if (IsServerShaped(original))
                {
                    Archive(root, original, "server-list-kept-as-yours");
                    File.Delete(OriginalPath(root));
                    outcome = RestoreOutcome.SavedCopyWasAServerList;
                    return true;
                }
                string path = ModSyncApply.ConfigPath;
                if (!File.Exists(path) || !IsKmhWritten(File.ReadAllBytes(path), original.Written))
                {
                    Archive(root, original, "kept-your-change");
                    File.Delete(OriginalPath(root));
                    outcome = RestoreOutcome.KeptYourChange;
                    return true;
                }
                // Same mods, but the order may be the player's own rearrangement, so it is kept before being replaced.
                Archive(root, new Record { Bytes = Convert.ToBase64String(File.ReadAllBytes(path)), Written = original.Written }, "before-restore");
                if (!PutBack(Convert.FromBase64String(original.Bytes)))
                {
                    why = "the restored mod list did not read back";
                    return false;
                }
                File.Delete(OriginalPath(root));
                outcome = RestoreOutcome.Restored;
                return true;
            }
            catch (Exception ex) { why = ex.Message; return false; }
        }

        private static bool IsKmhWritten(byte[] bytes, List<string> written)
            => written != null && written.Count > 0
            && ModSyncApply.ActiveModsOf(Decode(bytes), out List<string> ids, out string error) && error == null
            && SameMods(ids, written);

        // Older builds compared order and so saved a list they had written themselves as the player's. A record this
        // build marked as the player's is never second-guessed: a server that only reorders leaves the same mods.
        private static bool IsServerShaped(Record r)
        {
            if (r.Own) return false;
            try { return IsKmhWritten(Convert.FromBase64String(r.Bytes), r.Written); }
            catch { return false; }
        }

        // The same mods whatever their order, with RimWorld's "_steam" twin counting as the mod itself.
        internal static bool SameMods(IList<string> a, IList<string> b)
        {
            if (a == null || b == null) return false;
            HashSet<string> x = Keys(a), y = Keys(b);
            return x.SetEquals(y);
        }

        private static HashSet<string> Keys(IList<string> ids)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string id in ids)
                if (!string.IsNullOrWhiteSpace(id)) set.Add(Mods.KmhModPlan.Bare(id.Trim()));
            return set;
        }

        internal sealed class Candidate
        {
            public string Source = "";
            public DateTime WhenUtc;
            public byte[] Bytes;
            public List<string> Mods = new List<string>();
        }

        // Every list that could be the player's own, newest first: Colony Nexus's archive, ModSync's copy from before
        // its last write and RimWorld's own backup. Lists matching the current one, or with no Core, are left out.
        internal static List<Candidate> RecoverableLists(string root)
        {
            var found = new List<Candidate>();
            string current = ReadText(ModSyncApply.ConfigPath);
            ModSyncApply.ActiveModsOf(current, out List<string> now, out _);
            // Every list a server is known to have written: a backup holding the same mods is that server's list,
            // re-sorted, and is never offered under the player's name.
            var servers = new List<List<string>>();
            void Server(Record r) { if (r?.Written != null && r.Written.Count > 0) servers.Add(r.Written); }
            // A list known to be the player's is offered even when only its order differs from now; any other list
            // with the same mods as now would be noise, since RimWorld re-sorts lists on every restart.
            void Consider(string source, DateTime when, byte[] bytes, bool own)
            {
                if (bytes == null || bytes.Length == 0) return;
                if (!ModSyncApply.ActiveModsOf(Decode(bytes), out List<string> ids, out string error) || error != null) return;
                if (!Keys(ids).Contains("ludeon.rimworld")) return;
                if (own ? ModSyncApply.SameSequence(ids, now) : SameMods(ids, now)) return;
                if (!own && servers.Exists(s => SameMods(ids, s))) return;
                foreach (Candidate c in found) if (ModSyncApply.SameSequence(c.Mods, ids)) return;
                found.Add(new Candidate { Source = source, WhenUtc = when, Bytes = bytes, Mods = ids });
            }
            try
            {
                Record original = Read(OriginalPath(root));
                Server(original);
                var archived = new List<(string File, Record Record)>();
                if (Directory.Exists(ArchiveDir(root)))
                    foreach (string f in Directory.GetFiles(ArchiveDir(root), "*.json")) { Record r = Read(f); Server(r); archived.Add((f, r)); }
                if (original != null && !IsServerShaped(original))
                    Consider("saved", Parse(original.SavedUtc, File.GetLastWriteTimeUtc(OriginalPath(root))), Convert.FromBase64String(original.Bytes), original.Own);
                // Newest first, by the timestamp each archive name starts with.
                archived.Sort((a, b) => string.CompareOrdinal(b.File, a.File));
                foreach ((string f, Record r) in archived)
                {
                    if (r == null || IsServerShaped(r)) continue;
                    Consider("archive", Parse(r.SavedUtc, File.GetLastWriteTimeUtc(f)), Convert.FromBase64String(r.Bytes), r.Own);
                }
                string config = Path.GetDirectoryName(ModSyncApply.ConfigPath) ?? "";
                foreach ((string name, string source) in new[] { (Path.GetFileName(ModSyncApply.BackupPathFor(ModSyncApply.ConfigPath)), "modsync"),
                                                                 ("backup_ModsConfig.xml", "rimworld") })
                {
                    string p = Path.Combine(config, name);
                    if (File.Exists(p)) Consider(source, File.GetLastWriteTimeUtc(p), File.ReadAllBytes(p), own: false);
                }
            }
            catch (Exception ex) { KmhLog.Warn($"ModSync: could not list the mod lists that can be recovered: {ex.Message}"); }
            found.Sort((a, b) => b.WhenUtc.CompareTo(a.WhenUtc));
            return found;
        }

        // The list being replaced is archived first, so a recovery the player regrets can be undone the same way.
        internal static bool RecoverList(string root, Candidate c, out string why)
        {
            why = "";
            try
            {
                if (c?.Bytes == null || !ModSyncApply.ActiveModsOf(Decode(c.Bytes), out List<string> ids, out string error)
                    || error != null || !Keys(ids).Contains("ludeon.rimworld"))
                { why = "that list could not be read"; return false; }
                string path = ModSyncApply.ConfigPath;
                if (File.Exists(path)) Archive(root, new Record { Bytes = Convert.ToBase64String(File.ReadAllBytes(path)) }, "before-recovery");
                if (!PutBack(c.Bytes)) { why = "the recovered mod list did not read back"; return false; }
                File.Delete(OriginalPath(root));
                return true;
            }
            catch (Exception ex) { why = ex.Message; return false; }
        }

        // Boot only: a saved copy older builds filled with a server's list is moved aside and reported once.
        internal static bool QuarantineDamagedOriginal(string root)
        {
            try
            {
                Record original = Read(OriginalPath(root));
                if (original == null || !IsServerShaped(original)) return false;
                Archive(root, original, "server-list-kept-as-yours");
                File.Delete(OriginalPath(root));
                KmhLog.Warn("ModSync: the saved copy of your own mod list was a server's list (an older Colony Nexus build "
                          + "replaced it); it was set aside, and older lists that can be recovered are offered instead.");
                return true;
            }
            catch (Exception ex) { KmhLog.Warn($"ModSync: could not check the saved copy of your mod list: {ex.Message}"); return false; }
        }

        private static void Archive(string root, Record r, string reason)
        {
            string dir = ArchiveDir(root);
            Directory.CreateDirectory(dir);
            var copy = new Record { Bytes = r.Bytes, Written = r.Written ?? new List<string>(), Reason = reason, Own = r.Own,
                                    SavedUtc = string.IsNullOrEmpty(r.SavedUtc) ? DateTime.UtcNow.ToString("o") : r.SavedUtc };
            string name = DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff") + "-" + reason + ".json";
            Write(Path.Combine(dir, name), copy);
            var files = new List<string>(Directory.GetFiles(dir, "*.json"));
            files.Sort(StringComparer.Ordinal);
            for (int i = 0; i < files.Count - ArchiveKept; i++)
                try { File.Delete(files[i]); } catch (Exception ex) { KmhLog.Debug($"ModSync: could not prune '{files[i]}': {ex.Message}"); }
        }

        private static DateTime Parse(string iso, DateTime fallback)
            => DateTime.TryParse(iso, System.Globalization.CultureInfo.InvariantCulture,
                                 System.Globalization.DateTimeStyles.RoundtripKind, out DateTime t) ? t.ToUniversalTime() : fallback;

        private static bool PutBack(byte[] bytes)
        {
            Persistence.KmhAtomicFile.WriteBytes(ModSyncApply.ConfigPath, bytes);
            byte[] now = File.ReadAllBytes(ModSyncApply.ConfigPath);
            if (now.Length != bytes.Length) return false;
            for (int i = 0; i < now.Length; i++) if (now[i] != bytes[i]) return false;
            return true;
        }

        private static void Write(string path, Record record)
        {
            string json = JsonConvert.SerializeObject(record, Formatting.Indented);
            Persistence.KmhAtomicFile.Write(path, json);
            if (File.ReadAllText(path) != json) throw new IOException("the mod list journal did not read back");
        }

        // Unreadable is treated as absent; both files are only ever written whole.
        private static Record Read(string path)
        {
            if (!File.Exists(path)) return null;
            try
            {
                Record r = JsonConvert.DeserializeObject<Record>(File.ReadAllText(path));
                if (r == null || string.IsNullOrEmpty(r.Bytes)) return null;
                if (r.Generation == null) r.Generation = "";
                return r;
            }
            catch (Exception ex)
            {
                KmhLog.Warn($"ModSync: ignoring an unreadable '{Path.GetFileName(path)}': {ex.Message}");
                return null;
            }
        }

        // The same decoding File.ReadAllText applies, so a byte-order mark reads the way RimWorld sees it.
        private static string Decode(byte[] bytes)
        {
            using (StreamReader reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, true))
                return reader.ReadToEnd();
        }

        private static string ReadText(string path) => File.Exists(path) ? File.ReadAllText(path) : "";
    }
}
