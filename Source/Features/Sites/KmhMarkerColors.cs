using KMHPatch.Features.Sites.Dto;
using KMHPatch.UI;
using UnityEngine;

namespace KMHPatch.Features.Sites
{
    // Ownership is a halo around the marker, never a wash over it: the art has to stay readable as what it is.
    internal static class KmhMarkerColors
    {
        internal static readonly Color FallbackMine   = UI.KmhBrand.Accent;
        internal static readonly Color FallbackTheirs = UI.KmhBrand.Info;

        // Fixed on purpose: a state a player could recolour into something calm is worse than no colour at all.
        internal static readonly Color Claimable = UI.KmhBrand.Ink;
        internal static readonly Color Derelict  = UI.KmhBrand.Ring;
        internal static readonly Color Hostile   = UI.KmhBrand.Bad;
        internal static readonly Color Defeated  = UI.KmhBrand.Muted;
        internal static readonly Color Dormant   = UI.KmhBrand.Muted;

        internal static string ServerMine   { get; private set; } = "";
        internal static string ServerTheirs { get; private set; } = "";

        internal static void ApplyServerColors(string mine, string theirs)
        {
            ServerMine = mine ?? ""; ServerTheirs = theirs ?? "";
        }

        internal static void ClearServerColors() => ApplyServerColors("", "");

        private static KMHPatchSettings S => KMHPatchMod.Settings;

        // Same precedence the chat palette uses: a deliberate local choice wins, then the server's, then ours.
        private static Color Resolve(string playerHex, string serverHex, Color fallback)
        {
            if (S != null && !S.MarkerUseServer && KmhTheme.TryHex(playerHex, out Color p)) return p;
            if (KmhTheme.TryHex(serverHex, out Color v)) return v;
            return fallback;
        }

        // Read per marker per frame and TryHex parses: resolve on change, not per read.
        private static string _cachedPlayerMine, _cachedPlayerTheirs, _cachedServerMine, _cachedServerTheirs;
        private static bool   _cachedUseServer;
        private static Color  _mine, _theirs;
        private static bool   _colorsResolved;

        private static void EnsureColors()
        {
            string pm = S?.MarkerMine ?? "", pt = S?.MarkerTheirs ?? "";
            bool   us = S == null || S.MarkerUseServer;
            if (_colorsResolved && us == _cachedUseServer
                && pm == _cachedPlayerMine && pt == _cachedPlayerTheirs
                && ServerMine == _cachedServerMine && ServerTheirs == _cachedServerTheirs) return;

            _cachedPlayerMine = pm; _cachedPlayerTheirs = pt;
            _cachedServerMine = ServerMine; _cachedServerTheirs = ServerTheirs;
            _cachedUseServer  = us;
            _mine   = Resolve(pm, ServerMine,   FallbackMine);
            _theirs = Resolve(pt, ServerTheirs, FallbackTheirs);
            _colorsResolved = true;
        }

        internal static Color Mine   { get { EnsureColors(); return _mine;   } }
        internal static Color Theirs { get { EnsureColors(); return _theirs; } }

        // No thickness to offer: RimWorld's expanding-icon shader decides that, the same as for an owned settlement.
        internal static bool Enabled => S == null || S.MarkerShowRim;

        // No halo for a place that is nobody's and in no notable state, so the halos that matter carry weight.
        internal static bool TryHaloFor(SiteEntry s, bool mine, out Color color)
        {
            color = Color.white;
            if (s == null || !Enabled) return false;

            switch (s.OutpostState)
            {
                case SiteEntry.OutpostClaimable: color = Claimable; return true;
                case SiteEntry.OutpostHostile:   color = Hostile;   return true;
                case SiteEntry.OutpostDerelict:  color = Derelict;  return true;
                case SiteEntry.OutpostDefeated:  color = Defeated;  return true;
                case SiteEntry.OutpostDormant:   color = Dormant;   return true;
            }

            if (mine) { color = Mine; return true; }
            if (SiteOwnershipClient.IsPlayerControlled(s)) { color = Theirs; return true; }
            return false;
        }
    }
}
