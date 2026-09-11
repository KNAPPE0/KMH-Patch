using System.Collections.Generic;
using KMHPatch.Diagnostics;
using KMHPatch.Features.Sites.Dto;
using UnityEngine;
using Verse;

namespace KMHPatch.Features.Sites
{
    // The world-map look of a KMH marker; textures are cached because ExpandingIcon is read during world rendering.
    internal static class KmhMarkerArt
    {
        private static readonly Dictionary<string, Texture2D> _cache = new Dictionary<string, Texture2D>();

        // Misses are retried: ContentFinder answers nothing until mod content loads, and caching that poisons the slot.
        private static readonly Dictionary<string, int> _misses = new Dictionary<string, int>();
        internal const int ResolveAttempts = 3;

        // Two zoom ranges, two coordinate spaces - the same split every vanilla WorldObjectDef makes.
        internal const string Expanding = "KMHPatch/World/WorldObjects/Expanding/";
        internal const string Normal    = "KMHPatch/World/WorldObjects/";

        // Named rather than inlined so the offline suite can assert the two paths never converge on one image.
        public static string ExpandingPathFor(SiteEntry s) => PathFor(_expandingPaths, Expanding, ArtKeyFor(s));
        public static string NormalPathFor(SiteEntry s)    => PathFor(_normalPaths, Normal, ArtKeyFor(s));

        // Read per marker per frame while the world draws, off a fixed set of keys: concatenate once, not every frame.
        private static readonly Dictionary<string, string> _expandingPaths = new Dictionary<string, string>();
        private static readonly Dictionary<string, string> _normalPaths    = new Dictionary<string, string>();

        private static string PathFor(Dictionary<string, string> memo, string prefix, string key)
            => memo.TryGetValue(key, out string hit) ? hit : (memo[key] = prefix + key);

        private static Texture2D Tex(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (_cache.TryGetValue(path, out Texture2D hit)) return hit;
            _misses.TryGetValue(path, out int tried);
            if (tried >= ResolveAttempts) return null;   // believed missing: never pay ContentFinder per frame

            Texture2D t = LookupForTest != null ? LookupForTest(path) : Lookup(path);
            if (t != null) { _cache[path] = t; _misses.Remove(path); return t; }

            _misses[path] = tried + 1;
            // Said once, wrapped because this runs inside world rendering, and never for the self-test's staged misses.
            if (tried + 1 == ResolveAttempts && LookupForTest == null)
                try { KmhLog.Warn($"World marker art missing: {path} - that marker falls back to its def texture."); }
                catch { }
            return null;
        }

        private static Texture2D Lookup(string path)
        {
            try { return ContentFinder<Texture2D>.Get(path, reportFailure: false); } catch { return null; }
        }

        // ContentFinder needs a loaded game; the seam lets the offline suite drive the retry policy itself.
        internal static System.Func<string, Texture2D> LookupForTest;

        internal static void ResetForTest()
        {
            _cache.Clear();
            _misses.Clear();
        }

        // A per-frame budget, not a verdict: a marker placed during a load otherwise wore the def's art all session.
        internal static void ForgetMisses() => _misses.Clear();

        public static bool IsOutpost(SiteEntry s)
            => s != null && !string.IsNullOrEmpty(s.OutpostState);

        // One art identity per marker; state rides on the rim and priority, since borrowed vanilla art read as someone else's object.
        public static string ArtKeyFor(SiteEntry s)
        {
            if (s == null) return "";
            if (IsOutpost(s)) return "Outpost";
            switch (s.Archetype)
            {
                case SiteEntry.ArchetypeFarmland:  return "Farmland";
                case SiteEntry.ArchetypeQuarry:    return "Quarry";
                case SiteEntry.ArchetypeWoodland:  return "Woodland";
                case SiteEntry.ArchetypeRanch:     return "Ranch";
                case SiteEntry.ArchetypeRoadworks: return "Roadworks";
                case SiteEntry.ArchetypeCustom:    return "Custom";
                default:                           return "Generic";
            }
        }

        // KMH's own Noto Emoji art, not the vanilla work-site icons: those live in Ideology and break without that DLC.
        public static Texture2D IconFor(SiteEntry s) => Tex(ExpandingPathFor(s));

        public static Texture2D BaseIconFor(SiteEntry s) => Tex(NormalPathFor(s));

        // For markers that have no SiteEntry: the guild hall, which still wants the same retry budget and warn-once.
        internal static Texture2D NamedIcon(string key) => Tex(PathFor(_normalPaths, Normal, key));

        internal static Color Mine   => KmhMarkerColors.Mine;
        internal static Color Theirs => KmhMarkerColors.Theirs;

        // Secondary signal only: anything a player must not miss also changes priority, so colour is never alone.
        public static bool TryHaloFor(SiteEntry s, bool mine, out Color color)
            => KmhMarkerColors.TryHaloFor(s, mine, out color);

        // A claimable location is time-limited, so it wins the draw when markers overlap. Dormant gives way.
        public static float PriorityFor(SiteEntry s, float defPriority)
        {
            if (s == null) return defPriority;
            switch (s.OutpostState)
            {
                case SiteEntry.OutpostClaimable: return defPriority + 30f;
                case SiteEntry.OutpostHostile:   return defPriority + 20f;
                case SiteEntry.OutpostDormant:   return defPriority - 5f;
            }
            return defPriority;
        }

        // Names what the thing IS, not what it produces: "KMH site - Steel" made a captured fortress read as a shed.
        public static string LabelFor(SiteEntry s, string resolvedItemLabel)
        {
            if (s == null) return "KMH site";
            if (!string.IsNullOrEmpty(s.SiteName)) return s.SiteName;

            if (IsOutpost(s))
            {
                string state = StateWord(s.OutpostState);
                string kind  = TemplateWord(s.OutpostTemplate);
                return string.IsNullOrEmpty(state) ? kind : $"{state} {kind}";
            }

            return string.IsNullOrEmpty(resolvedItemLabel) ? "KMH site" : $"KMH site - {resolvedItemLabel}";
        }

        public static string StateWord(string outpostState)
        {
            switch (outpostState)
            {
                case SiteEntry.OutpostDerelict:  return "Derelict";
                case SiteEntry.OutpostHostile:   return "Hostile";
                case SiteEntry.OutpostDefeated:  return "Defeated";
                case SiteEntry.OutpostClaimable: return "Unclaimed";
                case SiteEntry.OutpostCaptured:  return "Captured";
                case SiteEntry.OutpostDormant:   return "Dormant";
                default:                         return "";
            }
        }

        public static string TemplateWord(string outpostTemplate)
        {
            switch (outpostTemplate)
            {
                case SiteEntry.TemplateRuins:     return "ruins";
                case SiteEntry.TemplateResource:  return "resource outpost";
                case SiteEntry.TemplateDepot:     return "depot";
                case SiteEntry.TemplateFortified: return "fortified outpost";
                case SiteEntry.TemplateRelay:     return "relay";
                default:                          return "outpost";
            }
        }

        public static string ArchetypeWord(string archetype)
        {
            switch (archetype)
            {
                case SiteEntry.ArchetypeFarmland:  return "Farmland";
                case SiteEntry.ArchetypeQuarry:    return "Quarry";
                case SiteEntry.ArchetypeWoodland:  return "Woodland";
                case SiteEntry.ArchetypeRanch:     return "Ranch";
                case SiteEntry.ArchetypeRoadworks: return "Roadworks";
                default:                           return "";
            }
        }
    }
}
