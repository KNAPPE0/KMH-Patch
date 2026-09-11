using System;
using System.Collections.Generic;
using KMHPatch.Diagnostics;
using KMHPatch.Features.Catalog.Dto;
using KMHPatch.SubProtocol;
using KMHPatch.UI;
using RimWorld;
using Verse;

namespace KMHPatch.Features.Catalog
{
    // Raw facts only: a family, a skill and a cost band are consequences the server decides, and nothing here computes one.
    internal static class SiteMetadataSender
    {
        // Smaller than the label chunk, since each entry carries several string lists.
        private const int ChunkSize = 250;

        // Sent and applied are different facts: one latch for both left a refused push unretried and the server catalog-less all session.
        private static string _sentFingerprint  = "";
        private static bool   _serverConfirmed;
        private static bool   _serverRefused;
        private static int    _retries;
        // A push is thousands of defs and a server still reporting none will refuse the next copy too; the next reconnect starts over.
        internal const int MaxRetries = 3;

        // Shared with every other catalog pusher, so one of them cannot quietly lose the re-push throttle.
        internal const string GuardKind = "site-metadata";

        // Nothing sent yet. Once something is on the wire the answer decides, not another send.
        internal static bool NeedsPush => string.IsNullOrEmpty(_sentFingerprint);
        internal static bool Confirmed => _serverConfirmed;
        internal static bool Refused   => _serverRefused;

        internal static void ResetForServerSwitch()
        {
            _sentFingerprint = ""; _serverConfirmed = false; _serverRefused = false; _retries = 0;
        }

        // Building a real push needs DefDatabase, which the offline suite has no game for.
        internal static void MarkSentForTest(string fingerprint)
        {
            _sentFingerprint = fingerprint; _serverConfirmed = false; _serverRefused = false;
        }

        // Called for every catalog snapshot, so a push that never landed is repaired in-session rather than at the next reconnect.
        internal static void OnServerCatalog(string serverFingerprint)
        {
            if (string.IsNullOrEmpty(_sentFingerprint)) return;

            if (string.Equals(serverFingerprint, _sentFingerprint, StringComparison.Ordinal))
            { _serverConfirmed = true; _serverRefused = false; return; }

            if (string.IsNullOrEmpty(serverFingerprint))
            {
                // The server holds no catalog at all, so this push did not arrive. Retrying is the whole point.
                _serverConfirmed = false; _serverRefused = false;
                if (_retries >= MaxRetries)
                { KmhLog.Debug("SiteMeta: the server still reports no catalog after retrying - leaving it."); return; }
                _retries++;
                // The server saying it holds nothing IS the evidence that the throttle must not speak for this one.
                CatalogPushGuard.Forget(GuardKind);
                KmhLog.Debug($"SiteMeta: the server reports no catalog - the push did not land, retry {_retries}.");
                // Network thread, and the rebuild reads DefDatabase; nothing here may throw back into the handler.
                try
                {
                    KmhMainThread.Post(() =>
                    {
                        try { PushOnce(); }
                        catch (Exception ex) { KmhLog.Warn($"SiteMeta: retry threw: {ex.Message}"); }
                    });
                }
                catch (Exception ex) { KmhLog.Warn($"SiteMeta: could not schedule a retry: {ex.Message}"); }
                return;
            }

            // A different established catalog is a deliberate refusal; re-sending the same thing would only be refused again.
            _serverConfirmed = false; _serverRefused = true;
            KmhLog.Debug($"SiteMeta: the server uses catalog {serverFingerprint}, not this client's {_sentFingerprint}.");
        }

