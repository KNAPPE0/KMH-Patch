using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using KMHPatch.Diagnostics;
using KMHPatch.UI;
using RimWorld;
using Verse;

namespace KMHPatch.Items
{
    // A round-trip must never clean, repair, reroll or strip an item, so anything stateful deep-serializes.
    internal static class KmhThingCapture
    {
        // A def whose full state is exactly def+count: plain stackables with no quality/rot/biocode/comp identity.
        public static bool IsSimple(ThingDef d)
        {
            if (d == null || d.category != ThingCategory.Item) return false;
            if (d.stackLimit <= 1) return false;
            if (d.IsApparel || d.IsWeapon) return false;
            if (typeof(MinifiedThing).IsAssignableFrom(d.thingClass)) return false;
            if (d.HasComp(typeof(CompQuality)) || d.HasComp(typeof(CompRottable)) || d.HasComp(typeof(CompBiocodable))
                || d.HasComp(typeof(CompArt)) || d.HasComp(typeof(CompBladelinkWeapon))) return false;
            return true;
        }

        // Sets the Mergeable flag only; it never changes what is captured, so nothing is stripped.
        public static bool IsFungible(ThingDef d)
        {
            if (d == null || d.category != ThingCategory.Item) return false;
            if (d.stackLimit <= 1) return false;
            if (d.IsApparel || d.IsWeapon) return false;
            if (typeof(MinifiedThing).IsAssignableFrom(d.thingClass)) return false;
            if (d.HasComp(typeof(CompQuality)) || d.HasComp(typeof(CompBiocodable))
                || d.HasComp(typeof(CompArt)) || d.HasComp(typeof(CompBladelinkWeapon))) return false;
            return true;
        }

        // A far weaker claim than merging: RimWorld already holds these units in one stack, so they interchange.
        public static bool IsSplittableStack(Thing thing)
        {
            if (thing?.def == null) return false;
            if (thing.def.category != ThingCategory.Item) return false;
            if (thing.def.stackLimit <= 1) return false;
            if (typeof(MinifiedThing).IsAssignableFrom(thing.def.thingClass)) return false;
            return thing.stackCount > 1;
        }

        public static KmhThingPayload Capture(Thing thing)
        {
            if (thing == null) return null;
            // Returning null aborts the all-or-nothing caller, so an unsafe type keeps every item in the colony.
            if (!KmhItemSafety.CanKmhHandleThing(thing, KmhItemContext.Store, out string block))
            {
                KmhLog.Warn($"KMH: refused to capture '{thing.def?.defName}' - {block}");
                return null;
            }
            KmhThingPayload p = new KmhThingPayload
            {
                DefName      = thing.def?.defName ?? "",
                StuffDefName = thing.Stuff?.defName ?? "",
                StackCount   = Math.Max(1, thing.stackCount),
                Quality      = ItemKeys.QualityIndexOf(thing),
                Tainted      = (thing as Apparel)?.WornByCorpse ?? false,
                DisplayLabel = SafeLabel(thing),
            };
            try { if (thing.def != null && thing.def.useHitPoints) { p.HitPoints = thing.HitPoints; p.MaxHitPoints = thing.MaxHitPoints; } } catch { }
            try { p.MarketValue = (long)Math.Round(thing.MarketValue); } catch { }

            // Additive metadata only; rot rides along so the server can weight-average freshness on merge.
            try
            {
                p.Mergeable  = IsFungible(thing.def);
                p.Splittable = IsSplittableStack(thing);
                CompRottable rot = thing.TryGetComp<CompRottable>();
                if (rot != null) p.RotProgressTicks = (long)rot.RotProgress;
            }
            catch { }

            if (IsSimple(thing.def))
            {
                p.Fidelity = KmhThingPayload.FidelityFull;   // def+count fully represents it
            }
            else
            {
                string xml = KmhThingScribe.Save(thing);
                if (!string.IsNullOrEmpty(xml))
                {
                    p.ScribeXml = xml;
                    p.Fidelity  = KmhThingPayload.FidelityFull;
                }
                else
                {
                    p.Fidelity = KmhThingPayload.FidelityMetadata;
                    p.Warnings.Add("deep item state could not be captured; material/quality/HP/taint preserved, other data may be lost");
                }
            }
            p.Fingerprint = Fingerprint(p);
            return p;
        }

