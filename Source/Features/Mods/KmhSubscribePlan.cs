using System;
using System.Collections.Generic;
using KMHPatch.Features.Global;

namespace KMHPatch.Features.Mods
{
    // Recomputed once a second: neither the comparison walk nor Steam's item state belongs at 60Hz.
    internal sealed class KmhSubscribePlan
    {
        public List<ModEntryDto> ToSubscribe = new List<ModEntryDto>();
        // Never downloaded unless the player chooses them.
        public List<ModEntryDto> OptionalToSubscribe = new List<ModEntryDto>();
        public List<ModEntryDto> ToDownload = new List<ModEntryDto>();
        // Steam is fetching them now; a restart before they finish loads nothing new.
        public int Downloading;
        // On disk and not yet seen by RimWorld, which only scans its mods at start-up: a restart is what is left.
        public int OnDisk;
        // On disk and already known to RimWorld, just switched off: "Set my mod list" turns them on, not a restart.
        public int OnDiskDisabled;

        public bool Offers => ToSubscribe.Count > 0;
        public bool OffersOptional => OptionalToSubscribe.Count > 0;
        public bool OffersDownload => ToSubscribe.Count == 0 && ToDownload.Count > 0;
        public bool StillDownloading => ToSubscribe.Count == 0 && ToDownload.Count == 0 && Downloading > 0;
        public bool RestartIsWhatIsLeft => ToSubscribe.Count == 0 && ToDownload.Count == 0 && Downloading == 0 && OnDisk > 0;

        public List<string> Ids()
        {
            List<string> ids = new List<string>();
            foreach (ModEntryDto e in ToSubscribe) if (!ids.Contains(e.Id)) ids.Add(e.Id);
            return ids;
        }

        public List<string> Names()
        {
            List<string> names = new List<string>();
            foreach (ModEntryDto e in ToSubscribe) names.Add(e.Name);
            return names;
        }

        // The first name the server gave this id; one id listed twice under two names is subscribed once.
        public string NameFor(string id)
        {
            foreach (ModEntryDto e in ToSubscribe)
                if (e.Id == id && !string.IsNullOrEmpty(e.Name)) return e.Name;
            return "";
        }

        private static readonly TimeSpan Recheck = TimeSpan.FromSeconds(1);
        private static readonly object _lock = new object();
        private static string _forServer = "";
        private static long _forRevision = -1;
        private static DateTime _nextUtc = DateTime.MinValue;
        private static KmhSubscribePlan _cached = new KmhSubscribePlan();

        public static KmhSubscribePlan For(ServerCardDto card)
        {
            if (card == null || string.IsNullOrEmpty(card.ServerId)) return new KmhSubscribePlan();

            lock (_lock)
            {
                bool sameTarget = _forServer == card.ServerId && _forRevision == card.ManifestRevision;
                if (sameTarget && DateTime.UtcNow < _nextUtc) return _cached;

                _forServer = card.ServerId;
                _forRevision = card.ManifestRevision;
                _nextUtc = DateTime.UtcNow + Recheck;
                _cached = Build(card);
                return _cached;
            }
        }

        private static KmhSubscribePlan Build(ServerCardDto card)
        {
            KmhSubscribePlan plan = new KmhSubscribePlan();
            ModComparison c = ModProfileClient.Peek(card.ServerId)?.Comparison;
            if (c == null) return plan;

            List<InstalledMod> known = KmhLocalMods.InstalledCached();
            foreach (ModEntryDto e in c.Subscribable())
                Place(plan, e, KmhWorkshop.StateOf(e.Id), KnownToRimWorld(e, known));
            foreach (ModEntryDto e in c.OptionalSubscribable())
                if (KmhWorkshop.StateOf(e.Id) == WorkshopItemState.NotSubscribed) plan.OptionalToSubscribe.Add(e);
            return plan;
        }

        // Subscribed is not on disk, and on disk is not loaded, so Steam's and RimWorld's state are both needed.
        internal static void Place(KmhSubscribePlan plan, ModEntryDto e, WorkshopItemState steam, bool knownToRimWorld)
        {
            switch (steam)
            {
                case WorkshopItemState.Downloading: plan.Downloading++; break;
                case WorkshopItemState.Queued:
                case WorkshopItemState.NeedsUpdate: plan.ToDownload.Add(e); break;
                case WorkshopItemState.Installed:
                    if (knownToRimWorld) plan.OnDiskDisabled++; else plan.OnDisk++;
                    break;
                // Unavailable is Steam not answering; offering Subscribe is still the honest step, and it reports failure.
                default: plan.ToSubscribe.Add(e); break;
            }
        }

        // By packageId when the server named one; a different mod sharing the name is not this one.
        private static bool KnownToRimWorld(ModEntryDto e, List<InstalledMod> known)
        {
            foreach (InstalledMod m in known)
            {
                if (!string.IsNullOrEmpty(e.PackageId))
                { if (string.Equals(m.PackageId, e.PackageId, StringComparison.OrdinalIgnoreCase)) return true; }
                else if (string.Equals(m.Name, e.Name, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        public int RequestDownloads()
        {
            int n = 0;
            foreach (ModEntryDto e in ToDownload) if (KmhWorkshop.RequestDownload(e.Id)) n++;
            return n;
        }

        // Steam reports a subscription only after a moment, so the button must stop offering it right away.
        public static void Invalidate()
        {
            lock (_lock) { _nextUtc = DateTime.MinValue; _forRevision = -1; }
        }

        internal static void SeedForTest(ServerCardDto card, KmhSubscribePlan plan)
        {
            lock (_lock)
            {
                _forServer = card.ServerId; _forRevision = card.ManifestRevision;
                _nextUtc = DateTime.UtcNow + Recheck; _cached = plan;
            }
        }

        internal static void ResetForTest()
        {
            lock (_lock)
            {
                _forServer = ""; _forRevision = -1; _nextUtc = DateTime.MinValue;
                _cached = new KmhSubscribePlan();
            }
        }
    }
}
