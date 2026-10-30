using System;
using System.Collections.Generic;
using System.IO;
using KMHPatch.Diagnostics;
using Newtonsoft.Json;

namespace KMHPatch.Features.Global
{
    // Presentation metadata only: never tickets, tokens or anything that is a credential if the file is copied.
    internal static class KmhDirectoryCache
    {
        private const int MaxCachedCards = 200;

        internal static string RootOverrideForTest;

        private static string Path_
        {
            get
            {
                string root = RootOverrideForTest ?? Verse.GenFilePaths.ConfigFolderPath;
                return System.IO.Path.Combine(root, "KMH_DirectoryCache.json");
            }
        }

        private sealed class Wrapper
        {
            public int SchemaVersion = 1;
            public string SavedUtc = "";
            // Which directory these rows came from: another directory's rows are never shown as this one's.
            public string Directory = "";
            public List<ServerCardDto> Cards = new List<ServerCardDto>();
        }

        internal static Action DuringSaveForTest;

        // Atomic replace: a write cut short left a file the next offline start could not read.
        public static void Save(List<ServerCardDto> cards)
        {
            try
            {
                Wrapper w = new Wrapper { SavedUtc = DateTime.UtcNow.ToString("o"), Directory = GlobalHttp.BaseUrl };
                int n = Math.Min(cards?.Count ?? 0, MaxCachedCards);
                for (int i = 0; i < n; i++) w.Cards.Add(cards[i]);
                Persistence.KmhAtomicFile.Write(Path_, JsonConvert.SerializeObject(w, Formatting.Indented),
                    new Persistence.KmhAtomicFile.Options { Durable = false, DuringWrite = DuringSaveForTest });
            }
            catch (Exception ex) { KmhLog.Debug($"Global: could not write the directory cache: {ex.Message}"); }
        }

        // One directory's rows: after a switch they must not load back as the new directory's "saved results".
        public static void Clear()
        {
            try { if (File.Exists(Path_)) File.Delete(Path_); }
            catch (Exception ex) { KmhLog.Debug($"Global: could not clear the directory cache: {ex.Message}"); }
        }

        public static List<ServerCardDto> Load(out DateTime savedUtc)
        {
            savedUtc = DateTime.MinValue;
            try
            {
                if (!File.Exists(Path_)) return null;
                Wrapper w = JsonConvert.DeserializeObject<Wrapper>(File.ReadAllText(Path_));
                if (w?.Cards == null) return null;
                if (!string.Equals(w.Directory, GlobalHttp.BaseUrl, StringComparison.OrdinalIgnoreCase))
                {
                    KmhLog.Debug("Global: the saved server list came from another directory; not shown.");
                    return null;
                }
                DateTime.TryParse(w.SavedUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out savedUtc);
                // Re-inerted, not trusted: the file is editable on disk and its cards reach Widgets.Label like fresh ones.
                foreach (ServerCardDto c in w.Cards) c.MakeInert();
                return w.Cards;
            }
            catch (Exception ex)
            {
                KmhLog.Debug($"Global: could not read the directory cache: {ex.Message}");
                return null;
            }
        }
    }
}
