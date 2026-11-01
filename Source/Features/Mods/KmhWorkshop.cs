using System;
using System.Collections.Generic;
using System.Reflection;
using KMHPatch.Diagnostics;

namespace KMHPatch.Features.Mods
{
    internal enum WorkshopItemState
    {
        // Steam is not running, or this build exposes no SteamUGC - Subscribe cannot be offered at all.
        Unavailable,
        NotSubscribed,
        Queued,
        Downloading,
        // On disk, but Steam holds a newer version it has not downloaded: playing on the old one can fail the join.
        NeedsUpdate,
        // Subscribed and on disk. RimWorld only picks a new mod up on restart, so this is still not "ready to play".
        Installed,
    }

    internal static class KmhWorkshop
    {
        // EItemState, from Steamworks. Mirrored rather than reflected: they are protocol constants, not members.
        private const uint Subscribed = 1, Installed = 4, NeedsUpdate = 8, Downloading = 16, DownloadPending = 32;

        public static bool Available => UgcType != null && Account.KmhSteamPersona.SteamIsRunning;

        private static Type _ugc;
        private static bool _ugcLooked;

        private static Type UgcType
        {
            get
            {
                if (_ugcLooked) return _ugc;
                _ugcLooked = true;
                _ugc = Account.KmhSteamInterop.Find("Steamworks.SteamUGC");
                if (_ugc == null) KmhLog.Debug("Workshop: this build exposes no SteamUGC; Subscribe is not offered.");
                return _ugc;
            }
        }

        public static bool IsWorkshopId(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length < 6 || id.Length > 20) return false;
            foreach (char c in id) if (c < '0' || c > '9') return false;
            return true;
        }

        // Steam's own answer, not a cached guess: a player who subscribes outside the game must not still be told to.
        public static WorkshopItemState StateOf(string id)
        {
            if (!Available || !IsWorkshopId(id)) return WorkshopItemState.Unavailable;
            try
            {
                object handle = FileId(id);
                if (handle == null) return WorkshopItemState.Unavailable;

                MethodInfo m = UgcType.GetMethod("GetItemState", BindingFlags.Public | BindingFlags.Static);
                if (m == null) return WorkshopItemState.Unavailable;

                return FromFlags(Convert.ToUInt32(m.Invoke(null, new[] { handle })));
            }
            catch (Exception ex)
            {
                KmhLog.Debug($"Workshop: reading the state of {id} failed ({ex.GetType().Name}).");
                return WorkshopItemState.Unavailable;
            }
        }

        internal static WorkshopItemState FromFlags(uint flags)
        {
            if ((flags & (Downloading | DownloadPending)) != 0) return WorkshopItemState.Downloading;
            // Installed|NeedsUpdate is an old copy on disk; reading it as Installed would tell a player they were ready.
            if ((flags & Installed) != 0) return (flags & NeedsUpdate) != 0 ? WorkshopItemState.NeedsUpdate : WorkshopItemState.Installed;
            // Subscribed without Installed is Steam having accepted the subscription and not yet fetched it.
            if ((flags & Subscribed) != 0) return WorkshopItemState.Queued;
            return WorkshopItemState.NotSubscribed;
        }

        public static bool RequestDownload(string id)
        {
            if (!Available || !IsWorkshopId(id)) return false;
            try
            {
                object handle = FileId(id);
                MethodInfo dl = handle == null ? null : UgcType.GetMethod("DownloadItem", BindingFlags.Public | BindingFlags.Static);
                if (dl == null) return false;
                return Convert.ToBoolean(dl.Invoke(null, new object[] { handle, true }));
            }
            catch (Exception ex)
            {
                KmhLog.Warn($"Workshop: asking Steam to download {id} failed: {ex.Message}");
                return false;
            }
        }

        public static bool DownloadInfo(string id, out ulong done, out ulong total)
        {
            done = 0; total = 0;
            if (!Available || !IsWorkshopId(id)) return false;
            try
            {
                object handle = FileId(id);
                MethodInfo m = handle == null ? null : UgcType.GetMethod("GetItemDownloadInfo", BindingFlags.Public | BindingFlags.Static);
                if (m == null) return false;
                object[] args = { handle, 0UL, 0UL };
                if (!Convert.ToBoolean(m.Invoke(null, args))) return false;
                done = Convert.ToUInt64(args[1]);
                total = Convert.ToUInt64(args[2]);
                return true;
            }
            catch (Exception ex)
            {
                KmhLog.Debug($"Workshop: reading download progress of {id} failed ({ex.GetType().Name}).");
                return false;
            }
        }

        public static bool Subscribe(string id)
        {
            if (!Available || !IsWorkshopId(id)) return false;
            try
            {
                object handle = FileId(id);
                if (handle == null) return false;

                MethodInfo sub = UgcType.GetMethod("SubscribeItem", BindingFlags.Public | BindingFlags.Static);
                if (sub == null) { KmhLog.Warn("Workshop: this Steamworks build has no SubscribeItem."); return false; }
                sub.Invoke(null, new[] { handle });

                // Subscribing only queues the download; requesting it makes Steam start now, not at the next restart.
                MethodInfo dl = UgcType.GetMethod("DownloadItem", BindingFlags.Public | BindingFlags.Static);
                if (dl != null) dl.Invoke(null, new object[] { handle, true });

                KmhLog.Info($"Workshop: subscribed to {id}.");
                return true;
            }
            catch (Exception ex)
            {
                KmhLog.Warn($"Workshop: subscribing to {id} failed: {ex.Message}");
                return false;
            }
        }

        // RimWorld lists only subscribed items, so it drops this one at the next start; Steam removes the files later.
        public static bool Unsubscribe(string id)
        {
            if (!Available || !IsWorkshopId(id)) return false;
            try
            {
                object handle = FileId(id);
                MethodInfo unsub = handle == null ? null : UgcType.GetMethod("UnsubscribeItem", BindingFlags.Public | BindingFlags.Static);
                if (unsub == null) { KmhLog.Warn("Workshop: this Steamworks build has no UnsubscribeItem."); return false; }
                unsub.Invoke(null, new[] { handle });
                KmhLog.Info($"Workshop: unsubscribed from {id}.");
                return true;
            }
            catch (Exception ex)
            {
                KmhLog.Warn($"Workshop: unsubscribing from {id} failed: {ex.Message}");
                return false;
            }
        }

        public static int SubscribeAll(IEnumerable<string> ids)
        {
            int done = 0;
            if (ids == null) return 0;
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in ids)
                if (seen.Add(id) && Subscribe(id)) done++;
            return done;
        }

        private static Type _fileIdType;
        private static bool _fileIdLooked;

        private static object FileId(string id)
        {
            if (!_fileIdLooked)
            {
                _fileIdLooked = true;
                _fileIdType = Account.KmhSteamInterop.Find("Steamworks.PublishedFileId_t");
            }
            if (_fileIdType == null) return null;
            return ulong.TryParse(id, out ulong raw) ? Activator.CreateInstance(_fileIdType, raw) : null;
        }

        internal static void ResetForTest() { _ugc = null; _ugcLooked = false; _fileIdType = null; _fileIdLooked = false; }
    }
}
