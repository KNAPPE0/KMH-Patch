using Verse;

namespace KMHPatch
{
    // Persistent settings for the patch mod, saved by RimWorld to Config\Mod_<packageId>_KMHPatchSettings.xml
    // automatically on changes
    //
    // Each setting needs a Scribe_Values.Look call in ExposeData() with a default - same pattern RimWorld uses
    // everywhere for serialization
    public class KMHPatchSettings : ModSettings
    {
        // Whether to auto-show the welcome dialog when the main menu first appears. Once a player dismisses the
        // welcome they probably don't want to see it every launch - but they can always re-open it from the About
        // dialog if they do
        public bool ShowWelcomeOnLaunch = true;

        // Verbose KMH logging (KmhLog.Debug / KmhLog.Protocol). Off by default so normal play stays quiet.
        public bool DebugLogging = false;

        // KMH API transport (experimental, off by default): talk to the addon over its own port instead of RWT chat.
        public bool   UseKmhApiTransport         = false;
        public int    KmhApiPort                 = 5099;
        public bool   AllowChatTransportFallback = true;
        public string KmhApiHostOverride         = "";   // blank = same host as the RWT server connection

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref ShowWelcomeOnLaunch,        "ShowWelcomeOnLaunch",        defaultValue: true);
            Scribe_Values.Look(ref DebugLogging,               "DebugLogging",               defaultValue: false);
            Scribe_Values.Look(ref UseKmhApiTransport,         "UseKmhApiTransport",         defaultValue: false);
            Scribe_Values.Look(ref KmhApiPort,                 "KmhApiPort",                 defaultValue: 5099);
            Scribe_Values.Look(ref AllowChatTransportFallback, "AllowChatTransportFallback", defaultValue: true);
            Scribe_Values.Look(ref KmhApiHostOverride,         "KmhApiHostOverride",         defaultValue: "");
            // Note: KmhLog.DebugEnabled is applied from the payload (KmhEntry) - the loader assembly can't see it.
        }
    }
}