        public static bool PushOnce()
        {
            if (!KmhDispatcher.IsKmhServer) return false;
            if (!KmhCapabilities.Has(KmhCapabilities.SiteMeta))
            {
                KmhLog.Debug("SiteMeta: server does not advertise site metadata - skipping push.");
                return true;   // not a failure: an older server classifies the old way and works fine
            }

            // Measured at ~137KB over the RWT chat channel, so a reconnect within the window must not stream it again.
            string endpoint = CatalogPushGuard.CurrentEndpoint();
            if (CatalogPushGuard.AlreadySent(GuardKind, endpoint, out int agoSeconds))
            {
                KmhLog.Debug($"SiteMeta: metadata already sent to {endpoint} {agoSeconds}s ago - skipping re-push.");
                return true;
            }

            List<Features.Sites.Dto.SiteOutputMetadata> all = Build();
            if (all.Count == 0)
            {
                KmhLog.Warn("SiteMeta: no item defs resolved, skipping push.");
                return false;
            }

            string fingerprint = Fingerprint(all);
            int total = (all.Count + ChunkSize - 1) / ChunkSize;
            int sent = 0;
            for (int c = 0; c < total; c++)
            {
                SiteMetadataPush push = new SiteMetadataPush
                {
                    ChunkIndex = c + 1, ChunkTotal = total, Fingerprint = fingerprint,
                };
                for (int i = c * ChunkSize; i < all.Count && i < (c + 1) * ChunkSize; i++)
                    push.Meta.Add(all[i]);

                if (!KmhDispatcher.Send(KmhProtocol.Kind.SiteMetaPush, push)) break;
                sent++;
            }

            // Recorded as SENT, not as accepted - OnServerCatalog decides that when the server says what it holds.
            _sentFingerprint = fingerprint;
            _serverConfirmed = false;
            _serverRefused   = false;
            if (sent == total) CatalogPushGuard.MarkSent(GuardKind, endpoint);   // a partial push must be sent again
            KmhLog.Debug($"SiteMeta: pushed {all.Count} def(s) in {sent}/{total} chunk(s), fingerprint {fingerprint}.");
            return sent == total;
        }

        // Lets the server tell "the same modpack, said twice" from "a second client claiming different facts".
        internal static string Fingerprint(List<Features.Sites.Dto.SiteOutputMetadata> all)
        {
            if (all == null || all.Count == 0) return "";
            List<string> names = new List<string>(all.Count);
            foreach (Features.Sites.Dto.SiteOutputMetadata m in all) if (m != null) names.Add(m.DefName ?? "");
            names.Sort(StringComparer.Ordinal);

            // Not a security hash: it identifies a catalog, and two clients agreeing is what the server treats as evidence.
            ulong h = 14695981039346656037UL;
            foreach (string n in names)
                foreach (char ch in n) { h ^= ch; h *= 1099511628211UL; }
            return names.Count.ToString() + "-" + h.ToString("x16");
        }

        // Built once: answering "what points at this def" per item would walk the whole DefDatabase, which is quadratic.
        private sealed class Reverse
        {
            public readonly HashSet<ThingDef> AnimalProducts = new HashSet<ThingDef>();
            public readonly HashSet<ThingDef> Harvested      = new HashSet<ThingDef>();
            public readonly HashSet<ThingDef> TreeHarvested  = new HashSet<ThingDef>();
            public readonly HashSet<ThingDef> WildHarvested  = new HashSet<ThingDef>();
            public readonly HashSet<ThingDef> Mineable       = new HashSet<ThingDef>();
            public readonly HashSet<ThingDef> Crafted        = new HashSet<ThingDef>();
        }

        private static Reverse BuildReverse()
        {
            var rev = new Reverse();
            try
            {
                foreach (ThingDef d in DefDatabase<ThingDef>.AllDefsListForReading)
                {
                    if (d == null) continue;

                    // race.Animal, not race != null: a mechanoid has a race too, and butchers into plasteel and steel.
                    if (d.race != null && d.race.Animal)
                    {
                        if (d.butcherProducts != null)
                            foreach (ThingDefCountClass c in d.butcherProducts)
                                if (c?.thingDef != null) rev.AnimalProducts.Add(c.thingDef);
                        if (d.race.meatDef != null)    rev.AnimalProducts.Add(d.race.meatDef);
                        if (d.race.leatherDef != null) rev.AnimalProducts.Add(d.race.leatherDef);
                        if (d.comps != null)
                            foreach (CompProperties cp in d.comps)
                            {
                                if (cp is CompProperties_Milkable milk && milk.milkDef != null) rev.AnimalProducts.Add(milk.milkDef);
                                if (cp is CompProperties_Shearable shear && shear.woolDef != null) rev.AnimalProducts.Add(shear.woolDef);
                                if (cp is CompProperties_EggLayer egg)
                                {
                                    if (egg.eggUnfertilizedDef != null) rev.AnimalProducts.Add(egg.eggUnfertilizedDef);
                                    if (egg.eggFertilizedDef != null)   rev.AnimalProducts.Add(egg.eggFertilizedDef);
                                }
                            }
                    }

                    if (d.plant?.harvestedThingDef != null)
                    {
                        rev.Harvested.Add(d.plant.harvestedThingDef);
                        if (IsTree(d.plant)) rev.TreeHarvested.Add(d.plant.harvestedThingDef);
                        // No sow tag means no colonist can plant it, so it is gathered where it grows.
                        if (d.plant.sowTags == null || d.plant.sowTags.Count == 0)
                            rev.WildHarvested.Add(d.plant.harvestedThingDef);
                    }
                    if (d.building?.mineableThing != null)  rev.Mineable.Add(d.building.mineableThing);
                }

                foreach (RecipeDef r in DefDatabase<RecipeDef>.AllDefsListForReading)
                {
                    if (r?.products == null) continue;
                    foreach (ThingDefCountClass p in r.products)
                        if (p?.thingDef != null) rev.Crafted.Add(p.thingDef);
                }
            }
            catch (Exception ex) { KmhLog.Warn($"SiteMeta: reverse index build threw: {ex.Message}"); }
            return rev;
        }

