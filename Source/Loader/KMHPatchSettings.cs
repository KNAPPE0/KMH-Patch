using Verse;

namespace KMHPatch
{
    // Every field needs a Scribe_Values.Look in ExposeData(), or it silently stops persisting.
    public class KMHPatchSettings : ModSettings
    {
        public bool ShowWelcomeOnLaunch = true;

        public bool ShowWhatsNewOnUpdate = true;

        public string WhatsNewSeen = "";
        public bool WelcomeSeen = false;

        public bool DebugLogging = false;

        // Separate from Debug on purpose: useful logging must not also write megabytes of packet chatter.
        public bool ProtocolLogging = false;

        // Standing opt-in to share log lines: a server can request, never enable; off means it must ask each session.
        public bool ShareDebugLogsWithServer = false;

        public const int LogShareNever     = 0;
        public const int LogShareAsk       = 1;
        public const int LogShareAutomatic = 2;

        public int LogSharingMode = LogShareAsk;

        // Presentation only. Blank = use the layer below, so a player sees the owner's theme until they opt out.
        public bool   ThemeUseServer  = true;
        public string ThemeAccent     = "";
        public string ThemeServerChat = "";
        public string ThemeGuildChat  = "";
        public string ThemeDirectMsg  = "";
        public string ThemeDiscord    = "";
        public string ThemeNameNormal  = "";
        public string ThemeTextNormal  = "";
        public string ThemeNameDiscord = "";
        public string ThemeTextDiscord = "";

        // Blank hex = take the server's suggestion, then KMH's own.
        public bool   MarkerShowRim    = true;
        public bool   MarkerUseServer  = true;
        public string MarkerMine       = "";
        public string MarkerTheirs     = "";

        // Only the [D] tag is switchable; the marker glyph must stay, as a cue that survives without colour.
        public bool   ShowDiscordTag  = true;

        // Only hosts the server owner trusts are ever offered; off asks per image.
        public bool   AutoLoadChatImages = true;

        public bool   ChatOverlay = true;

        // The chat windows' background; contents stay solid. Below MinChatOpacity the text loses its backdrop.
        public float  ChatWindowOpacity = 1f;
        public const float MinChatOpacity = 0.3f;

        public static float ChatOpacity(KMHPatchSettings s)
            => UnityEngine.Mathf.Clamp(s?.ChatWindowOpacity ?? 1f, MinChatOpacity, 1f);

        public static float NextChatOpacity(float current)
            => current > 0.9f ? 0.8f : current > 0.7f ? 0.6f : current > 0.5f ? 0.4f : 1f;

        // Not muted by default: a video only plays because the player clicked it, so silence would just be a second click.
        public bool   ChatVideoMuted  = false;
        public float  ChatVideoVolume = 0.6f;
        public bool   ChatVideoLoop   = true;

        // Blank is the official directory; self-hosters and local test instances point it elsewhere.
        public string GlobalBaseUrl = "";
        public bool   AskToLinkSteam = true;

        // Dials only when the server advertises it; the chat fallback keeps older servers working.
        public bool   UseKmhApiTransport         = true;
        public int    KmhApiPort                 = 5099;
        public bool   AllowChatTransportFallback = true;
        public string KmhApiHostOverride         = "";   // blank = same host as the RWT server connection

        // On by default like other multiplayer games, though it tells Discord contacts which server you are on.
        public bool   DiscordPresenceEnabled = true;
        // A default, not a constant: Scribe stores an explicit "", so a typed or deliberately blanked id survives.
        // Blank means KMH sets no presence and leaves RWT's own alone.
        public const string DefaultDiscordAppId = "1459619253010563327";
        public string DiscordAppId = DefaultDiscordAppId;
        // Even on, a join is advertised only for a publicly listed server without a password.
        public bool   AllowDiscordJoin = true;
        // A server absent from the public directory is private as far as KMH can tell; its name is not KMH's to broadcast.
        public bool   DiscordShowPrivateServerName = false;
        // On, like the rest of the activity: screen names, raids and colony size name nothing personal.
        public bool   DiscordShowActivity = true;
        public bool   DiscordShowGameplay = true;
        public bool   DiscordDetailedActivity = true;
        public bool   DiscordShowMultiplayer = true;
        public bool   DiscordShowServerName = true;
        public bool   DiscordShowColonyInfo = true;
        public bool   DiscordShowElapsed = true;
        public int    DiscordUpdateSeconds = 15;

