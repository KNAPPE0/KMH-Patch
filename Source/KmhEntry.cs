using System.Linq;
using HarmonyLib;
using KMHPatch.Diagnostics;
using KMHPatch.Features.Guilds;
using KMHPatch.Features.LinkedAccounts;
using KMHPatch.Features.Marketplace;
using KMHPatch.Features.PlayerStats;
using KMHPatch.Features.Quests;
using KMHPatch.Features.Treasury;
using KMHPatch.Patches;
using KMHPatch.SubProtocol;
using UnityEngine;
using Verse;

namespace KMHPatch
{
    // Payload entry from the loader; owns Harmony and handlers after the loader detects the installed RWT version.
    public static class KmhEntry
    {
        public static Harmony HarmonyInstance { get; private set; }

        public static void Init(ModContentPack content, Mod mod)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            KmhLog.DebugEnabled = KMHPatchMod.Settings?.DebugLogging ?? false;   // payload wires the loader's setting
            KmhLog.Info($"Bootstrap starting ({typeof(KmhEntry).Assembly.GetName().Name} {typeof(KmhEntry).Assembly.GetName().Version})");
            KmhLog.Info($"KMH-Patch UI build {SubProtocol.KmhProtocol.DisplayVersion}·{SubProtocol.KmhProtocol.UiBuildTag} loaded. If the Guild Hall title doesn't show this tag, the game is loading an OLDER KMH-Patch DLL.");

            // Crash recovery first - if we died mid config-apply last session, restore the player's personal configs before other mods read theirs
            SafeRun("enforce-recovery", Features.Enforcement.EnforcementProfileApplier.Bootstrap);

            // Each registration is isolated so one failure can't skip the rest.
            SafeRun("handshake",    KmhHandshakeHandler.Register);
            SafeRun("linked",       LinkedAccountsHandler.Register);
            SafeRun("playerstats",  PlayerStatsHandler.Register);
            SafeRun("treasury",     TreasuryHandler.Register);
            SafeRun("marketplace",  MarketplaceHandler.Register);
            SafeRun("quests",       QuestHandler.Register);
            SafeRun("guilds",       GuildHandler.Register);
            SafeRun("sites",        Features.Sites.SiteHandler.Register);
            SafeRun("world",        Features.World.WorldHandler.Register);
            SafeRun("auctions",     Features.Auctions.AuctionHandler.Register);
            SafeRun("wantboard",    Features.WantBoard.WantHandler.Register);
            SafeRun("seasons",      Features.Seasons.SeasonHandler.Register);
            SafeRun("reputation",   Features.Reputation.ReputationCache.Register);
            SafeRun("notifications", Features.OfflineMail.NotificationHandler.Register);
            SafeRun("enforcement",  Features.Enforcement.EnforcementHandler.Register);
            SafeRun("extensions",   Extensibility.ExtensionLoader.DiscoverAndLoad);

            KMHPatchMod.SettingsDrawer = DrawSettings;

            // Harmony IL generation is the expensive part, and none of our patches matter before the main menu - so
            // it runs at load-finish (still before the menu renders) instead of inside the mod ctor
            LongEventHandler.ExecuteWhenFinished(ApplyPatches);

            KmhLog.Info($"Bootstrap complete - patches queued, " +
                        $"{Extensibility.ExtensionLoader.Loaded.Count} extension(s), {sw.ElapsedMilliseconds}ms");
        }

        private static void ApplyPatches()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            HarmonyInstance = new Harmony(Constants.HarmonyId);

            // Patch classes one-by-one so one missing RWT type only skips that patch instead of killing the whole batch.
            int skipped = 0;
            foreach (System.Type type in AccessTools.GetTypesFromAssembly(typeof(KmhEntry).Assembly))
            {
                try { HarmonyInstance.CreateClassProcessor(type).Patch(); }
                catch (System.Exception ex)
                {
                    skipped++;
                    KmhLog.Warn($"KMH: skipped patch class '{type?.FullName}' (RWT type missing in this build?) - {ex.Message.Split('\n')[0]}");
                }
            }

            // Manual, version-safe patches: resolve RWT types by name and skip cleanly if absent on this build (RWT
            // removed/renamed some types in newer releases). Kept out of the attribute loop so a missing type can't
            // trip ReflectionTypeLoadException during type scanning.
            Patch_DLG_GuildList_KmhRedirect.TryApply(HarmonyInstance);
            Patch_DLG_Leaderboard_KmhRedirect.TryApply(HarmonyInstance);

