using System;
using System.IO;
using KMHPatch.Diagnostics;
using Verse;

namespace KMHPatch.Items
{
    // Lossless Thing <-> XML round-trip via RimWorld's own Scribe system (the same one that saves your game), so
    // comps, hediffs, minified contents, quality, taint, HP and modded state all survive. Everything is guarded: if
    // Scribe is busy (a real save/load is running) or anything throws, we return null and the caller degrades to a
    // metadata/legacy rebuild - a KMH item flow must never crash the game or corrupt the save pipeline.
    internal static class KmhThingScribe
    {
        // True only when it's safe to borrow the global Scribe (no game save/load in progress).
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

        public static Thing Load(string xml)
        {
            if (string.IsNullOrEmpty(xml) || !ScribeIdle) return null;
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
                    Scribe.loader.FinalizeLoading();   // cross-ref resolve + PostLoadInit
                }
                catch { SafeStop(); throw; }

                if (t == null) return null;
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

        // Reset the global Scribe to Inactive if our borrow left it mid-operation, so RimWorld's own save/load is safe.
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