        // The local off switch for reporting online at all; who sees it is decided on Global by the player's privacy.
        public bool   GlobalPresenceEnabled = true;

        // Wealth -> threat scaling is deliberately NOT here: it is an anti-exploit rule, and the server owns it.

        // One place, so the fields, the clamps, the windows and Reset cannot disagree. -1 means "never placed".
        public const float ChatPopoutMinW = 380f, ChatPopoutMinH = 260f, ChatPopoutDefW = 640f, ChatPopoutDefH = 480f;
        public const float VideoWindowMinW = 320f, VideoWindowMinH = 200f, VideoWindowDefW = 560f, VideoWindowDefH = 340f;

        public float  ChatPopoutX = -1f, ChatPopoutY = -1f;
        public float  ChatPopoutW = ChatPopoutDefW, ChatPopoutH = ChatPopoutDefH;
        public string ChatPopoutChannel = "";

        // 0 means take whatever the server allows.
        public int VideoQuality = 0;
        public int VideoCacheGigabytes = 2;

        public float VideoWindowX = -1f, VideoWindowY = -1f;
        public float VideoWindowW = VideoWindowDefW, VideoWindowH = VideoWindowDefH;

        // Reachable from Mod Options too: a window dragged onto a monitor that is gone cannot be clicked at all.
        public void ResetWindowPlacement()
        {
            ChatPopoutX  = ChatPopoutY  = -1f;
            ChatPopoutW  = ChatPopoutDefW;  ChatPopoutH  = ChatPopoutDefH;
            VideoWindowX = VideoWindowY = -1f;
            VideoWindowW = VideoWindowDefW; VideoWindowH = VideoWindowDefH;
        }

        // Per-channel level as an int (0 Silent, 1 Toast, 2 Toast+Sound) so the loader need not know the payload enum.
        public bool ChatNotificationsMuted = false;
        public int  ChatNotifyServer = 0;   // busy channel - quiet by default
        public int  ChatNotifyGuild  = 1;
        public int  ChatNotifyDm     = 2;   // directed at you - loudest by default

        // Its own level, not ChatNotifyServer's: server chat is quiet because it is busy, which says nothing about Global.
        public int  ChatNotifyGlobal = 1;

        public bool ShowPerfOverlay = false;

        public bool AnimatedVisuals = true;
        public bool ReduceMotion = false;
        public bool ColonyNexusLoadingScreen = true;
        public bool ShaderEffects = false;