        // treeCategory is the modern signal and harvestTag the old one; mods still ship either, so both count.
        private static bool IsTree(PlantProperties p)
        {
            if (p == null) return false;
            try
            {
                if (p.treeCategory != TreeCategory.None) return true;
            }
            catch { }
            return string.Equals(p.harvestTag, "Wood", StringComparison.OrdinalIgnoreCase);
        }

        private static List<Features.Sites.Dto.SiteOutputMetadata> Build()
        {
            var result = new List<Features.Sites.Dto.SiteOutputMetadata>();
            try
            {
                Reverse rev = BuildReverse();
                Dictionary<string, int> pickable = ItemDefBrowser.AllPickableItems();
                foreach (string defName in pickable.Keys)
                {
                    if (string.IsNullOrEmpty(defName)) continue;
                    ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
                    if (def == null) continue;
                    result.Add(Describe(def, rev));
                }
            }
            catch (Exception ex) { KmhLog.Warn($"SiteMeta: build threw: {ex.Message}"); }
            return result;
        }

        // One def -> raw facts. Every field is something RimWorld already asserts; nothing is inferred here.
        private static Features.Sites.Dto.SiteOutputMetadata Describe(ThingDef def, Reverse rev)
        {
            var m = new Features.Sites.Dto.SiteOutputMetadata
            {
                DefName     = def.defName ?? "",
                Label       = string.IsNullOrEmpty(def.label) ? (def.defName ?? "") : def.label,
                MarketValue = SafeValue(def),
            };

            // Parents included, most specific first: a modded category still descends from a vanilla one.
            try
            {
                if (def.thingCategories != null)
                    foreach (ThingCategoryDef c in def.thingCategories)
                        for (ThingCategoryDef cur = c; cur != null; cur = cur.parent)
                            if (!string.IsNullOrEmpty(cur.defName) && !m.Categories.Contains(cur.defName))
                                m.Categories.Add(cur.defName);
            }
            catch { }

            try
            {
                if (def.stuffProps?.categories != null)
                    foreach (StuffCategoryDef sc in def.stuffProps.categories)
                        if (sc != null && !string.IsNullOrEmpty(sc.defName)) m.StuffCategories.Add(sc.defName);
            }
            catch { }

            try
            {
                if (def.tradeTags != null)         foreach (string t in def.tradeTags)         if (!string.IsNullOrEmpty(t)) m.Tags.Add(t);
                if (def.thingSetMakerTags != null) foreach (string t in def.thingSetMakerTags) if (!string.IsNullOrEmpty(t) && !m.Tags.Contains(t)) m.Tags.Add(t);
            }
            catch { }

            try
            {
                m.IsIngestible = def.IsIngestible;
                if (def.ingestible != null) m.FoodType = def.ingestible.foodType.ToString();
            }
            catch { }

            m.IsAnimalProduct      = rev.AnimalProducts.Contains(def);
            m.IsHarvestedFromPlant = rev.Harvested.Contains(def);
            m.IsTreeHarvest        = rev.TreeHarvested.Contains(def);
            m.IsWildHarvest        = rev.WildHarvested.Contains(def);
            m.IsMineable           = rev.Mineable.Contains(def);
            m.IsCraftedProduct     = rev.Crafted.Contains(def);
            return m;
        }

        private static float SafeValue(ThingDef def)
        {
            try { return def.BaseMarketValue; } catch { return 0f; }
        }
    }
}
