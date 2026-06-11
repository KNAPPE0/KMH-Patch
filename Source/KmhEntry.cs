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
    // Payload entry, invoked by the loader (Assemblies/KMHPatch.dll) once it knows which RWT generation is
    // installed. Owns Harmony + handler registration; the loader owns the Mod instance + settings
    public static class KmhEntry
    {
        public static Harmony HarmonyInstance { get; private set; }

        public static void Init(ModContentPack content, Mod mod)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            KmhLog.Info($"Bootstrap starting ({typeof(KmhEntry).Assembly.GetName().Name} {typeof(KmhEntry).Assembly.GetName().Version})");

            // Crash recovery first - if we died mid config-apply last session, restore the player's personal
            // configs before other mods read theirs
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
            SafeRun("reputation",   Features.Reputation.ReputationCache.Register);
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
            try
            {
                HarmonyInstance = new Harmony(Constants.HarmonyId);
                HarmonyInstance.PatchAll(typeof(KmhEntry).Assembly);
            }
            catch (System.Exception ex)
            {
                KmhLog.Error($"Harmony PatchAll failed - some KMH features may be inactive: {ex}");
            }
            int patched = HarmonyInstance?.GetPatchedMethods().Count() ?? 0;
            KmhLog.Info($"Patches applied - {patched} method(s), {sw.ElapsedMilliseconds}ms (deferred off the mod ctor)");
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
                "Show welcome dialog on launch",
                ref settings.ShowWelcomeOnLaunch,
                "If enabled, the KMH welcome dialog appears once per game launch. " +
                "You can always reopen it from the KMH About panel."
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
