using System;
using System.IO;
using Verse;

namespace KMHPatch.Persistence
{
    // The pre-rename "KMH-Patch" folder beside RimWorld's saves is moved over once.
    internal static class KmhDataFolder
    {
        internal const string Name = "Colony Nexus";
        internal const string LegacyName = "KMH-Patch";

        private static readonly object Gate = new object();
        private static string _root;

        public static string Root
        {
            get
            {
                lock (Gate) return _root ??= Resolve(GenFilePaths.SaveDataFolderPath, out _);
            }
        }

        // Moved, not copied: two live copies would drift apart. A move that fails leaves the old folder in use.
        internal static string Resolve(string saveData, out bool moved)
        {
            moved = false;
            string current = Path.Combine(saveData, Name), legacy = Path.Combine(saveData, LegacyName);
            try
            {
                if (Directory.Exists(current) || !Directory.Exists(legacy)) return current;
                Directory.Move(legacy, current);
                moved = true;
                Log.Message($"[Colony Nexus] moved its data folder from {LegacyName} to {Name}.");
                return current;
            }
            catch (Exception ex)
            {
                if (Directory.Exists(current)) return current;
                Log.Warning($"[Colony Nexus] could not move {LegacyName} to {Name} ({ex.GetType().Name}); using {LegacyName} this session.");
                return legacy;
            }
        }
    }
}
