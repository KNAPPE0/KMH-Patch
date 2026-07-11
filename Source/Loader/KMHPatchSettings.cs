using Verse;

namespace KMHPatch
{
    // Persistent mod settings (RimWorld-serialized); every field needs a Scribe_Values.Look in ExposeData().
    public class KMHPatchSettings : ModSettings
    {
        // Auto-show the welcome dialog on the main menu (re-openable from the About dialog either way).
        public bool ShowWelcomeOnLaunch = true;

        // Show the "What's New" changelog on every launch (change notes included) until turned off. Re-openable from the KMH tab.
        public bool ShowWhatsNewOnUpdate = true;
        // Show the Discord/link reminder inside the welcome popup.
        public bool ShowDiscordReminder = true;

        // Verbose KMH logging (KmhLog.Debug / KmhLog.Protocol). Off by default so normal play stays quiet.
        public bool DebugLogging = false;

        // Mirror KMH logs to the server's Debug/ folder (server can also auto-enable via the hello).
        public bool RemoteDebugLogging = false;

        // KMH API transport (on by default since 1.2.0): only dials when advertised; chat fallback keeps old servers working.
        public bool   UseKmhApiTransport         = true;
        public int    KmhApiPort                 = 5099;
        public bool   AllowChatTransportFallback = true;
        public string KmhApiHostOverride         = "";   // blank = same host as the RWT server connection

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref ShowWelcomeOnLaunch,        "ShowWelcomeOnLaunch",        defaultValue: true);
            Scribe_Values.Look(ref ShowWhatsNewOnUpdate,       "ShowWhatsNewOnUpdate",       defaultValue: true);
            Scribe_Values.Look(ref ShowDiscordReminder,        "ShowDiscordReminder",        defaultValue: true);
            Scribe_Values.Look(ref DebugLogging,               "DebugLogging",               defaultValue: false);
            Scribe_Values.Look(ref RemoteDebugLogging,         "RemoteDebugLogging",         defaultValue: false);
            Scribe_Values.Look(ref UseKmhApiTransport,         "UseKmhApiTransport",         defaultValue: true);
            Scribe_Values.Look(ref KmhApiPort,                 "KmhApiPort",                 defaultValue: 5099);
            Scribe_Values.Look(ref AllowChatTransportFallback, "AllowChatTransportFallback", defaultValue: true);
            Scribe_Values.Look(ref KmhApiHostOverride,         "KmhApiHostOverride",         defaultValue: "");
            // Note: KmhLog.DebugEnabled is applied from the payload (KmhEntry) - the loader assembly can't see it.
        }
    }
}
