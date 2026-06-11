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

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref ShowWelcomeOnLaunch, "ShowWelcomeOnLaunch", defaultValue: true);
        }
    }
}
