using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Verse;

namespace KMHPatch
{
    // The class name is load-bearing: renaming it orphans the player's existing settings file.
    public class KMHPatchMod : Mod
    {
        public static KMHPatchSettings Settings { get; private set; }

        // Read per frame and in network callbacks, so a failed settings load degrades to defaults instead of throwing.
        public static KMHPatchSettings SettingsOrDefault => Settings ??= new KMHPatchSettings();

        // Set by the payload at Init; draws the full settings panel.
        public static Action<Rect> SettingsDrawer;

        private static KMHPatchMod _instance;

        // For settings changed outside the settings window; a no-op before the mod ctor has run.
        public static void SaveSettings() { try { _instance?.WriteSettings(); } catch { } }

        public KMHPatchMod(ModContentPack content) : base(content)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            _instance = this;
            CarryOverSettings(content);
            Settings = GetSettings<KMHPatchSettings>();

            string flavour = DetectRwtFlavour();
            if (flavour == null)
            {
                Log.Warning("[Colony Nexus] RimWorld Together not detected - Colony Nexus stays dormant.");
                return;
            }

            try
            {
                string dll = PayloadPath(content, flavour);
                if (!File.Exists(dll))
                {
                    Log.Error($"[Colony Nexus] payload missing for RWT '{flavour}': {dll}");
                    return;
                }
                Assembly payload = Assembly.LoadFile(dll);
                // Registered so defs, GenTypes and StaticConstructorOnStartup all see the payload's types.
                content.assemblies.loadedAssemblies.Add(payload);
                payload.GetType("KMHPatch.KmhEntry")
                       .GetMethod("Init", BindingFlags.Public | BindingFlags.Static)
                       .Invoke(null, new object[] { content, this });
                Log.Message($"[Colony Nexus] loaded payload for RWT '{flavour}' from {RelativeTo(content.RootDir, dll)} ({sw.ElapsedMilliseconds}ms)");
            }
            catch (Exception ex)
            {
                Log.Error($"[Colony Nexus] payload load failed: {ex}");
            }
        }

        // The mod's folder was "KMHPatch" before the rename, and RimWorld names the settings file after the folder.
        internal const string LegacyFolder = "KMHPatch";

        // Copied, not moved, so an older copy still finds its own file. Never over a file this folder already has.
        private void CarryOverSettings(ModContentPack content)
        {
            try
            {
                string folder = content?.FolderName;
                if (string.IsNullOrEmpty(folder) || string.Equals(folder, LegacyFolder, StringComparison.OrdinalIgnoreCase)) return;
                string handle = GetType().Name;
                string now = SettingsFile(folder, handle), old = SettingsFile(LegacyFolder, handle);
                if (File.Exists(now) || !File.Exists(old)) return;
                File.Copy(old, now);
                Log.Message($"[Colony Nexus] carried the settings over from the {LegacyFolder} folder.");
            }
            catch (Exception ex)
            {
                Log.Warning($"[Colony Nexus] could not carry the settings over from the {LegacyFolder} folder: {ex.Message}");
            }
        }

        // RimWorld's own naming (LoadedModManager keeps it private): Config\Mod_<folder>_<Mod class>.xml.
        private static string SettingsFile(string folder, string handle)
            => Path.Combine(GenFilePaths.ConfigFolderPath, GenText.SanitizeFilename($"Mod_{folder}_{handle}.xml"));

        // LoadFolders.xml folders newest first, so a version with its own folder gets its own payload; 1.6 is the fallback.
        internal static string PayloadPath(ModContentPack content, string flavour)
        {
            string name = $"KMHPatch.{flavour}.dll";
            try
            {
                foreach (string folder in content.foldersToLoadDescendingOrder)
                {
                    string root = Path.IsPathRooted(folder) ? folder : Path.Combine(content.RootDir, folder);
                    string p = Path.Combine(Path.Combine(root, "KMHLib"), name);
                    if (File.Exists(p)) return p;
                }
            }
            catch { }
            return Path.Combine(content.RootDir, "1.6", "KMHLib", name);
        }

        private static string RelativeTo(string root, string path)
            => !string.IsNullOrEmpty(root) && path.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                ? path.Substring(root.Length).TrimStart('\\', '/') : path;

        // Two RWT generations both ship an assembly named RTClient, so probe for a moved type, not the name.
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

        // The loader is its own assembly and cannot see Constants.DisplayName; contract §89 keeps the two equal.
        public override string SettingsCategory() => "KMH: Colony Nexus";

        public override void DoSettingsWindowContents(Rect inRect)
        {
            if (SettingsDrawer != null) { SettingsDrawer(inRect); return; }
            var listing = new Listing_Standard();
            listing.Begin(inRect);
            listing.Label("Colony Nexus is dormant - RimWorld Together was not detected this session.");
            listing.End();
        }
    }
}
