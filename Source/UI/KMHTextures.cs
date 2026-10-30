using UnityEngine;
using Verse;
using KMHPatch.Diagnostics;

namespace KMHPatch.UI
{
    // A missing PNG loads as null, so its button falls back to text.
    [StaticConstructorOnStartup]
    internal static class KMHTextures
    {
        public static readonly Texture2D Guild       = Load("KMHPatch/UI/Guild");
        public static readonly Texture2D Quests      = Load("KMHPatch/UI/Quests");
        public static readonly Texture2D Marketplace = Load("KMHPatch/UI/Marketplace");
        public static readonly Texture2D Treasury    = Load("KMHPatch/UI/Treasury");
        public static readonly Texture2D Sites       = Load("KMHPatch/UI/Sites");
        public static readonly Texture2D Roadworks   = Load("KMHPatch/UI/Roadworks");
        public static readonly Texture2D Comms       = Load("KMHPatch/UI/Comms");
        public static readonly Texture2D World       = Load("KMHPatch/UI/World");
        public static readonly Texture2D Leaderboard = Load("KMHPatch/UI/Leaderboard");
        public static readonly Texture2D Ping        = Load("KMHPatch/UI/Ping");
        public static readonly Texture2D Log         = Load("KMHPatch/UI/Log");
        public static readonly Texture2D About       = Load("KMHPatch/UI/About");

        public static readonly Texture2D Buy         = Load("KMHPatch/UI/Buy");
        public static readonly Texture2D Post        = Load("KMHPatch/UI/Post");
        public static readonly Texture2D Cancel      = Load("KMHPatch/UI/Cancel");
        public static readonly Texture2D Claim       = Load("KMHPatch/UI/Claim");
        public static readonly Texture2D Submit      = Load("KMHPatch/UI/Submit");
        public static readonly Texture2D Approve     = Load("KMHPatch/UI/Approve");
        public static readonly Texture2D Deposit     = Load("KMHPatch/UI/Deposit");
        public static readonly Texture2D Withdraw    = Load("KMHPatch/UI/Withdraw");

        // Logged at startup, so an admin sees how many icons loaded without opening every dialog.
        public static int LoadedCount { get; private set; }
        public static int TotalSlots  => 20;

        static KMHTextures()
        {
            Texture2D[] all =
            {
                Guild, Quests, Marketplace, Treasury, Sites, Roadworks, Comms, World, Leaderboard, Ping, Log, About,
                Buy, Post, Cancel, Claim, Submit, Approve, Deposit, Withdraw,
            };
            int n = 0;
            foreach (Texture2D t in all) if (t != null) n++;
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