        public bool ReportAttachScreenshot = true;
        public bool ReportAttachDiagnostics = true;
        public bool OfferErrorReports = true;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref ShowWelcomeOnLaunch,        "ShowWelcomeOnLaunch",        defaultValue: true);
            Scribe_Values.Look(ref ShowWhatsNewOnUpdate,       "ShowWhatsNewOnUpdate",       defaultValue: true);
            Scribe_Values.Look(ref WhatsNewSeen,               "WhatsNewSeen",               defaultValue: "");
            Scribe_Values.Look(ref WelcomeSeen,                "WelcomeSeen",                defaultValue: false);
            Scribe_Values.Look(ref DebugLogging,               "DebugLogging",               defaultValue: false);
            Scribe_Values.Look(ref ProtocolLogging,           "ProtocolLogging",           defaultValue: false);
            // A new key on purpose: the old consent meant something else, so it is re-asked rather than inherited.
            Scribe_Values.Look(ref ShareDebugLogsWithServer,   "ShareDebugLogsWithServer",   defaultValue: false);
            Scribe_Values.Look(ref LogSharingMode,             "LogSharingMode",             defaultValue: -1);
            // From the old boolean: opt-in becomes Automatic, anything else Ask. Never is opt-in only, never migrated into.
            if (LogSharingMode < LogShareNever || LogSharingMode > LogShareAutomatic)
                LogSharingMode = ShareDebugLogsWithServer ? LogShareAutomatic : LogShareAsk;
            Scribe_Values.Look(ref ThemeUseServer,             "ThemeUseServer",             defaultValue: true);
            Scribe_Values.Look(ref ThemeAccent,                "ThemeAccent",                defaultValue: "");
            Scribe_Values.Look(ref ThemeServerChat,            "ThemeServerChat",            defaultValue: "");
            Scribe_Values.Look(ref ThemeGuildChat,             "ThemeGuildChat",             defaultValue: "");
            Scribe_Values.Look(ref ThemeDirectMsg,             "ThemeDirectMsg",             defaultValue: "");
            Scribe_Values.Look(ref ThemeDiscord,               "ThemeDiscord",               defaultValue: "");
            Scribe_Values.Look(ref ThemeNameNormal,           "ThemeNameNormal",            defaultValue: "");
            Scribe_Values.Look(ref ThemeTextNormal,           "ThemeTextNormal",            defaultValue: "");
            Scribe_Values.Look(ref ThemeNameDiscord,          "ThemeNameDiscord",           defaultValue: "");
            Scribe_Values.Look(ref ThemeTextDiscord,          "ThemeTextDiscord",           defaultValue: "");
            Scribe_Values.Look(ref MarkerUseServer,            "MarkerUseServer",            defaultValue: true);
            Scribe_Values.Look(ref MarkerMine,                 "MarkerMine",                 defaultValue: "");
            Scribe_Values.Look(ref MarkerTheirs,               "MarkerTheirs",               defaultValue: "");
            Scribe_Values.Look(ref MarkerShowRim,              "MarkerShowRim",              defaultValue: true);
            Scribe_Values.Look(ref ShowDiscordTag,             "ShowDiscordTag",             defaultValue: true);
            Scribe_Values.Look(ref AutoLoadChatImages,        "AutoLoadChatImages",         defaultValue: true);
            Scribe_Values.Look(ref ChatOverlay,                "ChatOverlay",                defaultValue: true);
            Scribe_Values.Look(ref ChatWindowOpacity,          "ChatWindowOpacity",          defaultValue: 1f);
            Scribe_Values.Look(ref ChatVideoMuted,             "ChatVideoMuted",             defaultValue: false);
            Scribe_Values.Look(ref ChatVideoVolume,            "ChatVideoVolume",            defaultValue: 0.6f);
            Scribe_Values.Look(ref ChatVideoLoop,              "ChatVideoLoop",              defaultValue: true);
            // A hand-edited or half-written config must not leave playback silent-forever or deafening.
            if (ChatVideoVolume < 0f || ChatVideoVolume > 1f || float.IsNaN(ChatVideoVolume)) ChatVideoVolume = 0.6f;
            Scribe_Values.Look(ref GlobalBaseUrl,              "GlobalBaseUrl",              defaultValue: "");
            Scribe_Values.Look(ref AskToLinkSteam,             "AskToLinkSteam",             defaultValue: true);
            Scribe_Values.Look(ref UseKmhApiTransport,         "UseKmhApiTransport",         defaultValue: true);
            Scribe_Values.Look(ref KmhApiPort,                 "KmhApiPort",                 defaultValue: 5099);
            Scribe_Values.Look(ref AllowChatTransportFallback, "AllowChatTransportFallback", defaultValue: true);
            Scribe_Values.Look(ref KmhApiHostOverride,         "KmhApiHostOverride",         defaultValue: "");
            Scribe_Values.Look(ref DiscordPresenceEnabled,     "DiscordPresenceEnabled",     defaultValue: true);
            Scribe_Values.Look(ref DiscordAppId,               "DiscordAppId",               defaultValue: DefaultDiscordAppId);
            Scribe_Values.Look(ref AllowDiscordJoin,           "AllowDiscordJoin",           defaultValue: true);
            Scribe_Values.Look(ref DiscordShowPrivateServerName, "DiscordShowPrivateServerName", defaultValue: false);
            Scribe_Values.Look(ref DiscordShowActivity,        "DiscordShowActivity",        defaultValue: true);
            Scribe_Values.Look(ref DiscordShowGameplay,        "DiscordShowGameplay",        defaultValue: true);
            Scribe_Values.Look(ref DiscordDetailedActivity,    "DiscordDetailedActivity",    defaultValue: true);
            Scribe_Values.Look(ref DiscordShowMultiplayer,     "DiscordShowMultiplayer",     defaultValue: true);
            Scribe_Values.Look(ref DiscordShowServerName,      "DiscordShowServerName",      defaultValue: true);
            Scribe_Values.Look(ref DiscordShowColonyInfo,      "DiscordShowColonyInfo",      defaultValue: true);
            Scribe_Values.Look(ref DiscordShowElapsed,         "DiscordShowElapsed",         defaultValue: true);
            Scribe_Values.Look(ref DiscordUpdateSeconds,       "DiscordUpdateSeconds",       defaultValue: 15);
            // Discord drops updates faster than this, and a hand-edited value must not stall the status for hours.
            if (DiscordUpdateSeconds < 15 || DiscordUpdateSeconds > 120) DiscordUpdateSeconds = 15;
            Scribe_Values.Look(ref GlobalPresenceEnabled,      "GlobalPresenceEnabled",      defaultValue: true);
            Scribe_Values.Look(ref ChatPopoutX,                "ChatPopoutX",                defaultValue: -1f);
            Scribe_Values.Look(ref ChatPopoutY,                "ChatPopoutY",                defaultValue: -1f);
            Scribe_Values.Look(ref ChatPopoutW,                "ChatPopoutW",                defaultValue: ChatPopoutDefW);
            Scribe_Values.Look(ref ChatPopoutH,                "ChatPopoutH",                defaultValue: ChatPopoutDefH);
            Scribe_Values.Look(ref ChatPopoutChannel,          "ChatPopoutChannel",          defaultValue: "");
            Scribe_Values.Look(ref VideoQuality,              "VideoQuality",               defaultValue: 0);
            Scribe_Values.Look(ref VideoCacheGigabytes,       "VideoCacheGigabytes",        defaultValue: 2);
            // A hand-edited or half-written config must not park the window off-screen forever.
            if (float.IsNaN(ChatPopoutW) || ChatPopoutW < ChatPopoutMinW) ChatPopoutW = ChatPopoutDefW;
            if (float.IsNaN(ChatPopoutH) || ChatPopoutH < ChatPopoutMinH) ChatPopoutH = ChatPopoutDefH;
            if (float.IsNaN(ChatPopoutX)) ChatPopoutX = -1f;
            if (float.IsNaN(ChatPopoutY)) ChatPopoutY = -1f;
            Scribe_Values.Look(ref VideoWindowX,               "VideoWindowX",               defaultValue: -1f);
            Scribe_Values.Look(ref VideoWindowY,               "VideoWindowY",               defaultValue: -1f);
            Scribe_Values.Look(ref VideoWindowW,               "VideoWindowW",               defaultValue: VideoWindowDefW);
            Scribe_Values.Look(ref VideoWindowH,               "VideoWindowH",               defaultValue: VideoWindowDefH);
            // A hand-edited or half-written config must not park the window off-screen or at zero size.
            if (float.IsNaN(VideoWindowW) || VideoWindowW < VideoWindowMinW) VideoWindowW = VideoWindowDefW;
            if (float.IsNaN(VideoWindowH) || VideoWindowH < VideoWindowMinH) VideoWindowH = VideoWindowDefH;
            if (float.IsNaN(VideoWindowX)) VideoWindowX = -1f;
            if (float.IsNaN(VideoWindowY)) VideoWindowY = -1f;
            Scribe_Values.Look(ref ChatNotificationsMuted,     "ChatNotificationsMuted",     defaultValue: false);
            Scribe_Values.Look(ref ChatNotifyServer,           "ChatNotifyServer",           defaultValue: 0);
            Scribe_Values.Look(ref ChatNotifyGuild,            "ChatNotifyGuild",            defaultValue: 1);
            Scribe_Values.Look(ref ChatNotifyDm,               "ChatNotifyDm",               defaultValue: 2);
            Scribe_Values.Look(ref ChatNotifyGlobal,           "ChatNotifyGlobal",           defaultValue: 1);
            Scribe_Values.Look(ref ShowPerfOverlay,            "ShowPerfOverlay",            defaultValue: false);
            Scribe_Values.Look(ref AnimatedVisuals,            "AnimatedVisuals",            defaultValue: true);
            Scribe_Values.Look(ref ReduceMotion,               "ReduceMotion",               defaultValue: false);
            Scribe_Values.Look(ref ColonyNexusLoadingScreen,   "ColonyNexusLoadingScreen",   defaultValue: true);
            Scribe_Values.Look(ref ShaderEffects,              "ShaderEffects",              defaultValue: false);
            Scribe_Values.Look(ref ReportAttachScreenshot,     "ReportAttachScreenshot",     defaultValue: true);
            Scribe_Values.Look(ref ReportAttachDiagnostics,    "ReportAttachDiagnostics",    defaultValue: true);
            Scribe_Values.Look(ref OfferErrorReports,          "OfferErrorReports",          defaultValue: true);
            // KmhLog.DebugEnabled is applied from the payload (KmhEntry) - the loader assembly cannot see it.
        }
    }
}
