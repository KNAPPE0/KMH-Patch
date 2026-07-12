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
    // Capture a real Thing into a state-preserving payload and rebuild it later. Item-loss prevention: replaces the
    // lossy "defName+count -> ThingMaker" path so deposits/withdraws never clean, repair, reroll, or strip items.
    // Tiers: simple stackables stay compact (def+count lossless); everything else deep-serializes via Scribe (full),
    // falling back to hp/quality/taint/stuff metadata (partial) with a warning if that fails.
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

        // Superset of IsSimple that ALSO allows rottable food/meals: a fungible item the server may stack with an
        // equal-identity one (wear weight-averaged). Still excludes weapons/apparel/minified and quality/biocode/art/
        // persona (real per-instance identity). Only used to set the Mergeable flag - it does NOT change what's
        // captured (exact HP + full scribe blob are still kept, so nothing is stripped or refreshed).
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

        public static KmhThingPayload Capture(Thing thing)
        {
            if (thing == null) return null;
            // Shared safety gate: an unsafe type (minified/corpse/pawn/missing-def) must never be captured. Returning
            // null makes the all-or-nothing caller abort and keep every item in the colony rather than destroy any.
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

            // Fungible-stacking metadata (additive; does not change the captured state). Mark stackable food/resources
            // so the server may merge equal stacks, and carry rot so it can weight-average freshness on merge.
            try
            {
                p.Mergeable = IsFungible(thing.def);
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

        // Rebuild the exact Thing. Full+scribe -> deserialize; otherwise best-effort from metadata. Returns null on
        // total failure so the caller can fall back to the legacy def+count path.
        public static Thing Restore(KmhThingPayload p)
        {
            if (p == null) return null;

            if (!string.IsNullOrEmpty(p.ScribeXml))
            {
                Thing t = KmhThingScribe.Load(p.ScribeXml);
                if (t != null)
                {
                    if (p.StackCount > 1 && t.def != null && t.def.stackLimit >= p.StackCount) t.stackCount = p.StackCount;
                    // Fungible stacks are weight-averaged server-side on merge, so the payload's rot/HP is the
                    // authoritative freshness (all merged stacks share one representative blob) - apply it over the blob.
                    if (p.Mergeable) ApplyAveragedWear(t, p);
                    return t;
                }
                KmhLog.Warn($"KMH: exact item restore failed for {p.DefName}; rebuilding from captured state (some detail may be lost).");
            }

            // Metadata / simple rebuild.
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

        // Deterministic storage identity: any state difference that must NOT merge changes the fingerprint. Kept in
        // lockstep with the addon's KmhItemSafety.GetStateFingerprint.
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

        // Apply a fungible payload's (server-averaged) rot + HP onto a freshly-restored blob item, so a merged stack's
        // freshness is what the server computed - not whatever the single representative blob happened to carry.
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
