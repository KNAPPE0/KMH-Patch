using System;
using System.Collections.Generic;
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
    public static class KmhEntry
    {
        public static Harmony HarmonyInstance { get; private set; }

        public static void Init(ModContentPack content, Mod mod)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            // Before anything opens a file in it, so the one-time move from the old folder name runs on this thread.
            _ = Persistence.KmhDataFolder.Root;
            KmhLog.DebugEnabled    = KMHPatchMod.Settings?.DebugLogging ?? false;   // payload wires the loader's setting
            KmhLog.ProtocolEnabled = KMHPatchMod.Settings?.ProtocolLogging ?? false;
            KmhLog.Info($"Bootstrap starting ({typeof(KmhEntry).Assembly.GetName().Name} {typeof(KmhEntry).Assembly.GetName().Version})");
            KmhLog.Info($"Colony Nexus UI build {SubProtocol.KmhProtocol.DisplayVersion}·{SubProtocol.KmhProtocol.UiBuildTag} loaded. If the Guild Hall title doesn't show this tag, the game is loading an older Colony Nexus DLL.");

            if (KMHPatchMod.Settings != null)
            {
                string saved = KMHPatchMod.Settings.GlobalBaseUrl;
                string kept = Features.Global.GlobalHttp.ForRelease(saved, Features.Global.GlobalHttp.IsPlaytestBuild);
                if (kept != saved)
                {
                    KMHPatchMod.Settings.GlobalBaseUrl = kept;
                    KMHPatchMod.SaveSettings();
                    KmhLog.Info("Global: the playtest directory address was cleared; this release browses the official directory.");
                }
            }
            Features.Global.GlobalHttp.BaseUrl =
                Features.Global.GlobalHttp.Normalize(KMHPatchMod.Settings?.GlobalBaseUrl);
            if (!Features.Global.GlobalHttp.IsOfficial)
                KmhLog.Info($"Global: browsing {Features.Global.GlobalHttp.BaseUrl} instead of the official directory.");

            // Before RWT's first packet: a MessagePack type initializer that throws stays broken for the session.
            SafeRun("messagepack-coreclr", () =>
            {
                Compat.KmhMessagePackCoreClr.Apply(new Harmony(Constants.HarmonyId));
                KmhLog.Info("MessagePack on CoreCLR: " + Compat.KmhMessagePackCoreClr.Status);
            });

            // First, or another mod reads a config left half-applied by last session's crash.
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
            SafeRun("roadworks",    Features.Roadworks.RoadworksHandler.Register);
            SafeRun("frontier",     Features.Frontier.FrontierHandler.Register);
            SafeRun("world",        Features.World.WorldHandler.Register);
            SafeRun("auctions",     Features.Auctions.AuctionHandler.Register);
            SafeRun("wantboard",    Features.WantBoard.WantHandler.Register);
            SafeRun("mail",         Features.Mail.MailHandler.Register);
            SafeRun("connected",    Features.ConnectedTreasury.ConnectedTreasuryHandler.Register);
            SafeRun("guild-connected", Features.ConnectedGuild.ConnectedGuildHandler.Register);
            SafeRun("federation",   Features.Federation.FederationHandler.Register);
            SafeRun("chat",         Features.Chat.ChatHandler.Register);
            SafeRun("server-config", Features.Admin.ServerConfigCache.Register);
            SafeRun("join-ticket", Features.Global.KmhJoinTicketClient.Register);
            SafeRun("comms-notify", Features.Chat.KmhCommsNotifications.Register);
            SafeRun("seasons",      Features.Seasons.SeasonHandler.Register);
            SafeRun("leaderboards", Features.Leaderboards.LeaderboardHandler.Register);
            SafeRun("reputation",   Features.Reputation.ReputationCache.Register);
            SafeRun("notifications", Features.OfflineNotices.NotificationHandler.Register);
            SafeRun("enforcement",  Features.Enforcement.EnforcementHandler.Register);
            SafeRun("reports",      Features.Reports.KmhReportClient.Register);
            SafeRun("extensions",   Extensibility.ExtensionLoader.DiscoverAndLoad);
            SafeRun("video-cache",  SweepVideoCache);
            // IKmhClientExtension promises Shutdown() on a clean quit; Unity raises this only on a graceful exit.
            SafeRun("extension-shutdown", () =>
                UnityEngine.Application.quitting += Extensibility.ExtensionLoader.ShutdownAll);
            // Dropped explicitly on quit so the status clears at once and RWT's own presence is handed back.
            SafeRun("discord-shutdown", () =>
                UnityEngine.Application.quitting += Features.Discord.KmhRichPresence.Shutdown);

            KMHPatchMod.SettingsDrawer = DrawSettings;

            // Harmony IL generation is the expensive part and matters to nothing before the main menu.
            LongEventHandler.ExecuteWhenFinished(ApplyPatches);

            LongEventHandler.ExecuteWhenFinished(Diagnostics.KmhDuplicateModCheck.Report);

            KmhLog.Info($"Bootstrap complete - patches queued, " +
                        $"{Extensibility.ExtensionLoader.Loaded.Count} extension(s), {sw.ElapsedMilliseconds}ms");
        }

        // Read by the self-test, so a hook a new RWT build no longer fits fails loudly, not as one boot warning.
        internal static readonly List<string> SkippedPatchClasses = new List<string>();

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
                    lock (SkippedPatchClasses) SkippedPatchClasses.Add(type?.FullName ?? "?");
                    KmhLog.Warn($"KMH: skipped patch class '{type?.FullName}' (RWT type missing in this build?) - {ex.Message.Split('\n')[0]}");
                }
            }

            // By name, outside the attribute loop: a type missing on this RWT build would trip the scan otherwise.
            Patch_DLG_GuildList_KmhRedirect.TryApply(HarmonyInstance);
            Patch_DLG_Leaderboard_KmhRedirect.TryApply(HarmonyInstance);
            Patch_RwtSettlementIcon.TryApply(HarmonyInstance);
            Patch_DiscordManager_KmhPresence.Apply(HarmonyInstance);

            int patched = HarmonyInstance.GetPatchedMethods().Count();
            KmhLog.Info($"Patches applied - {patched} method(s)" + (skipped > 0 ? $", {skipped} class(es) skipped" : "") +
                        $", {sw.ElapsedMilliseconds}ms (deferred off the mod ctor)");
        }

        private static void SweepVideoCache()
        {
            Features.Chat.ChatVideoCache.CacheGigabytes = KMHPatchMod.Settings?.VideoCacheGigabytes ?? 2;
            Features.Chat.ChatVideoCache.Trim();
        }

        private static void SafeRun(string step, System.Action action)
        {
            try { action(); }
            catch (System.Exception ex) { KmhLog.Error($"KMH bootstrap step '{step}' failed: {ex}"); }
        }

        private static string DiscordSettingsKey(KMHPatchSettings s)
            => $"{s.DiscordPresenceEnabled}|{s.DiscordAppId}|{s.AllowDiscordJoin}|{s.DiscordShowPrivateServerName}"
             + $"|{s.DiscordShowActivity}|{s.DiscordShowGameplay}|{s.DiscordDetailedActivity}|{s.DiscordShowMultiplayer}"
             + $"|{s.DiscordShowServerName}|{s.DiscordShowColonyInfo}|{s.DiscordShowElapsed}|{s.DiscordUpdateSeconds}";

        private static string DetailName(bool detailed)
            => detailed ? UI.KmhText.Get("KMH_Settings_DiscordDetailed", "Detailed - raids, disasters, caravans")
                        : UI.KmhText.Get("KMH_Settings_DiscordSimple", "Simple - in a colony, or multiplayer");

        // The FloatMenu fires after this call returns, so it writes back through `set` rather than a captured ref.
        private static void NotifyRow(UI.KmhStack st, string label, Func<int> get, Action<int> set)
        {
            int lv = get(); lv = lv < 0 ? 0 : lv > 2 ? 2 : lv;
            if (st.ButtonLabeled(label, NotifyName(lv)))
                Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption>
                {
                    new FloatMenuOption(NotifyName(0), () => { set(0); KMHPatchMod.SaveSettings(); }),
                    new FloatMenuOption(NotifyName(1), () => { set(1); KMHPatchMod.SaveSettings(); }),
                    new FloatMenuOption(NotifyName(2), () => { set(2); KMHPatchMod.SaveSettings(); }),
                }));
        }

        // A picker, not a slider: a slider would need drag handling inside a list measured and drawn twice a frame.
        private static void ChatOpacityMenu(KMHPatchSettings settings)
        {
            List<FloatMenuOption> opts = new List<FloatMenuOption>();
            for (int pct = 100; pct >= Mathf.RoundToInt(KMHPatchSettings.MinChatOpacity * 100f); pct -= 10)
            {
                int chosen = pct;
                opts.Add(new FloatMenuOption(chosen + "%", () => { settings.ChatWindowOpacity = chosen / 100f; KMHPatchMod.SaveSettings(); }));
            }
            Find.WindowStack.Add(new FloatMenu(opts));
        }

        private static void VideoVolumeMenu(KMHPatchSettings settings)
        {
            List<FloatMenuOption> opts = new List<FloatMenuOption>();
            for (int pct = 100; pct >= 0; pct -= 10)
            {
                int chosen = pct;
                opts.Add(new FloatMenuOption(chosen + "%", () =>
                {
                    settings.ChatVideoVolume = chosen / 100f;
                    if (chosen > 0) settings.ChatVideoMuted = false;   // picking a volume is a request to hear it
                    KMHPatchMod.SaveSettings();
                    Features.Chat.ChatVideoPlayer.SyncFromSettings();
                }));
            }
            Find.WindowStack.Add(new FloatMenu(opts));
        }

        private static string QualityLabel(int height)
        {
            if (height <= 0) return UI.KmhText.Get("KMH_Settings_VideoQualityDefault", "Server default ({0}p)", Features.Chat.ChatYouTube.ServerMaxHeight);
            return height + "p";
        }

        // Blank means "use the layer below", so a bad value only falls through - this exists to say why nothing changed.
        internal static string InvalidColourList(KMHPatchSettings s)
        {
            if (s == null) return null;
            List<string> bad = new List<string>();
            void Check(string label, string hex) { if (!string.IsNullOrWhiteSpace(hex) && !UI.KmhTheme.TryHex(hex, out _)) bad.Add(label); }

            Check(UI.KmhText.Get("KMH_Settings_ColAccent", "Accent"),               s.ThemeAccent);
            Check(UI.KmhText.Get("KMH_Settings_ServerChat", "Server chat"),         s.ThemeServerChat);
            Check(UI.KmhText.Get("KMH_Settings_GuildChat", "Guild chat"),           s.ThemeGuildChat);
            Check(UI.KmhText.Get("KMH_Settings_DirectMessages", "Direct messages"), s.ThemeDirectMsg);
            Check(UI.KmhText.Get("KMH_Settings_ColDiscordMark", "Discord mark"),    s.ThemeDiscord);
            Check(UI.KmhText.Get("KMH_Settings_ColPlayerName", "Player name"),      s.ThemeNameNormal);
            Check(UI.KmhText.Get("KMH_Settings_ColMessageText", "Message text"),    s.ThemeTextNormal);
            Check(UI.KmhText.Get("KMH_Settings_ColDiscordName", "Discord name"),    s.ThemeNameDiscord);
            Check(UI.KmhText.Get("KMH_Settings_ColDiscordText", "Discord text"),    s.ThemeTextDiscord);

            return bad.Count == 0 ? null : string.Join(", ", bad.ToArray());
        }

        internal static string InvalidMarkerColourList(KMHPatchSettings s)
        {
            if (s == null) return null;
            List<string> bad = new List<string>();
            void Check(string label, string hex) { if (!string.IsNullOrWhiteSpace(hex) && !UI.KmhTheme.TryHex(hex, out _)) bad.Add(label); }

            Check(UI.KmhText.Get("KMH_Settings_MarkerYours", "Yours"),          s.MarkerMine);
            Check(UI.KmhText.Get("KMH_Settings_MarkerOthers", "Other players"), s.MarkerTheirs);

            return bad.Count == 0 ? null : string.Join(", ", bad.ToArray());
        }

        private static string NotifyName(int level)
        {
            switch (level)
            {
                case 0:  return UI.KmhText.Get("KMH_Settings_NotifySilent", "Silent (badge only)");
                case 2:  return UI.KmhText.Get("KMH_Settings_NotifyPopupSound", "Popup + sound");
                default: return UI.KmhText.Get("KMH_Settings_NotifyPopup", "Popup");
            }
        }

        private static Vector2 _settingsScroll;

        private static float _measuredW = -1f;
        private static float _measuredY;
        private static float _measuredAt = -999f;

        // Text.CalcHeight per row is RimWorld's most expensive text call, so the measure pass is not run every frame.
        private static void DrawSettings(Rect inRect)
        {
            KMHPatchSettings settings = KMHPatchMod.Settings;
            if (settings == null) return;

            float viewW = Mathf.Max(1f, inRect.width - UI.DialogLayout.ScrollbarReserveWidth);
            float now = Time.realtimeSinceStartup;
            if (_measuredW != viewW || now - _measuredAt > 0.25f)
            {
                UI.KmhStack measure = new UI.KmhStack(viewW, true);
                SettingsBody(measure, settings);
                _measuredY  = measure.Y;
                _measuredW  = viewW;
                _measuredAt = now;
            }

            Rect view = new Rect(0f, 0f, viewW, Mathf.Max(_measuredY + 8f, inRect.height));
            Widgets.BeginScrollView(inRect, ref _settingsScroll, view);
            SettingsBody(new UI.KmhStack(viewW, false), settings);
            Widgets.EndScrollView();
        }

        // Run twice per frame - measure, then draw - and both runs must take the same branches or the height is wrong.
        private static void SettingsBody(UI.KmhStack st, KMHPatchSettings settings)
        {
            st.Header(UI.KmhText.Get("KMH_Report_Area_General", "General"));
            st.Checkbox(UI.KmhText.Get("KMH_Settings_ShowWhatsNew", "Show Colony Nexus What's New after an update"), ref settings.ShowWhatsNewOnUpdate,
                UI.KmhText.Get("KMH_Settings_ShowWhatsNewTip",
                    "If enabled, the What's New changelog appears once after each new version. Reopen it any time from the Colony Nexus About panel."));
            st.Checkbox(UI.KmhText.Get("KMH_Settings_ShowWelcome", "Show the Colony Nexus welcome on first launch"), ref settings.ShowWelcomeOnLaunch,
                UI.KmhText.Get("KMH_Settings_ShowWelcomeTip",
                    "If enabled, the welcome appears the first time Colony Nexus starts. You can always reopen it from the Colony Nexus About panel."));
            if (st.Button(UI.KmhText.Get("KMH_Settings_WelcomeAgain", "Show the welcome again next launch")))
                settings.WelcomeSeen = false;

            st.Gap(6f);
            st.Header(UI.KmhText.Get("KMH_Settings_CommsHeader", "Colony Nexus Communications"));
            st.Checkbox(UI.KmhText.Get("KMH_Settings_MuteChat", "Mute all Colony Nexus chat notifications"), ref settings.ChatNotificationsMuted,
                UI.KmhText.Get("KMH_Settings_MuteChatTip",
                    "When on, no chat popups or sounds appear - the unread badge and tab glow still update. You can also toggle this from the Communications window."));
            NotifyRow(st, UI.KmhText.Get("KMH_Settings_ServerChat", "Server chat"),         () => settings.ChatNotifyServer, v => settings.ChatNotifyServer = v);
            NotifyRow(st, UI.KmhText.Get("KMH_Settings_GuildChat", "Guild chat"),           () => settings.ChatNotifyGuild,  v => settings.ChatNotifyGuild  = v);
            NotifyRow(st, UI.KmhText.Get("KMH_Settings_GlobalChat", "Global chat"),         () => settings.ChatNotifyGlobal, v => settings.ChatNotifyGlobal = v);
            NotifyRow(st, UI.KmhText.Get("KMH_Settings_DirectMessages", "Direct messages"), () => settings.ChatNotifyDm,     v => settings.ChatNotifyDm     = v);

            st.Gap(6f);
            st.Header(UI.KmhText.Get("KMH_Settings_DiscordHeader", "Discord status"));
            string discordWas = DiscordSettingsKey(settings);
            st.Checkbox(UI.KmhText.Get("KMH_Settings_DiscordPresence", "Show your Colony Nexus activity in Discord"), ref settings.DiscordPresenceEnabled,
                UI.KmhText.Get("KMH_Settings_DiscordPresenceTip",
                    "Replaces RimWorld Together's own 'Playing Multiplayer' with what you are doing - defending a raid, "
                    + "weathering a toxic fallout, out with a caravan - and the server you are on. Anyone who can see your Discord status can see this."));
            st.Checkbox(UI.KmhText.Get("KMH_Settings_DiscordGameplay", "Show what is happening in your colony"), ref settings.DiscordShowGameplay,
                UI.KmhText.Get("KMH_Settings_DiscordGameplayTip",
                    "Raids, disasters, caravans, research and ceremonies, worded generally. Never names, items, amounts or quest details."));
            if (st.ButtonLabeled(UI.KmhText.Get("KMH_Settings_DiscordDetail", "Activity detail"), DetailName(settings.DiscordDetailedActivity)))
                Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption>
                {
                    new FloatMenuOption(DetailName(true), () => { settings.DiscordDetailedActivity = true; KMHPatchMod.SaveSettings(); Features.Discord.KmhRichPresence.InvalidateNow(); }),
                    new FloatMenuOption(DetailName(false), () => { settings.DiscordDetailedActivity = false; KMHPatchMod.SaveSettings(); Features.Discord.KmhRichPresence.InvalidateNow(); }),
                }));
            st.Checkbox(UI.KmhText.Get("KMH_Settings_DiscordMultiplayer", "Show that you are playing multiplayer"), ref settings.DiscordShowMultiplayer,
                UI.KmhText.Get("KMH_Settings_DiscordMultiplayerTip",
                    "Off: a multiplayer game reads like any other colony, with no server, player count or Join."));
            st.Checkbox(UI.KmhText.Get("KMH_Settings_DiscordServerName", "Name public servers"), ref settings.DiscordShowServerName,
                UI.KmhText.Get("KMH_Settings_DiscordServerNameTip",
                    "A publicly listed server's name. Off shows 'Colony Nexus multiplayer' instead."));
            st.Checkbox(UI.KmhText.Get("KMH_Settings_DiscordColonyInfo", "Show colony size and day"), ref settings.DiscordShowColonyInfo,
                UI.KmhText.Get("KMH_Settings_DiscordColonyInfoTip", "Adds how many colonists you have and which day it is."));
            st.Checkbox(UI.KmhText.Get("KMH_Settings_DiscordElapsed", "Show time played"), ref settings.DiscordShowElapsed,
                UI.KmhText.Get("KMH_Settings_DiscordElapsedTip", "Discord's elapsed clock, restarting with each colony or server."));
            if (st.ButtonLabeled(UI.KmhText.Get("KMH_Settings_DiscordUpdate", "Update Discord at most every"),
                                 UI.KmhText.Get("KMH_Settings_DiscordSeconds", "{0} s", settings.DiscordUpdateSeconds)))
            {
                var every = new List<FloatMenuOption>();
                foreach (int sec in Features.Discord.KmhRichPresence.UpdateChoices)
                {
                    int chosen = sec;
                    every.Add(new FloatMenuOption(UI.KmhText.Get("KMH_Settings_DiscordSeconds", "{0} s", chosen),
                        () => { settings.DiscordUpdateSeconds = chosen; KMHPatchMod.SaveSettings(); }));
                }
                Find.WindowStack.Add(new FloatMenu(every));
            }
            st.Label(UI.KmhText.Get("KMH_Settings_DiscordUpdateHelp",
                         "A raid starting is sent within seconds whatever this says; everything else waits its turn."), new Color(0.72f, 0.72f, 0.72f));
            st.Checkbox(UI.KmhText.Get("KMH_Settings_DiscordAllowJoin", "Let friends join from your Discord status"), ref settings.AllowDiscordJoin,
                UI.KmhText.Get("KMH_Settings_DiscordAllowJoinTip",
                    "Adds a Join button, but only for a server that is publicly listed AND has no password. Joining "
                    + "always reconnects through your own Colony Nexus server list, never an address someone sent you. "
                    + "Registers one per-user entry so Discord can start RimWorld through Steam."));
            // Live: every reason a Join is missing is on this screen or the server, so say which.
            st.Label(UI.KmhText.Get("KMH_Settings_DiscordStatusLine", "Status: {0}", Features.Discord.KmhDiscordStatus.PresenceLine())
                   + "\n" + UI.KmhText.Get("KMH_Settings_DiscordJoinLine", "Join: {0}", Features.Discord.KmhDiscordStatus.JoinLine())
                   + "\n" + UI.KmhText.Get("KMH_Settings_DiscordArtLine", "Pictures: {0}", UI.KmhDisplayText.Inert(Features.Discord.KmhPresenceAssets.StatusLine())),
                     new Color(0.72f, 0.72f, 0.72f));
            st.Label(Features.Discord.KmhDiscordStatus.InviteHint(), new Color(0.72f, 0.72f, 0.72f));
            st.Checkbox(UI.KmhText.Get("KMH_Settings_DiscordNamePrivate", "Name private servers in your Discord status"), ref settings.DiscordShowPrivateServerName,
                UI.KmhText.Get("KMH_Settings_DiscordNamePrivateTip",
                    "Off by default: a server that is not publicly listed shows as 'On a private server'. Turn this on "
                    + "to show its name instead."));
            st.Checkbox(UI.KmhText.Get("KMH_Settings_DiscordActivity", "Show which Colony Nexus screen you are on"), ref settings.DiscordShowActivity,
                UI.KmhText.Get("KMH_Settings_DiscordActivityTip",
                    "Shows which Colony Nexus screen you are on - the marketplace, the auction house, the frontier - "
                    + "and nothing else. Never amounts, item names, other players or private names. Never on a private server."));
            settings.DiscordAppId = st.TextField(UI.KmhText.Get("KMH_Settings_DiscordAppId", "Discord application ID"), settings.DiscordAppId);
            st.Label(UI.KmhText.Get("KMH_Settings_DiscordAppIdHelp",
                         "Colony Nexus's own Discord application by default. Leave it blank and Colony Nexus sets no status at all - RimWorld "
                       + "Together's own is left exactly as it is. Only change it if you run your own Discord "
                       + "application with the Colony Nexus artwork uploaded to it."), new Color(0.72f, 0.72f, 0.72f));
            if (st.Button(UI.KmhText.Get("KMH_Settings_DiscordUseOwnApp", "Use the Colony Nexus application")))
                settings.DiscordAppId = KMHPatchSettings.DefaultDiscordAppId;
            // Only on a real change: pushing every frame the window is open would be Discord rate-limit abuse.
            if (DiscordSettingsKey(settings) != discordWas) Features.Discord.KmhRichPresence.InvalidateNow();

            if (st.Button(UI.KmhText.Get("KMH_Settings_ResetWindows", "Reset Colony Nexus window sizes and positions")))
            {
                settings.ResetWindowPlacement();
                KMHPatchMod.SaveSettings();
            }

            st.Label("<color=#A9AFB9>" + UI.KmhText.Get("KMH_Settings_VideoInfo",
                         "Video links are fetched and served by the Colony Nexus server you are on - nothing to install "
                       + "here. Quality is capped by that server.") + "</color>");

            long cached = Features.Chat.ChatVideoCache.Bytes();
            if (st.ButtonLabeled(UI.KmhText.Get("KMH_Settings_VideoKeep", "Keep watched videos"), $"{settings.VideoCacheGigabytes} GB"))
            {
                var sizes = new List<FloatMenuOption>();
                foreach (int gb in new[] { 1, 2, 5, 10, 20 })
                {
                    int chosen = gb;
                    sizes.Add(new FloatMenuOption($"{chosen} GB", () =>
                    {
                        settings.VideoCacheGigabytes = chosen;
                        KMHPatchMod.SaveSettings();
                        SweepVideoCache();
                    }));
                }
                Find.WindowStack.Add(new FloatMenu(sizes));
            }
            if (st.Button(UI.KmhText.Get("KMH_Settings_ClearVideoCache", "Clear video cache ({0} MB)", cached / (1024 * 1024))))
                Features.Chat.ChatVideoCache.Clear();

            if (st.ButtonLabeled(UI.KmhText.Get("KMH_Settings_VideoQuality", "Video quality"), QualityLabel(settings.VideoQuality)))
            {
                var options = new List<FloatMenuOption>();
                foreach (int h in new[] { 0, 360, 480, 720, 1080 })
                {
                    int choice = h;
                    options.Add(new FloatMenuOption(QualityLabel(choice), () =>
                    {
                        settings.VideoQuality = choice;
                        KMHPatchMod.SaveSettings();
                    }));
                }
                Find.WindowStack.Add(new FloatMenu(options));
            }

            bool wasAuto = settings.AutoLoadChatImages;
            // Two keys around the paragraph break, so no Keyed value has to carry a line break.
            st.Checkbox(UI.KmhText.Get("KMH_Settings_AutoLoadImages", "Load chat images automatically"), ref settings.AutoLoadChatImages,
                UI.KmhText.Get("KMH_Settings_AutoLoadImagesTip",
                    "Images and GIFs shared in chat load as soon as they appear. Off shows a placeholder naming the host " +
                    "until you click it.")
                + "\n\n" + UI.KmhText.Get("KMH_Settings_AutoLoadImagesPrivacy",
                    "Loading an image contacts that host directly, which tells it your IP address. " +
                    "Only hosts the server owner trusts (Discord, Giphy, Tenor, Imgur and the like) are ever offered."));
            // Turning it off should drop what it already pulled in, or the setting reads as decorative.
            if (wasAuto && !settings.AutoLoadChatImages) Features.Chat.ChatImageCache.Clear();

            st.Checkbox(UI.KmhText.Get("KMH_Settings_ChatOverlay", "Show new chat over the map"), ref settings.ChatOverlay,
                UI.KmhText.Get("KMH_Settings_ChatOverlayTip",
                    "New server, guild and Global Chat lines appear at the bottom left of the map for a few seconds while no chat " +
                    "window is open. They never block a click. Direct messages are left out."));
            if (st.ButtonLabeled(UI.KmhText.Get("KMH_Settings_ChatOpacity", "Chat window opacity"),
                                 Mathf.RoundToInt(KMHPatchSettings.ChatOpacity(settings) * 100f) + "%"))
                ChatOpacityMenu(settings);

            // The same three values the transport bar writes, so setting them in either place agrees with the other.
            bool  wasMuted = settings.ChatVideoMuted;
            bool  wasLoop  = settings.ChatVideoLoop;
            float wasVol   = settings.ChatVideoVolume;
            st.Checkbox(UI.KmhText.Get("KMH_Settings_MuteVideos", "Mute chat videos"), ref settings.ChatVideoMuted,
                UI.KmhText.Get("KMH_Settings_MuteVideosTip",
                    "Play videos silently. The volume below is remembered either way, so unmuting returns to it."));
            st.Checkbox(UI.KmhText.Get("KMH_Settings_RepeatVideos", "Repeat chat videos"), ref settings.ChatVideoLoop,
                UI.KmhText.Get("KMH_Settings_RepeatVideosTip",
                    "When on, a video restarts when it reaches the end instead of stopping on the last frame."));
            if (st.ButtonLabeled(UI.KmhText.Get("KMH_Settings_VideoVolume", "Chat video volume"),
                                 Mathf.RoundToInt(Mathf.Clamp01(settings.ChatVideoVolume) * 100f) + "%"))
                VideoVolumeMenu(settings);
            if (wasMuted != settings.ChatVideoMuted || wasLoop != settings.ChatVideoLoop
                || !Mathf.Approximately(wasVol, settings.ChatVideoVolume))
                Features.Chat.ChatVideoPlayer.SyncFromSettings();

            st.Gap(6f);
            st.Header(UI.KmhText.Get("KMH_Settings_ColoursHeader", "Colours"));

            st.Checkbox(UI.KmhText.Get("KMH_Settings_TagDiscord", "Tag Discord messages"), ref settings.ShowDiscordTag,
                UI.KmhText.Get("KMH_Settings_TagDiscordTip",
                    "Adds a compact [D] after the Discord mark on relayed messages - [D?] when the sender has no linked Colony Nexus " +
                    "account, so their name is only a Discord display name. The mark itself always shows."));
            st.Checkbox(UI.KmhText.Get("KMH_Settings_UseServerColours", "Use this server's colours"), ref settings.ThemeUseServer,
                UI.KmhText.Get("KMH_Settings_UseServerColoursTip",
                    "Server owners can set a small palette for the Colony Nexus accent and chat channels. Turn this off to use your own colours instead. " +
                    "Colours are decoration only - unread counts, warnings and the Discord mark are always labelled in text as well."));

            if (settings.ThemeUseServer)
            {
                st.Label("<color=#A9AFB9>" + UI.KmhText.Get("KMH_Settings_ShowingServerColours",
                             "Showing this server's colours (Colony Nexus defaults where the owner set none).") + "</color>");
            }
            else
            {
                st.Label("<color=#A9AFB9>" + UI.KmhText.Get("KMH_Settings_HexHelp",
                             "Six-digit hex, e.g. E2C16B. Blank uses the Colony Nexus default. Very dark values are lightened so text stays readable.")
                         + "</color>");
                settings.ThemeAccent     = st.ColorRow(UI.KmhText.Get("KMH_Settings_ColAccent", "Accent"),               settings.ThemeAccent,     UI.KmhTheme.Accent);
                settings.ThemeServerChat = st.ColorRow(UI.KmhText.Get("KMH_Settings_ServerChat", "Server chat"),         settings.ThemeServerChat, UI.KmhTheme.ServerChat);
                settings.ThemeGuildChat  = st.ColorRow(UI.KmhText.Get("KMH_Settings_GuildChat", "Guild chat"),           settings.ThemeGuildChat,  UI.KmhTheme.GuildChat);
                settings.ThemeDirectMsg  = st.ColorRow(UI.KmhText.Get("KMH_Settings_DirectMessages", "Direct messages"), settings.ThemeDirectMsg,  UI.KmhTheme.DirectMsg);
                settings.ThemeDiscord    = st.ColorRow(UI.KmhText.Get("KMH_Settings_ColDiscordMark", "Discord mark"),    settings.ThemeDiscord,    UI.KmhTheme.DiscordSrc);
                st.Gap(4f);
                st.Label("<color=#A9AFB9>" + UI.KmhText.Get("KMH_Settings_ChatText", "Chat text") + "</color>");
                settings.ThemeNameNormal  = st.ColorRow(UI.KmhText.Get("KMH_Settings_ColPlayerName", "Player name"),   settings.ThemeNameNormal,  UI.KmhTheme.NameNormal);
                settings.ThemeTextNormal  = st.ColorRow(UI.KmhText.Get("KMH_Settings_ColMessageText", "Message text"), settings.ThemeTextNormal,  UI.KmhTheme.TextNormal);
                settings.ThemeNameDiscord = st.ColorRow(UI.KmhText.Get("KMH_Settings_ColDiscordName", "Discord name"), settings.ThemeNameDiscord, UI.KmhTheme.NameDiscord);
                settings.ThemeTextDiscord = st.ColorRow(UI.KmhText.Get("KMH_Settings_ColDiscordText", "Discord text"), settings.ThemeTextDiscord, UI.KmhTheme.TextDiscord);

                // Named individually: a player who mistyped one field should not have to re-check five.
                string bad = InvalidColourList(settings);
                if (bad != null)
                    st.Label("<color=" + UI.KmhTheme.Hex(UI.KmhTheme.Warning) + ">" + UI.KmhText.Get("KMH_Settings_BadHex",
                                 "Not a six-digit hex colour: {0}. The Colony Nexus default is being used for these.", bad) + "</color>");

                if (st.Button(UI.KmhText.Get("KMH_Settings_ClearColours", "Clear my colours")))
                {
                    settings.ThemeAccent = settings.ThemeServerChat = settings.ThemeGuildChat =
                        settings.ThemeDirectMsg = settings.ThemeDiscord = settings.ThemeNameNormal =
                        settings.ThemeTextNormal = settings.ThemeNameDiscord = settings.ThemeTextDiscord = "";
                    KMHPatchMod.SaveSettings();
                }
            }

            st.Gap(6f);
            st.Header(UI.KmhText.Get("KMH_Settings_MarkersHeader", "World map markers"));
            st.Checkbox(UI.KmhText.Get("KMH_Settings_MarkerOutline", "Outline Colony Nexus places on the world map"), ref settings.MarkerShowRim,
                UI.KmhText.Get("KMH_Settings_MarkerOutlineTip",
                    "Draws the same coloured border RimWorld puts on a settlement you own, around Colony Nexus sites, outposts and " +
                    "your guild hall. The border is the only colour applied - the icon itself always keeps its own artwork."));

            if (Features.Sites.KmhMarkerColors.Enabled)
            {
                st.Checkbox(UI.KmhText.Get("KMH_Settings_UseServerMarkers", "Use this server's marker colours"), ref settings.MarkerUseServer,
                    UI.KmhText.Get("KMH_Settings_UseServerMarkersTip",
                        "Server owners can suggest which colours mark your own places and other players'. Turn this off to pick your own. " +
                        "Outpost states - claimable, hostile, derelict - always keep the Colony Nexus colours so a warning cannot be styled into something calm."));

                if (settings.MarkerUseServer)
                {
                    st.Label("<color=#A9AFB9>" + UI.KmhText.Get("KMH_Settings_ShowingServerColours",
                                 "Showing this server's colours (Colony Nexus defaults where the owner set none).") + "</color>");
                }
                else
                {
                    st.Label("<color=#A9AFB9>" + UI.KmhText.Get("KMH_Settings_MarkerHexHelp",
                                 "Six-digit hex, e.g. 6BE26B. Blank uses the Colony Nexus default.") + "</color>");
                    settings.MarkerMine   = st.ColorRow(UI.KmhText.Get("KMH_Settings_MarkerYours", "Yours"),
                                                        settings.MarkerMine,   Features.Sites.KmhMarkerColors.Mine);
                    settings.MarkerTheirs = st.ColorRow(UI.KmhText.Get("KMH_Settings_MarkerOthers", "Other players"),
                                                        settings.MarkerTheirs, Features.Sites.KmhMarkerColors.Theirs);

                    string badMarker = InvalidMarkerColourList(settings);
                    if (badMarker != null)
                        st.Label("<color=" + UI.KmhTheme.Hex(UI.KmhTheme.Warning) + ">" + UI.KmhText.Get("KMH_Settings_BadHex",
                                     "Not a six-digit hex colour: {0}. The Colony Nexus default is being used for these.", badMarker) + "</color>");

                    if (st.Button(UI.KmhText.Get("KMH_Settings_ClearMarkerColours", "Clear my marker colours")))
                    {
                        settings.MarkerMine = settings.MarkerTheirs = "";
                        KMHPatchMod.SaveSettings();
                    }
                }
            }

            st.Gap(6f);
            st.Header(UI.KmhText.Get("KMH_Settings_DiagnosticsHeader", "Diagnostics"));
            if (st.Button(UI.KmhText.Get("KMH_Settings_OpenLogFolder", "Open Colony Nexus log folder"))) Application.OpenURL(KmhLog.LogFolderPath);

            bool prevDebug = settings.DebugLogging;
            st.Checkbox(UI.KmhText.Get("KMH_Settings_VerboseLogging", "Verbose debug logging"), ref settings.DebugLogging,
                UI.KmhText.Get("KMH_Settings_VerboseLoggingTip",
                    "Logs detailed Colony Nexus diagnostics. Off by default so normal play stays quiet. Each session writes its own "
                    + "timestamped file under Colony Nexus/Logs, rotated at 10 MB and kept for 14 days."));
            if (prevDebug != settings.DebugLogging) KmhLog.DebugEnabled = settings.DebugLogging;

            bool prevProto = settings.ProtocolLogging;
            st.Checkbox(UI.KmhText.Get("KMH_Settings_ProtocolLogging", "Also log every packet (protocol tracing)"), ref settings.ProtocolLogging,
                UI.KmhText.Get("KMH_Settings_ProtocolLoggingTip",
                    "Adds a line for every packet, including the 15-second heartbeat. Only turn this on when asked for it - "
                    + "it produces megabytes over a long session. With it off, heartbeats are summarised once an hour."));
            if (prevProto != settings.ProtocolLogging) KmhLog.ProtocolEnabled = settings.ProtocolLogging;

            int mode = settings.LogSharingMode;
            string modeName = mode == KMHPatchSettings.LogShareNever     ? UI.KmhText.Get("KMH_Settings_LogShareNever", "Never")
                            : mode == KMHPatchSettings.LogShareAutomatic ? UI.KmhText.Get("KMH_Settings_LogShareAuto", "Automatically share")
                            :                                              UI.KmhText.Get("KMH_Settings_LogShareAsk", "Ask me each server");
            if (st.ButtonLabeled(UI.KmhText.Get("KMH_Settings_LogShareLabel", "When a server asks for my log"), modeName))
                Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption>
                {
                    new FloatMenuOption(UI.KmhText.Get("KMH_Settings_LogShareNeverOption", "Never - refuse without asking me"),
                        () => { settings.LogSharingMode = KMHPatchSettings.LogShareNever; settings.ShareDebugLogsWithServer = false; KMHPatchMod.SaveSettings(); }),
                    new FloatMenuOption(UI.KmhText.Get("KMH_Settings_LogShareAskOption", "Ask me each server (recommended)"),
                        () => { settings.LogSharingMode = KMHPatchSettings.LogShareAsk; settings.ShareDebugLogsWithServer = false; KMHPatchMod.SaveSettings(); }),
                    new FloatMenuOption(UI.KmhText.Get("KMH_Settings_LogShareAutoOption", "Automatically share with any server that asks"),
                        () => { settings.LogSharingMode = KMHPatchSettings.LogShareAutomatic; settings.ShareDebugLogsWithServer = true; KMHPatchMod.SaveSettings(); }),
                }));
            st.Label("<color=#A9AFB9>" + (mode == KMHPatchSettings.LogShareNever
                ? UI.KmhText.Get("KMH_Settings_LogShareNeverHelp", "Nothing is ever sent, and you are not prompted.")
                : mode == KMHPatchSettings.LogShareAutomatic
                    ? UI.KmhText.Get("KMH_Settings_LogShareAutoHelp",
                        "Sharing starts as soon as a server asks. It ends when you disconnect, or when you stop it here.")
                    : UI.KmhText.Get("KMH_Settings_LogShareAskHelp",
                        "You get one prompt per server, per session. Nothing is sent unless you say yes, and that answer is dropped when you disconnect."))
                + "</color>");
            st.Label("<color=#A9AFB9>" + UI.KmhText.Get("KMH_Settings_LogShareScope",
                         "Colony Nexus log lines only - never chat, saves, or hardware/system information.") + "</color>");

            st.Gap(6f);
            st.Header(UI.KmhText.Get("KMH_Settings_GlobalHeader", "Colony Nexus Global"));
            string wasGlobal = settings.GlobalBaseUrl ?? "";
            bool playtest = Features.Global.GlobalHttp.IsPlaytestBuild;
            settings.GlobalBaseUrl = st.TextField(playtest
                ? UI.KmhText.Get("KMH_Settings_DirectoryAddressPlaytest", "Directory address (blank = the playtest directory)")
                : UI.KmhText.Get("KMH_Settings_DirectoryAddress", "Directory address (blank = the official one)"), wasGlobal);
            if ((settings.GlobalBaseUrl ?? "") != wasGlobal)
            {
                Features.Global.GlobalHttp.BaseUrl = Features.Global.GlobalHttp.Normalize(settings.GlobalBaseUrl);
                Features.Global.KmhDirectoryClient.ForgetDirectory();
                KMHPatchMod.SaveSettings();
            }
            if (Features.Global.GlobalHttp.IsOfficial)
                st.Label(UI.KmhText.Get("KMH_Settings_DirectoryOfficial", "Browsing the official Colony Nexus directory."));
            else if (playtest && Features.Global.GlobalHttp.BaseUrl == Features.Global.GlobalHttp.DefaultBaseUrl)
                st.Label(UI.KmhText.Get("KMH_Settings_DirectoryPlaytest",
                    "Playtest build: browsing the playtest directory, {0}. The full release uses the official one.",
                    Features.Global.GlobalHttp.BaseUrl));
            else
                st.Label(playtest
                    ? UI.KmhText.Get("KMH_Settings_DirectoryCustomPlaytest", "Browsing {0} - set this back to blank for the playtest directory.",
                                     Features.Global.GlobalHttp.BaseUrl)
                    : UI.KmhText.Get("KMH_Settings_DirectoryCustom", "Browsing {0} - set this back to blank for the official one.",
                                     Features.Global.GlobalHttp.BaseUrl));

            st.Gap(6f);
            st.Header(UI.KmhText.Get("KMH_Settings_TransportHeader", "Colony Nexus API transport"));
            st.Checkbox(UI.KmhText.Get("KMH_Settings_UseApiTransport", "Use Colony Nexus API transport"), ref settings.UseKmhApiTransport,
                UI.KmhText.Get("KMH_Settings_UseApiTransportTip",
                    "On by default - the recommended path for newer RWT builds. Falls back to chat if the port can't be reached. " +
                    "Reconnect for changes to take effect."));
            st.Checkbox(UI.KmhText.Get("KMH_Settings_AllowChatFallback", "Allow chat fallback"), ref settings.AllowChatTransportFallback,
                UI.KmhText.Get("KMH_Settings_AllowChatFallbackTip",
                    "If the Colony Nexus API port can't be reached, keep using the RWT-chat transport so Colony Nexus still works."));

            // Named for what they actually are: the server's advertised endpoint beats both of these every time.
            string portText = st.TextField(UI.KmhText.Get("KMH_Settings_FallbackPort", "Fallback port (only if a server advertises none)"),
                                           settings.KmhApiPort.ToString());
            if (int.TryParse(portText, out int newPort) && newPort >= 1 && newPort <= 65535) settings.KmhApiPort = newPort;
            settings.KmhApiHostOverride = st.TextField(UI.KmhText.Get("KMH_Settings_HostOverride", "Host override, every server (blank = the one you joined)"),
                                                       settings.KmhApiHostOverride ?? "");

            string effective = SubProtocol.KmhApiClient.EffectiveEndpoint;
            if (effective.Length > 0) st.Label(UI.KmhText.Get("KMH_Settings_ConnectedTo", "Currently connected to: {0}", effective));
            else if (!string.IsNullOrEmpty(SubProtocol.KmhTransport.DegradedReason))
                st.Label(UI.KmhText.Get("KMH_Settings_LastAttemptFailed", "Last attempt failed: {0}", SubProtocol.KmhTransport.DegradedReason));

            st.Gap(6f);
            st.Header(UI.KmhText.Get("KMH_Settings_EnforcementHeader", "Server config enforcement"));
            if (Features.Enforcement.EnforcementProfileApplier.IsApplied)
            {
                st.Label("<color=#f0c04f>" + UI.KmhText.Get("KMH_Settings_EnforcementApplied",
                             "A server config profile is applied to your Config folder. Restoring puts your personal configs back " +
                             "(it re-applies if you rejoin an enforcing server).") + "</color>");
                if (st.Button(UI.KmhText.Get("KMH_Settings_RestoreConfigs", "Restore my original configs")))
                {
                    if (Features.Enforcement.EnforcementProfileApplier.Restore("ExplicitUserRestore"))
                        Messages.Message(UI.KmhText.Get("KMH_Settings_RestoredConfigs",
                                             "Colony Nexus: restored your personal config files. Restart RimWorld to load them."),
                                         RimWorld.MessageTypeDefOf.PositiveEvent, false);
                }
            }
            else st.Label("<color=#A9AFB9>" + UI.KmhText.Get("KMH_Settings_NoEnforcement", "No server config profile is applied.") + "</color>");
            if (st.Button(UI.KmhText.Get("KMH_Settings_RecoverModList", "Recover an older mod list…")))
                Find.WindowStack.Add(new Features.Mods.Dialog_KMHRecoverModList(""));

            st.Gap(6f);
            st.Header(UI.KmhText.Get("KMH_Menu_About", "About"));
            st.Label(UI.KmhText.Get("KMH_Settings_Version", "Version: {0} ({1})",
                                    OwnName.Version, OwnName.Name));
            st.Label(UI.KmhText.Get("KMH_Settings_Package", "Package: {0}", Constants.PackageId));
        }

        private static System.Reflection.AssemblyName _ownName;
        private static System.Reflection.AssemblyName OwnName => _ownName ?? (_ownName = typeof(KmhEntry).Assembly.GetName());
    }
}
