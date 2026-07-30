using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Verse;

namespace KMHPatch
{
    // Version-agnostic bootstrap: KMH ships one payload per RWT generation in 1.6/KMHLib and this stub loads the
    // one matching the installed RWT. Class name is kept so the settings file stays the same.
    public class KMHPatchMod : Mod
    {
        public static KMHPatchSettings Settings { get; private set; }

        // Set by the payload at Init; draws the full settings panel.
        public static Action<Rect> SettingsDrawer;

        public KMHPatchMod(ModContentPack content) : base(content)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Settings = GetSettings<KMHPatchSettings>();

            string flavour = DetectRwtFlavour();
            if (flavour == null)
            {
                Log.Warning("[KMH-Patch] RimWorld Together not detected - KMH stays dormant.");
                return;
            }

            try
            {
                string dll = Path.Combine(content.RootDir, "1.6", "KMHLib", $"KMHPatch.{flavour}.dll");
                if (!File.Exists(dll))
                {
                    Log.Error($"[KMH-Patch] payload missing for RWT '{flavour}': {dll}");
                    return;
                }
                Assembly payload = Assembly.LoadFile(dll);
                // Register with RimWorld so defs, GenTypes, and StaticConstructorOnStartup all see the payload's
                // types
                content.assemblies.loadedAssemblies.Add(payload);
                payload.GetType("KMHPatch.KmhEntry")
                       .GetMethod("Init", BindingFlags.Public | BindingFlags.Static)
                       .Invoke(null, new object[] { content, this });
                Log.Message($"[KMH-Patch] loaded payload for RWT '{flavour}' ({sw.ElapsedMilliseconds}ms)");
            }
            catch (Exception ex)
            {
                Log.Error($"[KMH-Patch] payload load failed: {ex}");
            }
        }

        // 26.6.9.1 and 26.7.25.1 both ship an assembly called RTClient, so the name alone can't tell them apart.
        // Probe for a type that moved instead of keeping a list of versions.
        private static string DetectRwtFlavour()
        {
            var loaded = AppDomain.CurrentDomain.GetAssemblies();
            var names = new System.Collections.Generic.HashSet<string>(loaded.Select(a => a.GetName().Name));

            if (names.Contains("RTClient"))
            {
                Assembly shared = loaded.FirstOrDefault(a => a.GetName().Name == "RTShared");
                bool movedToMisc = shared != null && shared.GetType("RTShared.Misc.PacketHeader", false) != null;
                return movedToMisc ? "RTClientV2" : "RTClient";   // 26.7.25.1+ vs 26.6.9.1
            }

            if (names.Contains("GameClient")) return "GameClient";
            return null;
        }

        public override string SettingsCategory() => "KMH Patch";

        public override void DoSettingsWindowContents(Rect inRect)
        {
            if (SettingsDrawer != null) { SettingsDrawer(inRect); return; }
            var listing = new Listing_Standard();
            listing.Begin(inRect);
            listing.Label("KMH is dormant - RimWorld Together was not detected this session.");
            listing.End();
        }
    }
}
