using System.Collections.Generic;
using Newtonsoft.Json;

namespace KMHPatch.Features.Enforcement.Dto
{
    // Mirror of the server's snapshot payload. is_admin is server-authoritative, so the admin bypass can't be
    // forged client-side
    public class EnforcementSnapshotDto
    {
        [JsonProperty("enabled")]      public bool         Enabled     { get; set; }
        [JsonProperty("admin_bypass")] public bool         AdminBypass { get; set; } = true;
        [JsonProperty("preserve_personal")] public bool    PreservePersonal { get; set; }
        [JsonProperty("is_admin")]     public bool         IsAdmin     { get; set; }
        [JsonProperty("has_profile")]  public bool         HasProfile   { get; set; }
        [JsonProperty("profile_files")] public int        ProfileFiles { get; set; }
        [JsonProperty("profile_hash")] public string       ProfileHash  { get; set; } = "";
        [JsonProperty("safe_mods")]    public List<string> SafeMods    { get; set; } = new List<string>();
    }
}
