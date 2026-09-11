namespace KMHPatch
{
    // Persisted keys and the Harmony ID live here, so a rename cannot orphan settings in one place only.
    internal static class Constants
    {
        public const string PackageId      = "knappe.kmh.patch";
        public const string HarmonyId      = "knappe.kmh.patch";
        public const string LogPrefix      = "[KMH-Patch]";
        public const string DisplayName    = "KMH Patch";

        public const string GitHubUrl              = "https://github.com/KNAPPE0/KMH-Patch";
        public const string GitHubServerUrl        = "https://github.com/KNAPPE0/KMH-Server-Addon";
        public const string DiscordUrl             = "https://discord.gg/gDwmsy7VVy";
        public const string SteamWorkshopUrl       = "https://steamcommunity.com/sharedfiles/filedetails/?id=3743150682";

        // KMH is downstream of RimWorld Together, so the upstream is credited alongside our own links.
        public const string OfficialRwtGitHubUrl         = "https://github.com/RimWorld-Together/Rimworld-Together";
        public const string OfficialRwtDiscordUrl        = "https://discord.gg/yUF2ec8Vt8";
        public const string OfficialRwtSteamWorkshopUrl  = "https://steamcommunity.com/sharedfiles/filedetails/?id=3005289691";
        public const string OfficialRwtKofiUrl           = "https://ko-fi.com/rimworldtogetherproject";
    }
}
