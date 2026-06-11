using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Verse;

namespace KMHPatch
{
    // Version-agnostic bootstrap. RWT 26.6.9.1 renamed its assemblies (GameClient -> RTClient etc.), so KMH ships
    // one payload per generation in 1.6/KMHLib and this stub loads the one matching the installed RWT. Keeps the
    // KMHPatchMod class name so the settings file stays the same
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

        // The RWT client assembly name doubles as the payload key.
        private static string DetectRwtFlavour()
        {
            var names = new System.Collections.Generic.HashSet<string>(
                AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetName().Name));
            if (names.Contains("RTClient"))  return "RTClient";
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
