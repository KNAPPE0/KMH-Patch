using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace KMHPatch.Features.Global
{
    internal static class ModRoleNames
    {
        public const string Required = "required", Optional = "optional";
    }

    internal static class ModKindNames
    {
        // The id is a Steam Workshop file id.
        public const string Workshop  = "workshop";
        // A paid expansion: nothing installable, so "change your mods" is advice the player cannot follow.
        public const string Expansion = "expansion";
        // Core or a hand-installed mod: named only.
        public const string Local     = "local";
    }

    internal sealed class ModEntryDto
    {
        public string Name = "";
        public string Role = ModRoleNames.Required;
        public string Kind = ModKindNames.Local;
        public string Id = "";
        public string PackageId = "";
        // Blank from the server (RWT's config has no author field); filled in locally for installed mods.
        public string Author = "";

        public bool Subscribable => Kind == ModKindNames.Workshop && Mods.KmhWorkshop.IsWorkshopId(Id);

        public static List<ModEntryDto> ParseAll(JToken token)
        {
            List<ModEntryDto> list = new List<ModEntryDto>();
            if (!(token is JArray arr)) return list;
            foreach (JToken t in arr)
            {
                if (!(t is JObject o)) continue;
                string name = (string)o["name"];
                if (string.IsNullOrEmpty(name)) continue;
                list.Add(new ModEntryDto
                {
                    Name      = name,
                    Role      = Known((string)o["role"], ModRoleNames.Required,
                                      ModRoleNames.Required, ModRoleNames.Optional),
                    Kind      = Known((string)o["kind"], ModKindNames.Local,
                                      ModKindNames.Workshop, ModKindNames.Expansion, ModKindNames.Local),
                    Id        = (string)o["id"] ?? "",
                    PackageId = (string)o["package_id"] ?? "",
                    Author    = (string)o["author"] ?? "",
                });
            }
            return list;
        }

        // Unknown values fall back so a kind this client does not know never reaches a switch as something else.
        private static string Known(string value, string fallback, params string[] allowed)
        {
            if (string.IsNullOrEmpty(value)) return fallback;
            foreach (string a in allowed) if (string.Equals(value, a, StringComparison.Ordinal)) return a;
            return fallback;
        }
    }
}
