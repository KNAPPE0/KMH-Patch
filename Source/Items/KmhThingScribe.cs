using System;
using System.IO;
using System.Xml.Linq;
using KMHPatch.Diagnostics;
using Verse;

namespace KMHPatch.Items
{
    // Borrows RimWorld's global Scribe, so every path must degrade rather than corrupt the save pipeline.
    internal static class KmhThingScribe
    {
        private static bool ScribeIdle => Scribe.mode == LoadSaveMode.Inactive;

        public static string Save(Thing thing)
        {
            if (thing == null || !ScribeIdle) return null;
            string tmp = null;
            try
            {
                tmp = Path.Combine(Path.GetTempPath(), "kmh_thing_" + Guid.NewGuid().ToString("N") + ".xml");
                Scribe.saver.InitSaving(tmp, "kmhthing");
                try
                {
                    Thing t = thing;
                    Scribe_Deep.Look(ref t, "t");
                }
                finally { Scribe.saver.FinalizeSaving(); }

                string xml = File.ReadAllText(tmp);
                return string.IsNullOrEmpty(xml) ? null : xml;
            }
            catch (Exception ex)
            {
                KmhLog.Debug($"KmhThingScribe.Save failed for {thing?.def?.defName}: {ex.Message}");
                SafeStop();
                return null;
            }
            finally { TryDelete(tmp); }
        }

        internal static Func<Type, bool> FromGameOrModForTest;

        private static bool FromGameOrMod(Type t)
            => FromGameOrModForTest != null ? FromGameOrModForTest(t) : Features.Enforcement.ConfigXmlGuard.IsGameOrModType(t);

        // Scribe instantiates whatever Class the blob names, so it must match the payload's def and class before loading.
        internal static bool ShapeMatches(string xml, string expectedDef, Func<string, Type> thingClassOf,
                                          Func<string, Type> resolveType, out string why)
        {
            why = null;
            try
            {
                var settings = new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Prohibit, XmlResolver = null };
                XElement root;
                using (var reader = System.Xml.XmlReader.Create(new System.IO.StringReader(xml), settings))
                    root = XDocument.Load(reader).Root;
                if (root == null) { why = "no item in the saved state"; return false; }
                // Scribe finds <t> by qualified name, namespace or not, so a namespaced <t> would be loaded while a plain decoy is checked.
                foreach (XElement e in root.DescendantsAndSelf())
                {
                    bool namespaced = e.Name.Namespace != XNamespace.None;
                    foreach (XAttribute a in e.Attributes()) namespaced |= a.IsNamespaceDeclaration || a.Name.Namespace != XNamespace.None;
                    if (namespaced) { why = "its saved state uses XML namespaces, which the game never writes"; return false; }
                }
                XElement t = null;
                int items = 0;
                foreach (XElement child in root.Elements()) { items++; t = child; }
                if (items != 1 || t.Name.LocalName != "t") { why = items == 0 ? "no item in the saved state" : "its saved state holds something besides the one item"; return false; }
                string def = (string)t.Element("def");
                if (!string.Equals(def, expectedDef, StringComparison.Ordinal)) { why = $"saved state is {def}, not {expectedDef}"; return false; }
                Type expected = thingClassOf(def);
                if (expected == null) { why = $"{def} is not a known item"; return false; }
                string cls = (string)t.Attribute("Class");
                Type actual = string.IsNullOrEmpty(cls) ? typeof(Thing) : resolveType(cls);
                if (actual != expected) { why = $"saved class {cls ?? "Thing"} is not {expected.Name}"; return false; }
                // What it is made of and what its crate holds must exist here too, or it loads as something else.
                foreach (System.Xml.Linq.XElement s in t.DescendantsAndSelf("stuff"))
                {
                    string stuff = ((string)s ?? "").Trim();
                    if (stuff.Length > 0 && thingClassOf(stuff) == null) { why = $"its material {stuff} is not loaded"; return false; }
                }
                // Scribe loads every child of a list, whatever it is named.
                foreach (System.Xml.Linq.XElement li in t.Descendants("innerList").Elements())
                {
                    string inner = ((string)li.Element("def") ?? "").Trim();
                    Type innerClass = inner.Length > 0 ? thingClassOf(inner) : null;
                    if (inner.Length > 0 && innerClass == null) { why = $"the {inner} inside it is not loaded"; return false; }
                    // A crate holds a building: its class is its def's, and never a pawn or a body.
                    if (innerClass == null) continue;
                    string innerCls = (string)li.Attribute("Class");
                    if (!string.IsNullOrEmpty(innerCls) && resolveType(innerCls) != innerClass)
                    { why = $"the {inner} inside it is saved as {innerCls}"; return false; }
                    if (typeof(Pawn).IsAssignableFrom(innerClass) || typeof(Corpse).IsAssignableFrom(innerClass))
                    { why = $"it holds {inner}, which is not an item"; return false; }
                }
                // Every nested Class too: Scribe constructs the named type before it asks whether the game can save it.
                foreach (System.Xml.Linq.XElement e in t.Descendants())
                {
                    string named = (string)e.Attribute("Class");
                    if (string.IsNullOrEmpty(named)) continue;
                    if (named.IndexOfAny(new[] { ',', '[', ']', '`', '&', '*' }) >= 0)
                    { why = $"its saved state names {named} in a form only code outside the game's mods would use"; return false; }
                    Type nested = resolveType(named);
                    if (nested != null && (nested.IsAbstract || !typeof(IExposable).IsAssignableFrom(nested) || !FromGameOrMod(nested)))
                    { why = $"its saved state names {named}, which is not a game type that can be saved"; return false; }
                }
                return true;
            }
            catch (Exception ex) { why = "unreadable saved state: " + ex.Message; return false; }
        }