            int patched = HarmonyInstance.GetPatchedMethods().Count();
            KmhLog.Info($"Patches applied - {patched} method(s)" + (skipped > 0 ? $", {skipped} class(es) skipped" : "") +
                        $", {sw.ElapsedMilliseconds}ms (deferred off the mod ctor)");
        }

        private static void SafeRun(string step, System.Action action)
        {
            try { action(); }
            catch (System.Exception ex) { KmhLog.Error($"KMH bootstrap step '{step}' failed: {ex}"); }
        }

        // Full settings panel, drawn through the loader's Mod instance.
        private static void DrawSettings(Rect inRect)
        {
            KMHPatchSettings settings = KMHPatchMod.Settings;
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(inRect);

            Text.Font = GameFont.Medium;
            listing.Label("General");
            Text.Font = GameFont.Small;

            listing.Gap(4f);
            listing.CheckboxLabeled(
                "Show KMH What's New on launch",
                ref settings.ShowWhatsNewOnUpdate,
                "If enabled, the What's New changelog appears every launch (change notes included). " +
                "Turn it off to stop the popup; reopen it any time from the KMH tab."
            );
            listing.CheckboxLabeled(
                "Show KMH welcome / help popup",
                ref settings.ShowWelcomeOnLaunch,
                "If enabled, the KMH welcome dialog appears on launch (once per launch). " +
                "You can always reopen it from the KMH About panel."
            );
            listing.CheckboxLabeled(
                "Show KMH Discord / link reminder",
                ref settings.ShowDiscordReminder,
                "If enabled, the welcome popup includes a Discord / account-link reminder."
            );

            listing.Gap(6f);
            if (listing.ButtonText("Show welcome dialog next time the main menu loads"))
            {
                Patch_MainMenuDrawer_MainMenuOnGUI.ResetShownFlag();
            }

            listing.GapLine(12f);

            Text.Font = GameFont.Medium;
            listing.Label("Diagnostics");
            Text.Font = GameFont.Small;

            listing.Gap(4f);
            if (listing.ButtonText("Open KMH log folder"))
            {
                Application.OpenURL(KmhLog.LogFolderPath);
            }

            listing.Gap(6f);
            bool prevDebug = settings.DebugLogging;
            listing.CheckboxLabeled("Verbose debug logging",
                ref settings.DebugLogging,
                "Logs detailed KMH diagnostics and per-packet protocol traces. Off by default so normal play stays quiet.");
            if (prevDebug != settings.DebugLogging) KmhLog.DebugEnabled = settings.DebugLogging;   // apply live

            listing.CheckboxLabeled("Send debug logs to the server",
                ref settings.RemoteDebugLogging,
                "Mirrors every KMH log line to the server's Debug folder (timestamped, rate-limited) so the owner can " +
                "diagnose issues with you. Forces verbose logging on while active. The server can also turn this on " +
                "for everyone when the owner enables server-side debugging.");

            listing.GapLine(12f);

            Text.Font = GameFont.Medium;
            listing.Label("KMH API transport");
            Text.Font = GameFont.Small;
            listing.Gap(4f);
            listing.CheckboxLabeled("Use KMH API transport",
                ref settings.UseKmhApiTransport,
                "On by default - the recommended path for newer RWT builds. When the server advertises its KMH API, " +
                "KMH talks over that port instead of RWT chat (required where RWT chat is unavailable, e.g. RWT " +
                "26.6.23.1+). Falls back to chat if the port can't be reached. Reconnect for changes to take effect.");
            listing.CheckboxLabeled("Allow chat fallback",
                ref settings.AllowChatTransportFallback,
                "If the KMH API port can't be reached, keep using the RWT-chat transport so KMH still works.");

            listing.Gap(4f);
            listing.Label("API port (must match the server; default 5099):");
            string portText = listing.TextEntry(settings.KmhApiPort.ToString());
            if (int.TryParse(portText, out int newPort) && newPort >= 1 && newPort <= 65535) settings.KmhApiPort = newPort;

            listing.Gap(4f);
            listing.Label("Host override (blank = the RWT server's address):");
            settings.KmhApiHostOverride = listing.TextEntry(settings.KmhApiHostOverride ?? "");

            listing.GapLine(12f);

            Text.Font = GameFont.Medium;
            listing.Label("Server config enforcement");
            Text.Font = GameFont.Small;
            listing.Gap(4f);
            if (Features.Enforcement.EnforcementProfileApplier.IsApplied)
            {
                listing.Label("<color=#ffce4d>A server config profile is applied to your Config folder. " +
                              "Restoring puts your personal configs back (it re-applies if you rejoin an enforcing server).</color>");
                listing.Gap(4f);
                if (listing.ButtonText("Restore my original configs"))
                {
                    Features.Enforcement.EnforcementProfileApplier.Restore();
                    Messages.Message("KMH: restored your personal configs.", RimWorld.MessageTypeDefOf.PositiveEvent, false);
                }
            }
            else
            {
                listing.Label("<color=grey>No server config profile is applied.</color>");
            }

            listing.GapLine(12f);

            Text.Font = GameFont.Medium;
            listing.Label("About");
            Text.Font = GameFont.Small;

            listing.Gap(4f);
            listing.Label($"Version: {typeof(KmhEntry).Assembly.GetName().Version} ({typeof(KmhEntry).Assembly.GetName().Name})");
            listing.Label($"Package: {Constants.PackageId}");

            listing.End();
        }
    }
}