        // Null on total failure, so the caller falls back to the legacy path rather than losing the item.
        public static Thing Restore(KmhThingPayload p)
        {
            if (p == null) return null;

            if (!string.IsNullOrEmpty(p.ScribeXml))
            {
                Thing t = KmhThingScribe.Load(p.ScribeXml);
                if (t != null)
                {
                    if (p.StackCount > 1 && t.def != null && t.def.stackLimit >= p.StackCount) t.stackCount = p.StackCount;
                    // The payload's rot wins over the blob's: merged stacks share one representative blob.
                    if (p.Mergeable) ApplyAveragedWear(t, p);
                    return t;
                }
                KmhLog.Warn($"KMH: exact item restore failed for {p.DefName}; rebuilding from captured state (some detail may be lost).");
            }

            ThingDef def = ColonyGoods.Def(p.DefName);
            if (def == null) { KmhLog.Warn($"KMH: cannot restore unknown item '{p.DefName}'."); return null; }
            try
            {
                ThingDef stuff = string.IsNullOrEmpty(p.StuffDefName) ? null : DefDatabase<ThingDef>.GetNamedSilentFail(p.StuffDefName);
                Thing t = ThingMaker.MakeThing(def, def.MadeFromStuff ? (stuff ?? GenStuff.DefaultStuffFor(def)) : null);
                t.stackCount = Math.Max(1, Math.Min(p.StackCount, def.stackLimit));
                if (p.Quality > 0) ItemKeys.ApplyQuality(t, p.Quality);
                if (p.HitPoints > 0 && def.useHitPoints) t.HitPoints = Math.Min(p.HitPoints, t.MaxHitPoints);
                if (p.Tainted && t is Apparel ap) ap.WornByCorpse = true;
                return t;
            }
            catch (Exception ex) { KmhLog.Warn($"KMH: metadata restore of {p.DefName} threw: {ex.Message}"); return null; }
        }

        // In lockstep with the addon's GetStateFingerprint: a difference that must not merge has to change this.
        public static string Fingerprint(KmhThingPayload p)
        {
            if (p == null) return "";
            string basis = string.Join("|", new[]
            {
                p.DefName ?? "", p.StuffDefName ?? "", p.Quality.ToString(),
                p.HitPoints.ToString(), p.MaxHitPoints.ToString(), p.Tainted ? "1" : "0",
                Hash(p.ScribeXml),
            });
            return Hash(basis).Substring(0, 16);
        }

        private static string Hash(string s)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] h = sha.ComputeHash(Encoding.UTF8.GetBytes(s ?? ""));
                StringBuilder sb = new StringBuilder(h.Length * 2);
                foreach (byte b in h) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        // The server discards the extra blobs anyway, so folding here changes only the wire size.
        public static bool TryFoldFungible(List<KmhThingPayload> captured, KmhThingPayload p)
        {
            if (captured == null || p == null || !p.Mergeable) return false;
            foreach (KmhThingPayload e in captured)
            {
                if (!CanMergeFungible(e, p)) continue;
                MergeFungible(e, p);
                return true;
            }
            return false;
        }

        // Wear is deliberately not identity, since MergeFungible averages it; material and taint are.
        private static bool CanMergeFungible(KmhThingPayload a, KmhThingPayload b)
        {
            if (a == null || b == null || !a.Mergeable || !b.Mergeable) return false;
            return string.Equals(a.DefName, b.DefName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(a.StuffDefName ?? "", b.StuffDefName ?? "", StringComparison.OrdinalIgnoreCase)
                && a.Quality == b.Quality
                && a.Tainted == b.Tainted;
        }

        // Weight-averaged by count, so a merged stack is neither refreshed nor over-rotted.
        private static void MergeFungible(KmhThingPayload target, KmhThingPayload incoming)
        {
            if (target == null || incoming == null) return;
            long ct = Math.Max(1, target.StackCount);
            long ci = Math.Max(1, incoming.StackCount);
            target.HitPoints        = (int)WeightedAvg(target.HitPoints, ct, incoming.HitPoints, ci);
            target.RotProgressTicks = WeightedAvg(target.RotProgressTicks, ct, incoming.RotProgressTicks, ci);
            target.StackCount       = (int)Math.Min(int.MaxValue, ct + ci);
        }

        // A negative value means unknown and is skipped, so -1 comes back only when both are unknown.
        private static long WeightedAvg(long a, long ca, long b, long cb)
        {
            if (a < 0 && b < 0) return -1;
            if (a < 0) return b;
            if (b < 0) return a;
            long denom = ca + cb;
            return denom <= 0 ? a : (long)Math.Round((a * (double)ca + b * (double)cb) / denom);
        }

        // The server's averaged freshness wins over whatever the representative blob happened to carry.
        private static void ApplyAveragedWear(Thing t, KmhThingPayload p)
        {
            if (t == null || p == null) return;
            try
            {
                if (p.RotProgressTicks >= 0)
                {
                    CompRottable rot = t.TryGetComp<CompRottable>();
                    if (rot != null) rot.RotProgress = p.RotProgressTicks;
                }
                if (p.HitPoints > 0 && t.def != null && t.def.useHitPoints)
                    t.HitPoints = Math.Min(p.HitPoints, t.MaxHitPoints);
            }
            catch (Exception ex) { KmhLog.Debug($"KMH: apply averaged wear for {p.DefName} threw: {ex.Message}"); }
        }

        private static string SafeLabel(Thing t)
        {
            try { return t.LabelCapNoCount; } catch { return t.def?.label ?? t.def?.defName ?? "item"; }
        }
    }
}
