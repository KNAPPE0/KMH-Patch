using UnityEngine;
using Verse;
using KMHPatch.Diagnostics;

namespace KMHPatch.UI
{
    // Lazy-loaded texture cache for icon buttons. Drop PNGs at Textures/KMHPatch/UI/{slot}.png - 64x64 transparent
    // single-color silhouettes (RimWorld tints, so colored art ends up muddy). Missing files return null and
    // IconButton degrades to text-only
    [StaticConstructorOnStartup]
    internal static class KMHTextures
    {
        // Main tab feature buttons.
        public static readonly Texture2D Guild       = Load("KMHPatch/UI/Guild");
        public static readonly Texture2D Quests      = Load("KMHPatch/UI/Quests");
        public static readonly Texture2D Marketplace = Load("KMHPatch/UI/Marketplace");
        public static readonly Texture2D Treasury    = Load("KMHPatch/UI/Treasury");
        public static readonly Texture2D Leaderboard = Load("KMHPatch/UI/Leaderboard");
        public static readonly Texture2D Ping        = Load("KMHPatch/UI/Ping");
        public static readonly Texture2D Log         = Load("KMHPatch/UI/Log");
        public static readonly Texture2D About       = Load("KMHPatch/UI/About");

        // Action buttons in feature dialogs.
        public static readonly Texture2D Buy         = Load("KMHPatch/UI/Buy");
        public static readonly Texture2D Post        = Load("KMHPatch/UI/Post");
        public static readonly Texture2D Cancel      = Load("KMHPatch/UI/Cancel");
        public static readonly Texture2D Claim       = Load("KMHPatch/UI/Claim");
        public static readonly Texture2D Submit      = Load("KMHPatch/UI/Submit");
        public static readonly Texture2D Approve     = Load("KMHPatch/UI/Approve");
        public static readonly Texture2D Deposit     = Load("KMHPatch/UI/Deposit");
        public static readonly Texture2D Withdraw    = Load("KMHPatch/UI/Withdraw");

        // Boot diagnostic: counts how many of the 16 slots actually resolved a file at startup, lets admins confirm
        // their PNG drop landed where expected without having to open every dialog
        public static int LoadedCount { get; private set; }
        public static int TotalSlots  => 16;

        static KMHTextures()
        {
            int n = 0;
            if (Guild       != null) n++;
            if (Quests      != null) n++;
            if (Marketplace != null) n++;
            if (Treasury    != null) n++;
            if (Leaderboard != null) n++;
            if (Ping        != null) n++;
            if (Log         != null) n++;
            if (About       != null) n++;
            if (Buy         != null) n++;
            if (Post        != null) n++;
            if (Cancel      != null) n++;
            if (Claim       != null) n++;
            if (Submit      != null) n++;
            if (Approve     != null) n++;
            if (Deposit     != null) n++;
            if (Withdraw    != null) n++;
            LoadedCount = n;
            KmhLog.Info($"Textures: {n}/{TotalSlots} icons loaded from Textures/KMHPatch/UI/");
        }

        private static Texture2D Load(string path)
        {
            try { return ContentFinder<Texture2D>.Get(path, reportFailure: false); }
            catch { return null; }
        }
    }
}