        // Stands in for the shape check, so a test can reach the check on what was actually loaded.
        internal static Func<string, string, bool> ShapeForTest;

        // A thing of its def's own class, in the item category: never a pawn, a body or an uncrated building.
        internal static bool LoadedAsExpected(Thing t, ThingDef expected, out string why)
        {
            why = null;
            if (t?.def == null) { why = "it loaded without a def"; return false; }
            if (expected != null && t.def != expected) { why = $"it loaded as {t.def.defName}"; return false; }
            if (t.GetType() != t.def.thingClass) { why = $"it loaded as {t.GetType().Name}, not its def's {t.def.thingClass?.Name}"; return false; }
            if (t.def.category != ThingCategory.Item || t is Pawn || t is Corpse || t is Building) { why = $"it loaded as {t.def.defName}, which is not an item"; return false; }
            if (t is RimWorld.MinifiedThing crate && !CrateHoldsOneThing(crate)) { why = "its crate does not hold exactly one thing of its def's own class"; return false; }
            return true;
        }

        internal static bool CrateHoldsOneThing(RimWorld.MinifiedThing crate)
        {
            ThingOwner held = crate.GetDirectlyHeldThings();
            Thing inner = held != null && held.Count == 1 ? held[0] : null;
            return inner?.def != null && inner.GetType() == inner.def.thingClass && !(inner is Pawn) && !(inner is Corpse);
        }

        public static Thing Load(string xml, string expectedDef = null)
        {
            if (string.IsNullOrEmpty(xml) || !ScribeIdle) return null;
            string refused = "refused by the test";
            if (expectedDef != null
                && !(ShapeForTest?.Invoke(xml, expectedDef)
                     ?? ShapeMatches(xml, expectedDef, d => DefDatabase<ThingDef>.GetNamedSilentFail(d)?.thingClass,
                                     n => GenTypes.GetTypeInAnyAssembly(n), out refused)))
            {
                KmhLog.Warn($"KMH: refused to load a saved item - {refused}.");
                return null;
            }
            string tmp = null;
            try
            {
                tmp = Path.Combine(Path.GetTempPath(), "kmh_thing_" + Guid.NewGuid().ToString("N") + ".xml");
                File.WriteAllText(tmp, xml);

                Thing t = null;
                Scribe.loader.InitLoading(tmp);
                try
                {
                    Scribe_Deep.Look(ref t, "t");
                    Scribe.loader.FinalizeLoading();
                }
                catch { SafeStop(); throw; }

                if (t == null) return null;
                // A crate whose building did not load is an empty crate, not the item that was stored.
                if (t is RimWorld.MinifiedThing crate && crate.InnerThing == null)
                {
                    KmhLog.Warn($"KMH: refused a restored {t.def?.defName} - the building inside it did not load.");
                    return null;
                }
                // The shape check read the XML; Scribe may have read another node, so what loaded is checked too.
                if (!LoadedAsExpected(t, expectedDef == null ? null : DefDatabase<ThingDef>.GetNamedSilentFail(expectedDef), out string wrong))
                {
                    KmhLog.Warn($"KMH: refused a restored {expectedDef ?? "item"} - {wrong}.");
                    return null;
                }
                // Fresh id so a restored item can't collide with a live map thing.
                try { t.thingIDNumber = -1; ThingIDMaker.GiveIDTo(t); } catch { }
                return t;
            }
            catch (Exception ex)
            {
                KmhLog.Debug($"KmhThingScribe.Load failed: {ex.Message}");
                SafeStop();
                return null;
            }
            finally { TryDelete(tmp); }
        }

        // A borrow left mid-operation would break RimWorld's own save/load, so force the global Scribe back to Inactive.
        private static void SafeStop()
        {
            try { if (Scribe.mode != LoadSaveMode.Inactive) Scribe.ForceStop(); } catch { }
        }

        private static void TryDelete(string path)
        {
            if (path == null) return;
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }
}
