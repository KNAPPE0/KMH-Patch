namespace KMHPatch
{
    // Single source of truth for the patch mod's identifiers and URLs. Anything user-facing or persisted
    // (PersistentSettings keys, Harmony ID, log prefix) should reference this so renames stay consistent
    internal static class Constants
    {
        public const string PackageId      = "knappe.kmh.patch";
        public const string HarmonyId      = "knappe.kmh.patch";
        public const string LogPrefix      = "[KMH-Patch]";
        public const string DisplayName    = "KMH Patch";

        // KMH project links. KMH ships as two repos - the client Patch and the server Addon - so both get surfaced
        // in the welcome flow
        public const string GitHubUrl              = "https://github.com/KNAPPE0/KMH-Patch";
        public const string GitHubServerUrl        = "https://github.com/KNAPPE0/KMH-Server-Addon";
        public const string DiscordUrl             = "https://discord.gg/gDwmsy7VVy";
        public const string SteamWorkshopUrl       = "https://steamcommunity.com/sharedfiles/filedetails/?id=3743150682";

        // Official RimWorld Together upstream - KMH is a downstream patch of their work. Always credit the upstream
        // alongside our own links
        public const string OfficialRwtGitHubUrl         = "https://github.com/RimWorld-Together/Rimworld-Together";
        public const string OfficialRwtDiscordUrl        = "https://discord.gg/yUF2ec8Vt8";
        public const string OfficialRwtSteamWorkshopUrl  = "https://steamcommunity.com/sharedfiles/filedetails/?id=3005289691";
        public const string OfficialRwtKofiUrl           = "https://ko-fi.com/rimworldtogetherproject";
    }
}
