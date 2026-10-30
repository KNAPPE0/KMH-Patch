using System;
using System.Collections.Generic;
using KMHPatch.Features.Sites.Dto;

namespace KMHPatch.Features.Sites
{
    // Must stay byte-identical to the server's SiteWorkerKey.
    internal static class SiteWorkerKey
    {
        internal const char Separator = '#';

        public static string For(string username, int pawnLoadId)
            => string.IsNullOrEmpty(username) ? ""
             : pawnLoadId > 0 ? username + Separator + pawnLoadId.ToString(System.Globalization.CultureInfo.InvariantCulture)
             : username;

        public static string AccountOf(string key)
        {
            if (string.IsNullOrEmpty(key)) return "";
            int at = key.IndexOf(Separator);
            return at < 0 ? key : key.Substring(0, at);
        }

        public static bool IsOwnedBy(string key, string username)
            => !string.IsNullOrEmpty(username)
            && string.Equals(AccountOf(key), username, StringComparison.OrdinalIgnoreCase);

        public static bool IsPerPawn(string key) => !string.IsNullOrEmpty(key) && key.IndexOf(Separator) >= 0;

        // Join order, so the UI lists a player's colonists consistently.
        public static List<KeyValuePair<string, WorkerProgressDto>> Mine(SiteEntry s, string username)
        {
            var outp = new List<KeyValuePair<string, WorkerProgressDto>>();
            if (s?.WorkerProgress == null || string.IsNullOrEmpty(username)) return outp;
            foreach (KeyValuePair<string, WorkerProgressDto> kv in s.WorkerProgress)
                if (kv.Value != null && IsOwnedBy(kv.Key, username)) outp.Add(kv);
            outp.Sort((a, b) => a.Value.JoinedUtcTicks.CompareTo(b.Value.JoinedUtcTicks));
            return outp;
        }

        // Falls back to the player's single row, which is what a not-yet-re-keyed legacy worker looks like.
        public static WorkerProgressDto For(SiteEntry s, string username, int pawnLoadId)
        {
            if (s?.WorkerProgress == null) return null;
            if (pawnLoadId > 0 && s.WorkerProgress.TryGetValue(For(username, pawnLoadId), out WorkerProgressDto exact) && exact != null)
                return exact;
            foreach (KeyValuePair<string, WorkerProgressDto> kv in Mine(s, username))
                if (pawnLoadId <= 0 || kv.Value.PawnLoadId == pawnLoadId || kv.Value.PawnLoadId <= 0) return kv.Value;
            return null;
        }

        public static WorkerProgressDto Any(SiteEntry s, string username)
        {
            List<KeyValuePair<string, WorkerProgressDto>> mine = Mine(s, username);
            return mine.Count == 0 ? null : mine[0].Value;
        }

        public static bool Works(SiteEntry s, string username)
        {
            if (s?.Workers == null || string.IsNullOrEmpty(username)) return false;
            foreach (string w in s.Workers) if (IsOwnedBy(w, username)) return true;
            return false;
        }
    }
}
