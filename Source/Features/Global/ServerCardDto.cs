using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace KMHPatch.Features.Global
{
    // No owner-private data is carried here, by design.
    internal sealed class ServerCardDto
    {
        public string ServerId = "";
        public string Name = "";
        // Owner-written and drawn to other players, so inerted like every other owner text.
        public string Description = "";
        public string DiscordUrl = "";
        public string WebsiteUrl = "";
        public bool   Official;
        // Server-reported and signature-covered on the heartbeat: RWT will ask a joining player for a password.
        public bool   RequiresPassword;
        // Set on parse, not carried on the wire: the public directory only returns Public and Listed servers.
        public bool   FromPublicDirectory;
        public string Status = "";
        public int    Players;
        public int    MaxPlayers;
        public string Region = "";
        public string PrimaryLanguage = "";
        public List<string> Languages = new List<string>();
        public List<string> Tags = new List<string>();
        public string RimWorldVersion = "";
        public string RwtVersion = "";
        public string KmhVersion = "";
        public int    Protocol;
        public string ManifestState = "";
        public long   ManifestRevision;
        public int    MetadataRevision;
        public string JoinEndpoint = "";
        public string ModPolicy = ModPolicyNames.Unknown;
        public int    ModCount;
        public long?  LastHeartbeatUnix;
        // Absent from a directory older than this field, which simply sorts as never-busy rather than failing.
        public int    PlayersPeak;
        public float  PlayersAverage;

        public bool TrySplitEndpoint(out string host, out int port)
        {
            host = ""; port = 0;
            if (string.IsNullOrEmpty(JoinEndpoint)) return false;
            int colon = JoinEndpoint.LastIndexOf(':');
            if (colon <= 0 || colon == JoinEndpoint.Length - 1) return false;
            if (!int.TryParse(JoinEndpoint.Substring(colon + 1), out port) || port <= 0 || port > 65535) return false;
            host = JoinEndpoint.Substring(0, colon);
            return host.Length > 0;
        }

        // Owner-written text: Global's caps are re-applied on this side.
        internal const int MaxName = 80, MaxShort = 24, MaxTags = 24, MaxDescription = 2000, MaxUrl = 200;

        // Global's own bound, so a version it accepted is never cut and still compares equal.
        internal const int MaxVersion = 64;

        // A full DNS name, or a bracketed IPv6 address, and a port.
        internal const int MaxEndpoint = 270;

        // The cache deserializes straight into this type, bypassing DirectoryParse, so a hand-edited file needs this.
        internal void MakeInert()
        {
            Name            = UI.KmhDisplayText.InertLine(Name ?? "", MaxName);
            JoinEndpoint    = DirectoryParse.Endpoint(JoinEndpoint);
            Description     = UI.KmhDisplayText.Inert(Description ?? "", MaxDescription);
            DiscordUrl      = UI.KmhDisplayText.Inert(DiscordUrl ?? "", MaxUrl);
            WebsiteUrl      = UI.KmhDisplayText.Inert(WebsiteUrl ?? "", MaxUrl);
            Region          = UI.KmhDisplayText.Inert(Region ?? "", MaxShort);
            PrimaryLanguage = UI.KmhDisplayText.Inert(PrimaryLanguage ?? "", MaxShort);
            RimWorldVersion = UI.KmhDisplayText.Inert(RimWorldVersion ?? "", MaxVersion);
            RwtVersion      = UI.KmhDisplayText.Inert(RwtVersion ?? "", MaxVersion);
            KmhVersion      = UI.KmhDisplayText.Inert(KmhVersion ?? "", MaxVersion);
            if (Languages != null)
                for (int i = 0; i < Languages.Count; i++) Languages[i] = UI.KmhDisplayText.Inert(Languages[i] ?? "", MaxShort);
            if (Tags != null)
                for (int i = 0; i < Tags.Count; i++) Tags[i] = UI.KmhDisplayText.Inert(Tags[i] ?? "", MaxShort);
        }
    }

    internal static class DirectoryParse
    {
        public static int Int(JObject body, string field)
            => body == null ? 0 : (int?)body[field] ?? 0;

        public static List<ServerCardDto> Cards(JObject body, out string nextCursor)
        {
            nextCursor = null;
            if (body == null) return null;
            nextCursor = (string)body["next_cursor"];

            JArray rows = body["servers"] as JArray;
            if (rows == null) return null;

            List<ServerCardDto> cards = new List<ServerCardDto>(rows.Count);
            foreach (JToken row in rows)
            {
                ServerCardDto card = Card(row as JObject);
                if (card != null) cards.Add(card);
            }
            return cards;
        }

        // Shared by the stream and the polled page so the same card is never built two ways.
        internal static ServerCardDto Card(JObject o)
        {
            if (o == null) return null;
            string id = (string)o["server_id"];
            if (string.IsNullOrEmpty(id)) return null;

            return new ServerCardDto
            {
                ServerId          = id,
                // Written by other server owners, and Widgets.Label parses rich text; a star in it is not the official mark.
                Name              = UI.KmhDisplayText.InertLine(UI.KmhBrandWords.StripMarks((string)o["name"] ?? ""), MaxName),
                Description       = UI.KmhDisplayText.Inert((string)o["description"] ?? "", MaxDescription),
                DiscordUrl        = UI.KmhDisplayText.Inert((string)o["discord_url"] ?? "", MaxUrl),
                WebsiteUrl        = UI.KmhDisplayText.Inert((string)o["website_url"] ?? "", MaxUrl),
                // A directory the player typed in can say anything; only Colony Nexus's own can make a server official.
                Official          = GlobalHttp.IsFirstParty && ((bool?)o["official"] ?? false),
                // Absent (older Global) must mean locked: offering a join the server then refuses is the worse failure.
                RequiresPassword    = (bool?)o["requires_password"] ?? true,
                FromPublicDirectory = true,
                Status            = (string)o["status"] ?? "",
                Players           = (int?)o["players"] ?? 0,
                MaxPlayers        = (int?)o["max_players"] ?? 0,
                Region            = UI.KmhDisplayText.Inert((string)o["region"] ?? "", MaxShort),
                PrimaryLanguage   = UI.KmhDisplayText.Inert((string)o["primary_language"] ?? "", MaxShort),
                Languages         = Strings(o["languages"]),
                Tags              = Strings(o["tags"]),
                RimWorldVersion   = UI.KmhDisplayText.Inert((string)o["rimworld_version"] ?? "", MaxVersion),
                RwtVersion        = UI.KmhDisplayText.Inert((string)o["rwt_version"] ?? "", MaxVersion),
                KmhVersion        = UI.KmhDisplayText.Inert((string)o["kmh_version"] ?? "", MaxVersion),
                Protocol          = (int?)o["protocol"] ?? 0,
                ManifestState     = (string)o["manifest_state"] ?? "",
                ManifestRevision  = (long?)o["manifest_revision"] ?? 0,
                MetadataRevision  = (int?)o["metadata_revision"] ?? 0,
                JoinEndpoint      = Endpoint((string)o["join_endpoint"]),
                ModPolicy         = (string)o["mod_policy"] ?? ModPolicyNames.Unknown,
                ModCount          = (int?)o["mod_count"] ?? 0,
                LastHeartbeatUnix = (long?)o["last_heartbeat"],
                PlayersPeak       = (int?)o["players_peak"] ?? 0,
                PlayersAverage    = (float?)o["players_avg"] ?? 0f,
            };
        }

        // Dialled and drawn as written, so only host:port characters pass; anything else is no address at all.
        internal static string Endpoint(string raw)
        {
            string s = (raw ?? "").Trim();
            if (s.Length > ServerCardDto.MaxEndpoint) return "";
            foreach (char c in s)
                if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')
                      || c == '.' || c == '-' || c == '_' || c == ':' || c == '[' || c == ']'))
                    return "";
            return s;
        }

        private const int MaxName = ServerCardDto.MaxName, MaxShort = ServerCardDto.MaxShort,
                          MaxTags = ServerCardDto.MaxTags, MaxDescription = ServerCardDto.MaxDescription,
                          MaxUrl = ServerCardDto.MaxUrl, MaxVersion = ServerCardDto.MaxVersion;

        private static List<string> Strings(JToken token)
        {
            List<string> list = new List<string>();
            if (token is JArray arr)
                foreach (JToken t in arr)
                {
                    string s = UI.KmhDisplayText.Inert((string)t ?? "", MaxShort);
                    if (!string.IsNullOrEmpty(s)) list.Add(s);
                    // A listing with a thousand tags would be a filter menu nobody can close.
                    if (list.Count >= MaxTags) break;
                }
            return list;
        }
    }
}
