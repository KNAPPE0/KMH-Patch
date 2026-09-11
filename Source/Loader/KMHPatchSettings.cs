using Verse;

namespace KMHPatch
{
    // Every field needs a Scribe_Values.Look in ExposeData(), or it silently stops persisting.
    public class KMHPatchSettings : ModSettings
    {
        public bool ShowWelcomeOnLaunch = true;

        public bool ShowWhatsNewOnUpdate = true;

        // Verbose KMH logging (KmhLog.Debug). Off by default so normal play stays quiet.
        public bool DebugLogging = false;

        // Separate from Debug on purpose: useful logging must not also write megabytes of packet chatter.
        public bool ProtocolLogging = false;

        // Standing opt-in to share KMH log lines. A server can request, never enable; off means it must ask each session.
        public bool ShareDebugLogsWithServer = false;

        //   0 = Never (no prompt, ever)   1 = Ask me (once per server per session)   2 = Automatic
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

        // World-map marker rim. Blank hex = take the server's suggestion, then KMH's own.
        public bool   MarkerShowRim    = true;
        public bool   MarkerUseServer  = true;
        public string MarkerMine       = "";
        public string MarkerTheirs     = "";

        // The [D] tag on relayed lines. The marker glyph is NOT switchable: a cue that survives without colour must stay.
        public bool   ShowDiscordTag  = true;

        // Load image previews AUTOMATICALLY. Off by design: loading one reveals this player's IP to a third-party host.
        public bool   AutoLoadChatImages = false;

        // Not muted by default: a video only plays because the player clicked it, so silence would just be a second click.
        public bool   ChatVideoMuted  = false;
        public float  ChatVideoVolume = 0.6f;
        public bool   ChatVideoLoop   = true;

        // KMH API transport (on by default since 1.2.0): only dials when advertised; chat fallback keeps old servers working.
        public bool   UseKmhApiTransport         = true;
        public int    KmhApiPort                 = 5099;
        public bool   AllowChatTransportFallback = true;
        public string KmhApiHostOverride         = "";   // blank = same host as the RWT server connection

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

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref ShowWelcomeOnLaunch,        "ShowWelcomeOnLaunch",        defaultValue: true);
            Scribe_Values.Look(ref ShowWhatsNewOnUpdate,       "ShowWhatsNewOnUpdate",       defaultValue: true);
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
            Scribe_Values.Look(ref AutoLoadChatImages,        "AutoLoadChatImages",         defaultValue: false);
            Scribe_Values.Look(ref ChatVideoMuted,             "ChatVideoMuted",             defaultValue: false);
            Scribe_Values.Look(ref ChatVideoVolume,            "ChatVideoVolume",            defaultValue: 0.6f);
            Scribe_Values.Look(ref ChatVideoLoop,              "ChatVideoLoop",              defaultValue: true);
            // A hand-edited or half-written config must not leave playback silent-forever or deafening.
            if (ChatVideoVolume < 0f || ChatVideoVolume > 1f || float.IsNaN(ChatVideoVolume)) ChatVideoVolume = 0.6f;
            Scribe_Values.Look(ref UseKmhApiTransport,         "UseKmhApiTransport",         defaultValue: true);
            Scribe_Values.Look(ref KmhApiPort,                 "KmhApiPort",                 defaultValue: 5099);
            Scribe_Values.Look(ref AllowChatTransportFallback, "AllowChatTransportFallback", defaultValue: true);
            Scribe_Values.Look(ref KmhApiHostOverride,         "KmhApiHostOverride",         defaultValue: "");
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
            // KmhLog.DebugEnabled is applied from the payload (KmhEntry) - the loader assembly cannot see it.
        }
    }
}
