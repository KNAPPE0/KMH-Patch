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
            KmhLog.DebugEnabled    = KMHPatchMod.Settings?.DebugLogging ?? false;   // payload wires the loader's setting
            KmhLog.ProtocolEnabled = KMHPatchMod.Settings?.ProtocolLogging ?? false;
            KmhLog.Info($"Bootstrap starting ({typeof(KmhEntry).Assembly.GetName().Name} {typeof(KmhEntry).Assembly.GetName().Version})");
            KmhLog.Info($"KMH-Patch UI build {SubProtocol.KmhProtocol.DisplayVersion}·{SubProtocol.KmhProtocol.UiBuildTag} loaded. If the Guild Hall title doesn't show this tag, the game is loading an OLDER KMH-Patch DLL.");

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
            SafeRun("chat",         Features.Chat.ChatHandler.Register);
            SafeRun("comms-notify", Features.Chat.KmhCommsNotifications.Register);
            SafeRun("seasons",      Features.Seasons.SeasonHandler.Register);
            SafeRun("reputation",   Features.Reputation.ReputationCache.Register);
            SafeRun("notifications", Features.OfflineNotices.NotificationHandler.Register);
            SafeRun("enforcement",  Features.Enforcement.EnforcementHandler.Register);
            SafeRun("extensions",   Extensibility.ExtensionLoader.DiscoverAndLoad);
            SafeRun("video-cache",  SweepVideoCache);   // a cache left by an old session is swept on the way in
            // IKmhClientExtension promises Shutdown() on a clean quit; Unity raises this only on a graceful exit.
            SafeRun("extension-shutdown", () =>
                UnityEngine.Application.quitting += Extensibility.ExtensionLoader.ShutdownAll);

            KMHPatchMod.SettingsDrawer = DrawSettings;

            // Harmony IL generation is the expensive part and matters to nothing before the main menu.
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

            // By name, outside the attribute loop: a type missing on this RWT build would trip the scan otherwise.
            Patch_DLG_GuildList_KmhRedirect.TryApply(HarmonyInstance);
            Patch_DLG_Leaderboard_KmhRedirect.TryApply(HarmonyInstance);

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
            if (height <= 0) return $"Server default ({Features.Chat.ChatYouTube.ServerMaxHeight}p)";
            return height + "p";
        }

        // Blank means "use the layer below", so a bad value only falls through - this exists to say why nothing changed.
        internal static string InvalidColourList(KMHPatchSettings s)
        {
            if (s == null) return null;
            List<string> bad = new List<string>();
            void Check(string label, string hex) { if (!string.IsNullOrWhiteSpace(hex) && !UI.KmhTheme.TryHex(hex, out _)) bad.Add(label); }

            Check("Accent",          s.ThemeAccent);
            Check("Server chat",     s.ThemeServerChat);
            Check("Guild chat",      s.ThemeGuildChat);
            Check("Direct messages", s.ThemeDirectMsg);
            Check("Discord mark",    s.ThemeDiscord);
            Check("Player name",     s.ThemeNameNormal);
            Check("Message text",    s.ThemeTextNormal);
            Check("Discord name",    s.ThemeNameDiscord);
            Check("Discord text",    s.ThemeTextDiscord);

            return bad.Count == 0 ? null : string.Join(", ", bad.ToArray());
        }

        internal static string InvalidMarkerColourList(KMHPatchSettings s)
        {
            if (s == null) return null;
            List<string> bad = new List<string>();
            void Check(string label, string hex) { if (!string.IsNullOrWhiteSpace(hex) && !UI.KmhTheme.TryHex(hex, out _)) bad.Add(label); }

            Check("Yours",         s.MarkerMine);
            Check("Other players", s.MarkerTheirs);

            return bad.Count == 0 ? null : string.Join(", ", bad.ToArray());
        }

        private static string NotifyName(int level)
        {
            switch (level)
            {
                case 0:  return "Silent (badge only)";
                case 2:  return "Popup + sound";
                default: return "Popup";
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
            st.Header("General");
            st.Checkbox("Show KMH What's New on launch", ref settings.ShowWhatsNewOnUpdate,
                "If enabled, the What's New changelog appears every launch. Turn it off to stop the popup; reopen it any time from the KMH tab.");
            st.Checkbox("Show KMH welcome / help popup", ref settings.ShowWelcomeOnLaunch,
                "If enabled, the KMH welcome dialog appears on launch (once per launch). You can always reopen it from the KMH About panel.");
            if (st.Button("Show welcome dialog next time the main menu loads"))
                Patch_MainMenuDrawer_MainMenuOnGUI.ResetShownFlag();

            st.Gap(6f);
            st.Header("KMH Communications");
            st.Checkbox("Mute all KMH chat notifications", ref settings.ChatNotificationsMuted,
                "When on, no chat popups or sounds appear - the unread badge and tab glow still update. You can also toggle this from the Communications window.");
            NotifyRow(st, "Server chat",     () => settings.ChatNotifyServer, v => settings.ChatNotifyServer = v);
            NotifyRow(st, "Guild chat",      () => settings.ChatNotifyGuild,  v => settings.ChatNotifyGuild  = v);
            NotifyRow(st, "Direct messages", () => settings.ChatNotifyDm,     v => settings.ChatNotifyDm     = v);

            if (st.Button("Reset KMH window sizes and positions"))
            {
                settings.ResetWindowPlacement();
                KMHPatchMod.SaveSettings();
            }

            st.Label("<color=grey>Video links are fetched and served by the KMH server you are on - nothing to install "
                     + "here. Quality is capped by that server.</color>");

            long cached = Features.Chat.ChatVideoCache.Bytes();
            if (st.ButtonLabeled("Keep watched videos", $"{settings.VideoCacheGigabytes} GB"))
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
            if (st.Button($"Clear video cache ({cached / (1024 * 1024)} MB)")) Features.Chat.ChatVideoCache.Clear();

            if (st.ButtonLabeled("Video quality", QualityLabel(settings.VideoQuality)))
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
            st.Checkbox("Load chat images automatically", ref settings.AutoLoadChatImages,
                "Images shared in chat show as a placeholder naming the host until you click one. Turning this on " +
                "loads them without asking.\n\nLoading an image contacts that host directly, which tells it your IP " +
                "address. KMH never loads one on its own, and the server owner decides which hosts are allowed at all.");
            // Turning it off should drop what it already pulled in, or the setting reads as decorative.
            if (wasAuto && !settings.AutoLoadChatImages) Features.Chat.ChatImageCache.Clear();

            // The same three values the transport bar writes, so setting them in either place agrees with the other.
            bool  wasMuted = settings.ChatVideoMuted;
            bool  wasLoop  = settings.ChatVideoLoop;
            float wasVol   = settings.ChatVideoVolume;
            st.Checkbox("Mute chat videos", ref settings.ChatVideoMuted,
                "Play videos silently. The volume below is remembered either way, so unmuting returns to it.");
            st.Checkbox("Repeat chat videos", ref settings.ChatVideoLoop,
                "When on, a video restarts when it reaches the end instead of stopping on the last frame.");
            if (st.ButtonLabeled("Chat video volume", Mathf.RoundToInt(Mathf.Clamp01(settings.ChatVideoVolume) * 100f) + "%"))
                VideoVolumeMenu(settings);
            if (wasMuted != settings.ChatVideoMuted || wasLoop != settings.ChatVideoLoop
                || !Mathf.Approximately(wasVol, settings.ChatVideoVolume))
                Features.Chat.ChatVideoPlayer.SyncFromSettings();

            st.Gap(6f);
            st.Header("Colours");

            st.Checkbox("Tag Discord messages", ref settings.ShowDiscordTag,
                "Adds a compact [D] after the Discord mark on relayed messages - [D?] when the sender has no linked KMH " +
                "account, so their name is only a Discord display name. The mark itself always shows.");
            st.Checkbox("Use this server's colours", ref settings.ThemeUseServer,
                "Server owners can set a small palette for KMH's accent and chat channels. Turn this off to use your own colours instead. " +
                "Colours are decoration only - unread counts, warnings and the Discord mark are always labelled in text as well.");

            if (settings.ThemeUseServer)
            {
                st.Label("<color=grey>Showing this server's colours (KMH's own where the owner set none).</color>");
            }
            else
            {
                st.Label("<color=grey>Six-digit hex, e.g. E2C16B. Blank uses KMH's colour. Very dark values are lightened so text stays readable.</color>");
                settings.ThemeAccent     = st.ColorRow("Accent",          settings.ThemeAccent,     UI.KmhTheme.Accent);
                settings.ThemeServerChat = st.ColorRow("Server chat",     settings.ThemeServerChat, UI.KmhTheme.ServerChat);
                settings.ThemeGuildChat  = st.ColorRow("Guild chat",      settings.ThemeGuildChat,  UI.KmhTheme.GuildChat);
                settings.ThemeDirectMsg  = st.ColorRow("Direct messages", settings.ThemeDirectMsg,  UI.KmhTheme.DirectMsg);
                settings.ThemeDiscord    = st.ColorRow("Discord mark",    settings.ThemeDiscord,    UI.KmhTheme.DiscordSrc);
                st.Gap(4f);
                st.Label("<color=grey>Chat text</color>");
                settings.ThemeNameNormal  = st.ColorRow("Player name",     settings.ThemeNameNormal,  UI.KmhTheme.NameNormal);
                settings.ThemeTextNormal  = st.ColorRow("Message text",    settings.ThemeTextNormal,  UI.KmhTheme.TextNormal);
                settings.ThemeNameDiscord = st.ColorRow("Discord name",    settings.ThemeNameDiscord, UI.KmhTheme.NameDiscord);
                settings.ThemeTextDiscord = st.ColorRow("Discord text",    settings.ThemeTextDiscord, UI.KmhTheme.TextDiscord);

                // Named individually: a player who mistyped one field should not have to re-check five.
                string bad = InvalidColourList(settings);
                if (bad != null)
                    st.Label($"<color={UI.KmhTheme.Hex(UI.KmhTheme.Warning)}>Not a six-digit hex colour: {bad}. "
                           + "KMH's own colour is being used for these.</color>");

                if (st.Button("Clear my colours"))
                {
                    settings.ThemeAccent = settings.ThemeServerChat = settings.ThemeGuildChat =
                        settings.ThemeDirectMsg = settings.ThemeDiscord = settings.ThemeNameNormal =
                        settings.ThemeTextNormal = settings.ThemeNameDiscord = settings.ThemeTextDiscord = "";
                    KMHPatchMod.SaveSettings();
                }
            }

            st.Gap(6f);
            st.Header("World map markers");
            st.Checkbox("Outline KMH places on the world map", ref settings.MarkerShowRim,
                "Draws the same coloured border RimWorld puts on a settlement you own, around KMH sites, outposts and " +
                "your guild hall. The border is the only colour applied - the icon itself always keeps its own artwork.");

            if (Features.Sites.KmhMarkerColors.Enabled)
            {
                st.Checkbox("Use this server's marker colours", ref settings.MarkerUseServer,
                    "Server owners can suggest which colours mark your own places and other players'. Turn this off to pick your own. " +
                    "Outpost states - claimable, hostile, derelict - always keep KMH's colours so a warning cannot be styled into something calm.");

                if (settings.MarkerUseServer)
                {
                    st.Label("<color=grey>Showing this server's colours (KMH's own where the owner set none).</color>");
                }
                else
                {
                    st.Label("<color=grey>Six-digit hex, e.g. 6BE26B. Blank uses KMH's colour.</color>");
                    settings.MarkerMine   = st.ColorRow("Yours",         settings.MarkerMine,   Features.Sites.KmhMarkerColors.Mine);
                    settings.MarkerTheirs = st.ColorRow("Other players", settings.MarkerTheirs, Features.Sites.KmhMarkerColors.Theirs);

                    string badMarker = InvalidMarkerColourList(settings);
                    if (badMarker != null)
                        st.Label($"<color={UI.KmhTheme.Hex(UI.KmhTheme.Warning)}>Not a six-digit hex colour: {badMarker}. "
                               + "KMH's own colour is being used for these.</color>");

                    if (st.Button("Clear my marker colours"))
                    {
                        settings.MarkerMine = settings.MarkerTheirs = "";
                        KMHPatchMod.SaveSettings();
                    }
                }
            }

            st.Gap(6f);
            st.Header("Diagnostics");
            if (st.Button("Open KMH log folder")) Application.OpenURL(KmhLog.LogFolderPath);

            bool prevDebug = settings.DebugLogging;
            st.Checkbox("Verbose debug logging", ref settings.DebugLogging,
                "Logs detailed KMH diagnostics. Off by default so normal play stays quiet. Each session writes its own "
                + "timestamped file under KMH-Patch/Logs, rotated at 10 MB and kept for 14 days.");
            if (prevDebug != settings.DebugLogging) KmhLog.DebugEnabled = settings.DebugLogging;

            bool prevProto = settings.ProtocolLogging;
            st.Checkbox("Also log every packet (protocol tracing)", ref settings.ProtocolLogging,
                "Adds a line for every packet, including the 15-second heartbeat. Only turn this on when asked for it - "
                + "it produces megabytes over a long session. With it off, heartbeats are summarised once an hour.");
            if (prevProto != settings.ProtocolLogging) KmhLog.ProtocolEnabled = settings.ProtocolLogging;

            int mode = settings.LogSharingMode;
            string modeName = mode == KMHPatchSettings.LogShareNever     ? "Never"
                            : mode == KMHPatchSettings.LogShareAutomatic ? "Automatically share"
                            :                                              "Ask me each server";
            if (st.ButtonLabeled("When a server asks for my log", modeName))
                Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption>
                {
                    new FloatMenuOption("Never - refuse without asking me",
                        () => { settings.LogSharingMode = KMHPatchSettings.LogShareNever; settings.ShareDebugLogsWithServer = false; KMHPatchMod.SaveSettings(); }),
                    new FloatMenuOption("Ask me each server (recommended)",
                        () => { settings.LogSharingMode = KMHPatchSettings.LogShareAsk; settings.ShareDebugLogsWithServer = false; KMHPatchMod.SaveSettings(); }),
                    new FloatMenuOption("Automatically share with any server that asks",
                        () => { settings.LogSharingMode = KMHPatchSettings.LogShareAutomatic; settings.ShareDebugLogsWithServer = true; KMHPatchMod.SaveSettings(); }),
                }));
            st.Label(mode == KMHPatchSettings.LogShareNever
                ? "<color=grey>Nothing is ever sent, and you are not prompted.</color>"
                : mode == KMHPatchSettings.LogShareAutomatic
                    ? "<color=grey>Sharing starts as soon as a server asks. It ends when you disconnect, or when you stop it here.</color>"
                    : "<color=grey>You get one prompt per server, per session. Nothing is sent unless you say yes, and that answer is dropped when you disconnect.</color>");
            st.Label("<color=grey>KMH log lines only - never chat, saves, or hardware/system information.</color>");

            st.Gap(6f);
            st.Header("KMH API transport");
            st.Checkbox("Use KMH API transport", ref settings.UseKmhApiTransport,
                "On by default - the recommended path for newer RWT builds. Falls back to chat if the port can't be reached. " +
                "Reconnect for changes to take effect.");
            st.Checkbox("Allow chat fallback", ref settings.AllowChatTransportFallback,
                "If the KMH API port can't be reached, keep using the RWT-chat transport so KMH still works.");

            // Named for what they actually are: the server's advertised endpoint beats both of these every time.
            string portText = st.TextField("Fallback port (only if a server advertises none)", settings.KmhApiPort.ToString());
            if (int.TryParse(portText, out int newPort) && newPort >= 1 && newPort <= 65535) settings.KmhApiPort = newPort;
            settings.KmhApiHostOverride = st.TextField("Host override, every server (blank = the one you joined)", settings.KmhApiHostOverride ?? "");

            string effective = SubProtocol.KmhApiClient.EffectiveEndpoint;
            if (effective.Length > 0) st.Label($"Currently connected to: {effective}");
            else if (!string.IsNullOrEmpty(SubProtocol.KmhTransport.DegradedReason))
                st.Label($"Last attempt failed: {SubProtocol.KmhTransport.DegradedReason}");

            st.Gap(6f);
            st.Header("Server config enforcement");
            if (Features.Enforcement.EnforcementProfileApplier.IsApplied)
            {
                st.Label("<color=#ffce4d>A server config profile is applied to your Config folder. Restoring puts your personal configs back " +
                         "(it re-applies if you rejoin an enforcing server).</color>");
                if (st.Button("Restore my original configs"))
                {
                    Features.Enforcement.EnforcementProfileApplier.Restore();
                    Messages.Message("KMH: restored your personal configs.", RimWorld.MessageTypeDefOf.PositiveEvent, false);
                }
            }
            else st.Label("<color=grey>No server config profile is applied.</color>");

            st.Gap(6f);
            st.Header("About");
            st.Label($"Version: {typeof(KmhEntry).Assembly.GetName().Version} ({typeof(KmhEntry).Assembly.GetName().Name})");
            st.Label($"Package: {Constants.PackageId}");
        }
    }
}
