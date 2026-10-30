using System;
using HarmonyLib;
using KMHPatch.Diagnostics;
using Verse;

namespace KMHPatch.Patches
{
    // Root.Update is core RimWorld, always present, and runs on the main thread every frame.
    [HarmonyPatch(typeof(Root), nameof(Root.Update))]
    internal static class Patch_Root_Update_KmhPump
    {
        private static readonly string[] Names =
        {
            "main-thread pump",
            "chat images",
            "chat video",
            "watch links",
            "activity",
            "debug uplink",
            "debug consent",      // main thread: the prompt touches the window stack
            "discord presence",
            "steam presence",
            "steam join",
            "global account",
            "join ticket",
            "global presence",
            "global dms",
            "join preparation",
            "tick rate",
            "game log watch",
            "report outbox",
            "report status",
            "error offer",
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
            Features.Discord.KmhRichPresence.Tick,
            Features.Discord.KmhSteamPresence.Tick,
            Features.Discord.KmhSteamJoinBoot.Tick,
            Features.Account.KmhAccountClient.Watch,
            Features.Global.KmhJoinTicketClient.Tick,
            Features.Friends.KmhPresenceReporter.Tick,
            // Driven here, not by the Comms window, or a cross-server DM shows nothing until Comms is opened.
            Features.Global.KmhGlobalDmClient.PollIfDue,
            Features.Mods.KmhJoinPreparation.Tick,
            SampleTickRate,
            Features.Reports.KmhGameLogWatch.Tick,
            Features.Reports.KmhReportClient.Tick,
            Features.Reports.KmhMyReports.Tick,
            Features.Reports.KmhErrorOffer.Tick,
        };

        private static readonly long[] Timings = new long[Steps.Length];

        [HarmonyPostfix]
        private static void Postfix()
        {
            KmhPerfSampler.Frame(UnityEngine.Time.unscaledDeltaTime);
            KmhFrameSteps.Run(Names, Steps, Timings);
            KmhPerfSampler.Steps(Names, Timings);
        }

        private static void SampleTickRate()
        {
            TickManager ticks = Current.ProgramState == ProgramState.Playing ? Current.Game?.tickManager : null;
            if (ticks == null) { KmhPerfSampler.NoGame(); return; }
            KmhPerfSampler.Ticks(ticks.TicksGame, ticks.TickRateMultiplier, ticks.Paused);
        }
    }
}
