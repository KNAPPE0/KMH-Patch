using System;
using HarmonyLib;
using KMHPatch.Diagnostics;
using Verse;

namespace KMHPatch.Patches
{
    // Drains KmhMainThread each frame on the main thread; Root.Update is core RimWorld, always present
    [HarmonyPatch(typeof(Root), nameof(Root.Update))]
    internal static class Patch_Root_Update_KmhPump
    {
        private static readonly string[] Names =
        {
            "main-thread pump",
            "chat images",        // polls at most one in-flight image request
            "chat video",         // and at most one video preparing to stream
            "watch links",        // and at most one watch link being resolved
            "activity",           // watches for a chair nobody is sitting in
            "debug uplink",
            "debug consent",      // main thread: the prompt touches the window stack
        };

        private static readonly Action[] Steps =
        {
            KmhMainThread.Pump,
            Features.Chat.ChatImageCache.Tick,
            Features.Chat.ChatVideoPlayer.Tick,
            Features.Chat.ChatYouTube.Tick,
            Features.PlayerStats.KmhActivity.Tick,
            KmhDebugUplink.Pump,
            KmhDebugConsent.PumpPrompt,
        };

        [HarmonyPostfix]
        private static void Postfix() => KmhFrameSteps.Run(Names, Steps);
    }
}
