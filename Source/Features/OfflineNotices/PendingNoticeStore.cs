using System;
using System.Collections.Generic;
using System.IO;
using KMHPatch.Diagnostics;
using KMHPatch.Features.OfflineNotices.Dto;
using Newtonsoft.Json;
using Verse;

namespace KMHPatch.Features.OfflineNotices
{
    // Content is stored before the ack, or a crash between acking and showing loses the notice for good.
    internal static class PendingNoticeStore
    {
        private sealed class State
        {
            [JsonProperty("shown")]   public List<string> Shown   = new List<string>();
            [JsonProperty("pending")] public List<NotificationDto> Pending = new List<NotificationDto>();
        }

        // Displayed notices keep only their ids, so the list stays small; content is kept only for the undisplayed.
        private const int MaxShownRemembered = 2000;

        private static readonly object _lock = new object();
        private static State _state;

        // Injectable because GenFilePaths reaches into Unity, which the offline self-test runner has no engine for.
        internal static string RootOverrideForTest;

        private static string Root => RootOverrideForTest
            ?? Persistence.KmhDataFolder.Root;

        private static string Path => System.IO.Path.Combine(Root, "pending-notices.json");

        private static void LoadLocked()
        {
            if (_state != null) return;
            _state = new State();
            try
            {
                if (File.Exists(Path))
                    _state = JsonConvert.DeserializeObject<State>(File.ReadAllText(Path)) ?? new State();
            }
            catch (Exception ex) { KmhLog.Warn($"Pending notices: could not read '{Path}' ({ex.Message}) - starting empty."); }
            _state.Shown   ??= new List<string>();
            _state.Pending ??= new List<NotificationDto>();
        }

        private static bool SaveLocked()
            => Persistence.KmhAtomicFile.TryWrite(
                Path, JsonConvert.SerializeObject(_state, Formatting.Indented), "Pending notices");

        // False means the write failed, so the caller must not acknowledge and the server keeps holding them.
        public static bool Accept(List<NotificationDto> incoming, out List<NotificationDto> fresh)
        {
            fresh = new List<NotificationDto>();
            if (incoming == null || incoming.Count == 0) return true;
            lock (_lock)
            {
                LoadLocked();
                var shown   = new HashSet<string>(_state.Shown, StringComparer.Ordinal);
                var pending = new HashSet<string>(StringComparer.Ordinal);
                foreach (NotificationDto p in _state.Pending) if (p?.Id != null) pending.Add(p.Id);

                foreach (NotificationDto n in incoming)
                {
                    if (n == null || string.IsNullOrEmpty(n.Id)) continue;
                    if (shown.Contains(n.Id) || pending.Contains(n.Id)) continue;   // a redelivery, not a new notice
                    _state.Pending.Add(n);
                    pending.Add(n.Id);
                    fresh.Add(n);
                }
                if (fresh.Count == 0) return true;      // nothing new to write down, so nothing can fail
                if (SaveLocked()) return true;
                // Rolled back so a later attempt still treats them as unseen.
                foreach (NotificationDto n in fresh) _state.Pending.RemoveAll(p => p?.Id == n.Id);
                fresh.Clear();
                return false;
            }
        }

        // Anything written down but never displayed - a crash between the ack and the letter lands here.
        public static List<NotificationDto> TakePending()
        {
            lock (_lock)
            {
                LoadLocked();
                var outList = new List<NotificationDto>(_state.Pending);
                return outList;
            }
        }

        public static void MarkShown(IEnumerable<NotificationDto> notices)
        {
            if (notices == null) return;
            lock (_lock)
            {
                LoadLocked();
                foreach (NotificationDto n in notices)
                {
                    if (n?.Id == null) continue;
                    _state.Pending.RemoveAll(p => p?.Id == n.Id);
                    if (!_state.Shown.Contains(n.Id)) _state.Shown.Add(n.Id);
                }
                if (_state.Shown.Count > MaxShownRemembered)
                    _state.Shown.RemoveRange(0, _state.Shown.Count - MaxShownRemembered);
                SaveLocked();
            }
        }

        internal static void ResetForTest()
        {
            lock (_lock) { _state = new State(); try { if (File.Exists(Path)) File.Delete(Path); } catch { } }
        }
    }
}
