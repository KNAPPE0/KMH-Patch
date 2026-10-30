using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using KMHPatch.Features.Global;
using KMHPatch.SubProtocol;
using Newtonsoft.Json.Linq;

namespace KMHPatch.Diagnostics
{
    // Files stay under one temp folder (this runs in a player's game); not *SelfTest, or the runner runs it standalone.
    internal static class KmhClientHardeningChecks
    {
        private const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Static;

        internal static void Run(List<(string, bool, string)> r)
        {
            string scratch = Path.Combine(Path.GetTempPath(), "kmh-harden-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(scratch);
            try
            {
                Guard(r, "global http", () => BoundedBody(r));
                Guard(r, "directory", () => DirectorySnapshots(r, scratch));
                Guard(r, "server text", () => ServerSuppliedText(r, scratch));
                Guard(r, "guild name", () => GuildNamesDrawInert(r));
                Guard(r, "global dm", () => DmMapsLocked(r));
                Guard(r, "account", () => AccountWriteIsAtomic(r, scratch));
                Guard(r, "steam link", () => SteamLinkUrl(r));
                Guard(r, "enforcement text", () => EnforcementText(r));
                Guard(r, "video", () => VideoDownloadBounds(r, scratch));
                Guard(r, "subscribe", () => SubscribeShowsIds(r));
                Guard(r, "presence", () => PresenceNeedsTheDialledServer(r));
                Guard(r, "publish", () => PublishPartSize(r, scratch));
                Guard(r, "duplicate copies", () => DuplicateCopies(r));
                Guard(r, "data folder", () => DataFolderMove(r, scratch));
                Guard(r, "legacy folder", () => LegacyFolder(r, scratch));
                Guard(r, "entry points", () => EntryPoints(r));
                Guard(r, "restored items", () => RestoredItems(r));
                Guard(r, "server names", () => ServerNames(r));
                Guard(r, "workshop details", () => WorkshopDetails(r));
                Guard(r, "official standing", () => OfficialStanding(r));
            }
            finally { try { Directory.Delete(scratch, true); } catch (Exception) { } }
        }

        private sealed class TypedHolder { public object x = null; }

        // Only Colony Nexus Global may say a server is official, or speak as Colony Nexus; a server never can.
        private static void OfficialStanding(List<(string, bool, string)> r)
        {
            string[] claims = { "Colony Nexus", "C0l0ny-N3xus", "ＧＬＯＢＡＬ", "Glоbal", "GIoba1", "Off​icial", "KMH", "Verified", "Worldweave", "CN Staff" };
            string[] plain = { "Owner", "Admin", "Mod", "Builder", "Trader", "Dev" };
            var misread = new List<string>();
            foreach (string c in claims) if (!UI.KmhBrandWords.ClaimsAuthority(c)) misread.Add(c);
            foreach (string p in plain) if (UI.KmhBrandWords.ClaimsAuthority(p)) misread.Add("flagged " + p);
            string stripped = UI.KmhBrandWords.StripMarks("★ Best Server ✔");
            r.Add(("official: a badge or name dressed as Colony Nexus is caught through digits, look-alikes and hidden characters, and stars are stripped",
                   misread.Count == 0 && stripped == "Best Server", $"misread [{string.Join(", ", misread)}]; stripped '{stripped}'"));

            var badges = (System.Collections.IDictionary)typeof(Features.Identity.KmhStaff).GetField("_badges", Hidden).GetValue(null);
            var roles = (Dictionary<string, string>)typeof(Features.Identity.KmhStaff).GetField("_roles", Hidden).GetValue(null);
            var savedBadges = new List<System.Collections.DictionaryEntry>();
            foreach (System.Collections.DictionaryEntry e in badges) savedBadges.Add(e);
            var savedRoles = new Dictionary<string, string>(roles, StringComparer.OrdinalIgnoreCase);
            try
            {
                // The made-up role carries a plain label, so only the role check can keep its badge off.
                Features.Identity.KmhStaff.ApplyWire("owner:Colony Nexus:#ff0000;admin:G1obal Mod:#00ff00;moderator:Helper:#0000ff;god:Guide:#ffffff");
                Features.Identity.KmhStaff.ApplyRoles("kmh-test-ada:owner;kmh-test-bob:god");
                Features.Identity.KmhStaff.TryBadgeForRole("owner", out string owner, out _);
                Features.Identity.KmhStaff.TryBadgeForRole("admin", out string admin, out _);
                Features.Identity.KmhStaff.TryBadgeForRole("moderator", out string mod, out _);
                bool god = Features.Identity.KmhStaff.TryBadgeForRole("god", out _, out _);
                string tip = Features.Identity.KmhStaff.Tooltip("kmh-test-ada") ?? "";
                bool ok = owner == "Owner" && admin == "Admin" && mod == "Helper" && !god
                          && Features.Identity.KmhStaff.RoleOf("kmh-test-bob") == "" && tip.Contains("not a Colony Nexus title");
                r.Add(("official: a server's staff badge names its own roles, never Colony Nexus's, and an unknown role is dropped", ok,
                       $"owner '{owner}', admin '{admin}', moderator '{mod}', made-up role kept: {god}; tooltip '{tip}'"));
            }
            finally
            {
                badges.Clear();
                foreach (System.Collections.DictionaryEntry e in savedBadges) badges[e.Key] = e.Value;
                roles.Clear();
                foreach (var kv in savedRoles) roles[kv.Key] = kv.Value;
            }

            string wasUrl = GlobalHttp.BaseUrl;
            try
            {
                var row = new JObject { ["server_id"] = "srv-x", ["name"] = "★ Real Name ✔", ["official"] = true };
                GlobalHttp.BaseUrl = GlobalHttp.DefaultBaseUrl;
                ServerCardDto ours = DirectoryParse.Card(row);
                GlobalHttp.BaseUrl = "https://directory.example.org";
                ServerCardDto typed = DirectoryParse.Card(row);
                bool ok = ours.Official && !typed.Official && ours.Name == "Real Name";
                r.Add(("official: only Colony Nexus's own directory can mark a server official, and a star in a name is not the mark", ok,
                       $"own directory: {ours.Official}; typed-in directory: {typed.Official}; name '{ours.Name}'"));
            }
            finally { GlobalHttp.BaseUrl = wasUrl; }

            string staging = GlobalHttp.PlaytestBaseUrl;
            bool release = GlobalHttp.ForRelease(staging, false) == "" && GlobalHttp.ForRelease(staging + "/", false) == ""
                        && GlobalHttp.ForRelease(staging, true) == staging && GlobalHttp.ForRelease("https://my.example.org", false) == "https://my.example.org";
            r.Add(("official: a playtester's saved staging address is cleared by the release build and kept by a playtest build", release, staging));

            string listed = KmhServerLabel.For("Listed Name", "Colony Nexus HQ", "1.2.3.4:5555");
            string posing = KmhServerLabel.For("", "★ Colony Nexus HQ", "1.2.3.4:5555");
            string own = KmhServerLabel.For("", "Ada's Farm ★", "1.2.3.4:5555");
            bool labels = listed == "Listed Name" && posing.Contains("1.2.3.4:5555") && !posing.Contains("Nexus") && own == "Ada's Farm"
                          && Features.Reports.KmhReportClient.ServerLabel("Colony Nexus HQ") == UI.KmhText.Get("KMH_Report_ThisServer", "this server");
            r.Add(("official: a server is named by its directory card, and a name posing as Colony Nexus falls back to its address", labels,
                   $"listed '{listed}'; posing '{posing}'; own '{own}'"));

            bool markers = UI.KmhTheme.ClampMarker("​") == UI.KmhTheme.DefaultDiscordMarker && UI.KmhTheme.ClampMarker("★") == UI.KmhTheme.DefaultDiscordMarker
                        && UI.KmhTheme.ClampMarker("́") == UI.KmhTheme.DefaultDiscordMarker && UI.KmhTheme.ClampMarker("◆") == "◆";
            string n1 = UI.KmhTheme.ServerNameNormal, t1 = UI.KmhTheme.ServerTextNormal, n2 = UI.KmhTheme.ServerNameDiscord, t2 = UI.KmhTheme.ServerTextDiscord;
            string marker = UI.KmhTheme.DiscordMarker;
            try
            {
                UI.KmhTheme.ApplyServerChatTheme(n1, t1, n2, t2, "◆");
                var g = new Features.Chat.Dto.ChatMessage { Channel = Features.Chat.ChatChannels.Global, FromUsername = "Ann", Body = "hi", Origin = "discord",
                                                            SentUtcTicks = DateTime.UtcNow.Ticks };
                var s = new Features.Chat.Dto.ChatMessage { Channel = Features.Chat.ChatChannels.Server, FromUsername = "Ann", Body = "hi", Origin = "discord",
                                                            SentUtcTicks = DateTime.UtcNow.Ticks };
                string globalLine = Features.Chat.ChatPanel.Line(g), serverLine = Features.Chat.ChatPanel.Line(s);
                string relayed = Features.Chat.KmhCommsNotifications.GlobalWho(new Features.Chat.Dto.ChatMessage { FromUsername = "<b>Ann</b>", Origin = "ingame" });
                string discord = Features.Chat.KmhCommsNotifications.GlobalWho(g);
                bool lines = globalLine.Contains(UI.KmhTheme.DefaultDiscordMarker) && !globalLine.Contains("◆") && serverLine.Contains("◆")
                             && relayed.StartsWith("[relayed] ") && !relayed.Contains("<") && discord.StartsWith("[D?] ");
                r.Add(("official: a server's Discord marker must be visible and never styles Global lines; Global toasts say who relayed a name",
                       markers && lines, $"markers {markers}; global '{globalLine}'; server '{serverLine}'; toast names '{relayed}', '{discord}'"));
            }
            finally { UI.KmhTheme.ApplyServerChatTheme(n1, t1, n2, t2, marker); }

            var fake = new Features.Chat.Dto.ChatMessage { Channel = Features.Chat.ChatChannels.Server, FromUsername = "Global · Ann", Body = "hi",
                                                           SentUtcTicks = DateTime.UtcNow.Ticks };
            string overlay = Features.Chat.ChatOverlay.TextFor(fake);
            r.Add(("official: an overlay line names its channel first, so a server player named like Global still reads as server chat",
                   overlay.StartsWith("<color=#A9AFB9>" + Features.Chat.KmhCommsNotifications.Label(Features.Chat.ChatChannels.Server) + "</color>"), overlay));

            string unknownBoard = Features.Leaderboards.LeaderboardText.Title(new Features.Leaderboards.Dto.LeaderboardBoard { Key = "zz-new", Title = "<color=red>Top</color>" });
            var site = new Features.Sites.Dto.SiteEntry { SiteName = "<size=90>Fort</size>", OwnerKind = Features.Sites.Dto.SiteEntry.OwnerSystem,
                                                         ControllerFaction = "<color=gold>Colony Nexus</color>" };
            string drawn = unknownBoard + Features.Sites.SiteLabels.Name(site) + Features.Sites.SiteLabels.Controller(site)
                         + Features.Sites.KmhMarkerArt.LabelFor(site, "");
            r.Add(("official: board titles, site names and controllers a server sends are drawn as text", !drawn.Contains("<"), drawn));
        }

        // What a server, or a player posing as one on a stock RWT server, can make this client do.
        private static void EntryPoints(List<(string, bool, string)> r)
        {
            // A stock RWT server echoes the client's own envelope back; from then on, the "server's" lines are a player's.
            const string me = "Ada", env = "{\"kind\":\"kmh.hello_ack\",\"v\":3}";
            Patches.Patch_PM_Chat_KmhIntercept.Sender Who(string user, string msg, bool seen) => Patches.Patch_PM_Chat_KmhIntercept.Classify(user, msg, me, seen);
            bool echo = Who(me, env, false) == Patches.Patch_PM_Chat_KmhIntercept.Sender.OwnEcho
                     && Who(KmhProtocol.ClientUsername, env, false) == Patches.Patch_PM_Chat_KmhIntercept.Sender.OwnEcho;
            bool forged = Who(KmhProtocol.SystemUsername, env, true) == Patches.Patch_PM_Chat_KmhIntercept.Sender.Forged
                       && Who(KmhProtocol.SystemUsername, env, false) == Patches.Patch_PM_Chat_KmhIntercept.Sender.Server;
            bool chat = Who(me, "hello everyone", false) == Patches.Patch_PM_Chat_KmhIntercept.Sender.Player
                     && Who("Bob", env, false) == Patches.Patch_PM_Chat_KmhIntercept.Sender.Player;
            r.Add(("entry points: on a server that echoes Colony Nexus traffic, lines under the server's name are refused",
                   echo && forged && chat, $"echo={echo} forged={forged} chat={chat}"));

            KmhDialPolicy.ResolveForTest = h => h == "play.example" ? new[] { IPAddress.Parse("203.0.113.7") }
                                              : h == "lan.example" ? new[] { IPAddress.Parse("192.168.1.20") }
                                              : IPAddress.TryParse(h, out IPAddress a) ? new[] { a } : new IPAddress[0];
            try
            {
                bool dial = !KmhDialPolicy.MayDial("play.example", "lan.example", out _) && !KmhDialPolicy.MayDial("203.0.113.7", "127.0.0.1", out _)
                         && !KmhDialPolicy.MayDial("play.example", "fd00::5", out _)
                         && KmhDialPolicy.MayDial("192.168.1.5", "lan.example", out _) && KmhDialPolicy.MayDial("play.example", "198.51.100.4", out _);
                r.Add(("entry points: a server reached over the internet cannot aim the API connection into the player's own network", dial,
                       dial ? "" : "A PRIVATE ADDRESS WAS DIALLED FOR A PUBLIC SERVER, OR A LAN GAME WAS REFUSED"));
            }
            finally { KmhDialPolicy.ResolveForTest = null; }

            // A frame's own size sets its decode buffer, so a frame larger than the image is refused, not allocated.
            byte[] Gif(int fw, int fh) => new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 4, 0, 4, 0, 0x80, 0, 0, 0, 0, 0, 0xFF, 0xFF, 0xFF,
                                                       0x2C, 0, 0, 0, 0, (byte)fw, (byte)(fw >> 8), (byte)fh, (byte)(fh >> 8), 0, 2, 2, 0x44, 0x01, 0, 0x3B };
            var fits = Features.Chat.KmhGif.DecodeFrames(Gif(4, 4));
            var over = Features.Chat.KmhGif.DecodeFrames(Gif(8, 8));
            bool gif = fits != null && fits.Frames.Count == 1 && over == null;
            r.Add(("entry points: a gif frame larger than its image is refused before its buffer is allocated", gif,
                   $"fitting frame decoded={fits?.Frames.Count ?? 0}, oversized decoded={over?.Frames.Count ?? 0}"));

            // Every animated png frame becomes a full texture, and what is copied into each frame is bounded.
            byte[] Int(int v) => new[] { (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v };
            var png = new List<byte> { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
            var ihdr = new List<byte>(Int(1920)); ihdr.AddRange(Int(1080)); ihdr.AddRange(new byte[] { 8, 6, 0, 0, 0 });
            png.AddRange(Features.Chat.KmhApng.Chunk("IHDR", ihdr.ToArray()));
            png.AddRange(Features.Chat.KmhApng.Chunk("acTL", new byte[8]));
            // One chunk no frame needs, and one a frame needs but far larger than any real one: each guard on its own.
            png.AddRange(Features.Chat.KmhApng.Chunk("tEXt", new byte[2 * 1024]));
            png.AddRange(Features.Chat.KmhApng.Chunk("PLTE", new byte[20 * 1024]));
            for (int i = 0; i < 25; i++)
            {
                var fc = new List<byte>(Int(i * 2));
                fc.AddRange(Int(1)); fc.AddRange(Int(1)); fc.AddRange(Int(0)); fc.AddRange(Int(0));
                fc.AddRange(new byte[] { 0, 1, 0, 10, 0, 0 });
                png.AddRange(Features.Chat.KmhApng.Chunk("fcTL", fc.ToArray()));
                var fd = new List<byte>(Int(i * 2 + 1)) { 0 };
                png.AddRange(Features.Chat.KmhApng.Chunk("fdAT", fd.ToArray()));
            }
            png.AddRange(Features.Chat.KmhApng.Chunk("IEND", new byte[0]));
            var sheet = Features.Chat.KmhApng.Split(png.ToArray());
            bool budget = sheet != null && sheet.Parts.Count > 0 && sheet.Parts.Count <= Features.Chat.KmhGif.MaxTotalDecodedPixels / (1920L * 1080);
            bool small = sheet != null && sheet.Parts.TrueForAll(p => p.Png.Length < 1024);
            r.Add(("entry points: an animated png is budgeted by full frames and copies no bulk chunk into each one", budget && small,
                   $"frames kept={sheet?.Parts.Count ?? -1}, largest frame={(sheet == null || sheet.Parts.Count == 0 ? 0 : System.Linq.Enumerable.Max(sheet.Parts.ConvertAll(p => p.Png.Length)))} bytes"));

            // A link code is shown only in answer to the player's own request, and only if it is one.
            DateTime now = DateTime.UtcNow;
            Features.LinkedAccounts.LinkedAccountsHandler.ArmForTest(DateTime.MinValue);
            bool unasked = !Features.LinkedAccounts.LinkedAccountsHandler.AcceptCode("ABC234", now);
            Features.LinkedAccounts.LinkedAccountsHandler.ArmForTest(now);
            bool markup = !Features.LinkedAccounts.LinkedAccountsHandler.AcceptCode("<b>X</b>", now);
            bool asked = Features.LinkedAccounts.LinkedAccountsHandler.AcceptCode("ABC234", now);
            bool once = !Features.LinkedAccounts.LinkedAccountsHandler.AcceptCode("ABC234", now);
            Features.LinkedAccounts.LinkedAccountsHandler.ArmForTest(now - TimeSpan.FromMinutes(3));
            bool late = !Features.LinkedAccounts.LinkedAccountsHandler.AcceptCode("ABC234", now);
            Features.LinkedAccounts.LinkedAccountsHandler.ArmForTest(DateTime.MinValue);
            bool link = unasked && markup && asked && once && late;
            r.Add(("entry points: a link code nobody asked for, or one that is not a code, is never shown or copied", link,
                   $"unasked={unasked} markup={markup} asked={asked} once={once} late={late}"));

            bool global = Features.Chat.ChatHandler.GlobalOwned("global") && Features.Chat.ChatHandler.GlobalOwned("gdm:abc")
                       && !Features.Chat.ChatHandler.GlobalOwned("server") && !Features.Chat.ChatHandler.GlobalOwned("guild:x")
                       && KmhIlReader.CallsMadeBy("KMHPatch.Features.Chat.ChatHandler", "OnMessage").Contains("GlobalOwned")
                       && KmhIlReader.CallsMadeBy("KMHPatch.Features.Chat.ChatHandler", "OnSnapshot").Contains("GlobalOwned");
            r.Add(("entry points: the game server cannot write lines into Global chat or Global direct messages", global,
                   global ? "" : "A SERVER CAN SPEAK AS GLOBAL"));

            bool stacks = UI.ColonyGoods.TooManyStacks(1, int.MaxValue) && UI.ColonyGoods.TooManyStacks(500, 600_000)
                       && !UI.ColonyGoods.TooManyStacks(500, 100_000) && !UI.ColonyGoods.TooManyStacks(75, 75_000)
                       && KmhIlReader.CallsMadeBy("KMHPatch.UI.ColonyGoods", "DeliverKey").Contains("TooManyStacks");
            r.Add(("entry points: one delivery cannot make the client build more than a thousand stacks", stacks,
                   stacks ? "" : "AN UNBOUNDED AMOUNT REACHES THE STACK BUILDER"));

            // A saved item names only game types, never an assembly, and a crate never holds a pawn.
            Func<string, Type> thingClass = d => Verse.DefDatabase<Verse.ThingDef>.GetNamedSilentFail(d)?.thingClass;
            Func<string, Type> resolve = n => Verse.GenTypes.GetTypeInAnyAssembly(n);
            string Saved(string inner) => "<savegame><t Class=\"ThingWithComps\"><def>Silver</def>" + inner + "</t></savegame>";
            bool plain = Items.KmhThingScribe.ShapeMatches(Saved(""), "Silver", thingClass, resolve, out string plainWhy);
            bool qualified = !Items.KmhThingScribe.ShapeMatches(Saved("<comps><li Class=\"Verse.Thing, Assembly-CSharp\"/></comps>"), "Silver", thingClass, resolve, out _);
            Items.KmhThingScribe.FromGameOrModForTest = _ => false;
            bool foreign;
            try { foreign = !Items.KmhThingScribe.ShapeMatches(Saved("<comps><li Class=\"Verse.Thing\"/></comps>"), "Silver", thingClass, resolve, out _); }
            finally { Items.KmhThingScribe.FromGameOrModForTest = null; }
            bool pawnCrate = !Items.KmhThingScribe.ShapeMatches(
                "<savegame><t Class=\"MinifiedThing\"><def>MinifiedThing</def><innerContainer><innerList><li Class=\"Pawn\"><def>Human</def></li></innerList></innerContainer></t></savegame>",
                "MinifiedThing", thingClass, resolve, out _);
            bool scribe = plain && qualified && foreign && pawnCrate;
            r.Add(("entry points: a saved item naming an assembly, a type outside the game, or a crated pawn is refused", scribe,
                   $"plain={plain} ({plainWhy}) qualified={qualified} foreign={foreign} pawn crate={pawnCrate}"));
            // Scribe loads every child of a crate's list, so one not named <li> is checked like any other.
            bool unnamedPawnCrate = !Items.KmhThingScribe.ShapeMatches(
                "<savegame><t Class=\"MinifiedThing\"><def>MinifiedThing</def><innerContainer><innerList><x Class=\"Pawn\"><def>Human</def></x></innerList></innerContainer></t></savegame>",
                "MinifiedThing", thingClass, resolve, out string unnamedWhy);
            r.Add(("entry points: a crated pawn under a list entry not named li is refused too", unnamedPawnCrate, unnamedWhy ?? "ACCEPTED"));

            // Answers are taken only for questions this client asked; what they say is drawn inert.
            KmhEnvelope Env(string dataJson) => KmhEnvelope.TryParse("{\"kind\":\"x\",\"data\":" + dataJson + "}");
            typeof(Features.Chat.ChatMediaClient).GetMethod("OnMeta", Hidden)
                .Invoke(null, new object[] { Env("{\"id\":\"kmh-test-unasked\",\"ok\":false,\"reason\":\"<b>x</b>\"}") });
            bool mediaIgnored = Features.Chat.ChatMediaClient.FailureFor("kmh-test-unasked") == null;
            MethodInfo refreshed = typeof(Features.Chat.ChatMediaRefreshClient).GetMethod("OnRefreshed", Hidden);
            refreshed.Invoke(null, new object[] { Env("{\"ref\":\"kmh-test-ref\",\"ok\":false,\"reason\":\"gone\"}") });
            bool refreshIgnored = Features.Chat.ChatMediaRefreshClient.GoneReasonFor("kmh-test-ref") == null;
            Features.Chat.ChatMediaRefreshClient.Request("kmh-test-ref2");
            refreshed.Invoke(null, new object[] { Env("{\"ref\":\"kmh-test-ref2\",\"ok\":false,\"reason\":\"<color=#ff0000>gone</color>\"}") });
            string gone = Features.Chat.ChatMediaRefreshClient.GoneReasonFor("kmh-test-ref2");
            Features.Chat.ChatMediaRefreshClient.Clear();
            bool media = mediaIgnored && refreshIgnored && gone != null && NoMarkup(gone);
            r.Add(("entry points: media answers nobody asked for are dropped, and a reason is drawn as plain text", media,
                   $"meta dropped={mediaIgnored} refresh dropped={refreshIgnored} reason={gone ?? "<none>"}"));

            // Another mod's process-wide Newtonsoft settings never reach what a server sends.
            Func<Newtonsoft.Json.JsonSerializerSettings> priorDefaults = Newtonsoft.Json.JsonConvert.DefaultSettings;
            object held = null;
            try
            {
                Newtonsoft.Json.JsonConvert.DefaultSettings = () => new Newtonsoft.Json.JsonSerializerSettings { TypeNameHandling = Newtonsoft.Json.TypeNameHandling.All };
                held = KmhEnvelope.TryParse("{\"kind\":\"x\",\"data\":{\"x\":{\"$type\":\"System.Version, mscorlib\"}}}")
                    ?.DataAs<TypedHolder>()?.x;
            }
            finally { Newtonsoft.Json.JsonConvert.DefaultSettings = priorDefaults; }
            bool untyped = held is JObject;
            r.Add(("entry points: a type name in server JSON is never honoured, whatever another mod set process-wide", untyped,
                   untyped ? "" : "BUILT " + (held?.GetType().FullName ?? "nothing")));

            var many = new JArray();
            for (int i = 0; i <= ModProfileDto.MaxListed; i++) many.Add("m" + i);
            bool capped = ModProfileDto.Parse(new JObject { ["server_id"] = "s", ["order"] = many }) == null
                       && ModProfileDto.Parse(new JObject { ["server_id"] = "s", ["order"] = new JArray("a", "b") }) != null;
            r.Add(("entry points: a mod profile listing more mods than any server publishes is refused", capped, capped ? "" : "UNBOUNDED"));

            bool redacted = KmhIlReader.CallsMadeBy("KMHPatch.Diagnostics.KmhDebugUplink", "Enqueue").Contains("Apply");
            r.Add(("entry points: a shared debug log line goes through the report redactor", redacted, redacted ? "" : "RAW LINES SHIP"));

            bool strict = Features.Global.KmhHttps.Strict(null, null, null, System.Net.Security.SslPolicyErrors.None)
                       && !Features.Global.KmhHttps.Strict(null, null, null, System.Net.Security.SslPolicyErrors.RemoteCertificateChainErrors)
                       && !Features.Global.KmhHttps.Strict(null, null, null, System.Net.Security.SslPolicyErrors.RemoteCertificateNameMismatch);
            bool wired;
            using (HttpClientHandler h = Features.Global.KmhHttps.Handler())
            {
                try { wired = h.ServerCertificateCustomValidationCallback != null; }
                catch (Exception ex) when (ex is NotImplementedException || ex is PlatformNotSupportedException) { wired = true; }
            }
            r.Add(("entry points: Colony Nexus's own HTTPS clients check certificates themselves", strict && wired,
                   $"strict={strict} handler checks={wired}"));

            // Its cards can be marked Official, so the directory socket checks its own certificate or is never opened.
            PropertyInfo socketCheck = typeof(System.Net.WebSockets.ClientWebSocketOptions).GetProperty("RemoteCertificateValidationCallback");
            bool canCheck;
            using (var probe = new System.Net.WebSockets.ClientWebSocket()) canCheck = Features.Global.KmhHttps.CheckSocket(probe.Options);
            string streamDetail;
            bool streamChecks;
            using (System.Net.WebSockets.ClientWebSocket tls = KmhDirectoryStream.NewSocket("wss://directory.example.org/api/v1/directory/stream"))
            using (System.Net.WebSockets.ClientWebSocket plainWs = KmhDirectoryStream.NewSocket("ws://localhost:5080/api/v1/directory/stream"))
            {
                System.Net.Security.RemoteCertificateValidationCallback check = null;
                try { if (tls != null && socketCheck != null) check = socketCheck.GetValue(tls.Options, null) as System.Net.Security.RemoteCertificateValidationCallback; }
                catch (Exception) { }
                bool tlsOk = canCheck
                    ? check != null && check(null, null, null, System.Net.Security.SslPolicyErrors.None)
                      && !check(null, null, null, System.Net.Security.SslPolicyErrors.RemoteCertificateChainErrors)
                    : tls == null;
                // RimWorld 1.7's runtime has the per-socket check, so there the stream must still open.
                bool liveWhereItCan = canCheck || !Compat.KmhGameVersion.IsCoreClr;
                bool opens = KmhIlReader.CallsMadeBy("KMHPatch.Features.Global.KmhDirectoryStream", "StreamAsync").Contains("NewSocket");
                streamChecks = tlsOk && liveWhereItCan && plainWs != null && opens;
                streamDetail = $"runtime check={canCheck} (property {(socketCheck != null)}, CoreCLR {Compat.KmhGameVersion.IsCoreClr}) "
                             + $"tls socket={(tls == null ? "not opened" : check == null ? "UNCHECKED" : "checked")} plain socket={(plainWs != null)} stream uses it={opens}";
            }
            r.Add(("entry points: the directory socket checks the certificate itself, or stays closed and the list is polled", streamChecks, streamDetail));
        }

        // The shape check reads the XML and Scribe may read another node, so what was actually built is checked too.
        private static void RestoredItems(List<(string, bool, string)> r)
        {
            const string kinds = "restored items: what loaded must be the def it was sold as, of that def's class, and an item rather than a pawn";
            const string crates = "restored items: a crate must hold exactly one thing of its def's class, never a pawn";
            const string blob = "restored items: an item that loads as something else is refused once loaded, and the reported blob never loads";
            // Things take their ids from the running game.
            if (Verse.Current.Game == null)
            {
                foreach (string name in new[] { kinds, crates, blob }) r.Add(KmhSelfTestResult.Skipped(name, "needs a loaded game"));
                return;
            }
            Verse.ThingDef silverDef = RimWorld.ThingDefOf.Silver, humanDef = RimWorld.ThingDefOf.Human;
            Verse.Thing odd = silverDef.thingClass == typeof(Verse.ThingWithComps) ? new Verse.Thing() : new Verse.ThingWithComps();
            odd.def = silverDef;
            bool silver = Items.KmhThingScribe.LoadedAsExpected(Verse.ThingMaker.MakeThing(silverDef), silverDef, out string silverWhy);
            bool otherDef = !Items.KmhThingScribe.LoadedAsExpected(Verse.ThingMaker.MakeThing(RimWorld.ThingDefOf.Steel), silverDef, out _);
            bool otherClass = !Items.KmhThingScribe.LoadedAsExpected(odd, silverDef, out _);
            bool pawn = !Items.KmhThingScribe.LoadedAsExpected(new Verse.Pawn { def = humanDef }, humanDef, out _);
            r.Add((kinds, silver && otherDef && otherClass && pawn, $"silver={silver} ({silverWhy}) other def={otherDef} other class={otherClass} pawn={pawn}"));

            Verse.ThingDef buildingDef = null;
            foreach (Verse.ThingDef d in Verse.DefDatabase<Verse.ThingDef>.AllDefsListForReading)
                if (d.Minifiable && !d.MadeFromStuff && d.thingClass == typeof(Verse.Building)) { buildingDef = d; break; }
            bool crate = false, two = false, pawnInside = false;
            if (buildingDef != null)
            {
                RimWorld.MinifiedThing box = RimWorld.MinifyUtility.MakeMinified(Verse.ThingMaker.MakeThing(buildingDef));
                crate = box != null && Items.KmhThingScribe.LoadedAsExpected(box, box.def, out _);
                // Scribe fills the list directly, past the one-stack rule an ordinary add obeys.
                var held = (List<Verse.Thing>)typeof(Verse.ThingOwner<Verse.Thing>)
                    .GetField("innerList", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(box.GetDirectlyHeldThings());
                held.Add(Verse.ThingMaker.MakeThing(buildingDef));
                two = !Items.KmhThingScribe.LoadedAsExpected(box, box.def, out _);
                held.RemoveAt(1);
                held[0] = new Verse.Pawn { def = humanDef };
                pawnInside = !Items.KmhThingScribe.LoadedAsExpected(box, box.def, out _);
            }
            r.Add((crates, crate && two && pawnInside,
                   buildingDef == null ? "NO MINIFIABLE BUILDING TO TEST WITH" : $"{buildingDef.defName}: crate={crate} two inside={two} pawn inside={pawnInside}"));

            // The reported blob: a namespaced <t> that Scribe loads, ahead of the honest item the shape check reads.
            string honest = Items.KmhThingScribe.Save(Verse.ThingMaker.MakeThing(silverDef)) ?? "";
            string hostile = honest.Replace("<kmhthing>", "<kmhthing><t xmlns=\"urn:x\" Class=\"Fire\"><def>Fire</def><id>Fire90210</id></t>");
            bool blocked = Items.KmhThingScribe.Load(hostile, silverDef.defName) == null;
            Verse.Thing back, fire;
            Items.KmhThingScribe.ShapeForTest = (xml, def) => true;
            try
            {
                back = Items.KmhThingScribe.Load(honest, silverDef.defName);
                fire = Items.KmhThingScribe.Load(hostile, silverDef.defName);
            }
            finally { Items.KmhThingScribe.ShapeForTest = null; }
            r.Add((blob, hostile != honest && blocked && back?.def == silverDef && fire == null,
                   $"blob built={hostile != honest} refused by its shape={blocked} honest={back?.def?.defName ?? "NOT LOADED"} "
                   + $"hostile past the shape check={fire?.GetType().Name ?? "refused"}"));
        }

        // A server's name is its own claim; a line break in it must never start a line that reads as the client's.
        private static void ServerNames(List<(string, bool, string)> r)
        {
            string br = "Farm" + (char)13 + (char)10 + "[Colony Nexus] You are banned" + (char)0x2028 + "x";
            string line = UI.KmhDisplayText.InertLine(br, 200);
            r.Add(("server names: every line break in a name becomes one space", line == "Farm [Colony Nexus] You are banned x", line));

            string wasName = KmhDispatcher.ServerName, wasBuild = KmhDispatcher.ServerBuild, wasId = KmhDispatcher.ServerId;
            try
            {
                KmhDispatcher.ServerName = br;
                KmhDispatcher.ServerBuild = "rel-1" + (char)10 + "[Colony Nexus] update now";
                bool stored = KmhDispatcher.ServerName.IndexOf((char)10) < 0 && KmhDispatcher.ServerName.IndexOf((char)13) < 0
                              && KmhDispatcher.ServerBuild.IndexOf((char)10) < 0;
                r.Add(("server names: a hello's name and build are stored on one line", stored, KmhDispatcher.ServerName));

                KmhDispatcher.ServerId = "kmh-test-unlisted-" + Guid.NewGuid().ToString("N");
                KmhDispatcher.ServerName = "Colony Nexus Official";
                string posing = KmhHandshakeHandler.ConnectedToast(3);
                KmhDispatcher.ServerName = "Ada's Farm";
                string own = KmhHandshakeHandler.ConnectedToast(3);
                bool posted = KmhIlReader.CallsMadeBy("KMHPatch.SubProtocol.KmhHandshakeHandler", "ActivateSession").Contains("ConnectedToast");
                bool toast = posing.IndexOf("Official", StringComparison.Ordinal) < 0 && own.Contains("Ada's Farm") && posted;
                r.Add(("server names: the connected toast names a server the way every notice does, never by a hello posing as Colony Nexus", toast,
                       $"posing '{posing}', own '{own}', handshake uses it={posted}"));
            }
            finally { KmhDispatcher.ServerName = wasName; KmhDispatcher.ServerBuild = wasBuild; KmhDispatcher.ServerId = wasId; }

            bool federation = KmhIlReader.CallsMadeBy("KMHPatch.Features.Federation.Dialog_KMHFederation", "DrawPlace").Contains("Current");
            r.Add(("server names: the Federation window names the server through the same label", federation,
                   federation ? "" : "THE HELLO NAME IS DRAWN RAW"));

            ServerCardDto card = DirectoryParse.Card(new JObject
            {
                ["server_id"] = "kmh-test-endpoint", ["name"] = "Farm" + (char)10 + "[Colony Nexus] Verified",
                ["join_endpoint"] = "play.example.net:25555" + (char)10 + (char)10 + "[Colony Nexus] Verified",
            });
            var cached = new ServerCardDto { Name = "Farm" + (char)10 + "x", JoinEndpoint = "<b>evil</b>:1" };
            cached.MakeInert();
            bool endpoints = card.JoinEndpoint == "" && card.Name.IndexOf((char)10) < 0 && cached.JoinEndpoint == "" && cached.Name.IndexOf((char)10) < 0
                          && DirectoryParse.Endpoint("play.example.net:25555") == "play.example.net:25555"
                          && DirectoryParse.Endpoint("[2001:db8::1]:25555") == "[2001:db8::1]:25555";
            r.Add(("server names: a directory card's address is host and port only, and its name one line", endpoints,
                   $"parsed '{card.JoinEndpoint}', cached '{cached.JoinEndpoint}', name '{card.Name}'"));
        }

        // Steam's own record of each Workshop item sits beside the server's name for it, and refuses what Steam refuses.
        private static void WorkshopDetails(List<(string, bool, string)> r)
        {
            DateTime now = DateTime.UtcNow;
            long fresh = DateTimeOffset.UtcNow.AddDays(-2).ToUnixTimeSeconds();
            string body = "{\"response\":{\"result\":1,\"resultcount\":3,\"publishedfiledetails\":["
                + "{\"publishedfileid\":\"2009463077\",\"result\":1,\"title\":\"Harmony\",\"consumer_app_id\":294100,\"visibility\":0,\"time_created\":1570000000,\"banned\":0},"
                + "{\"publishedfileid\":\"1111111111\",\"result\":1,\"title\":\"Harmony\",\"consumer_app_id\":4000,\"visibility\":0,\"time_created\":1570000000,\"banned\":0},"
                + "{\"publishedfileid\":\"2222222222\",\"result\":1,\"title\":\"Free Mods Pack\",\"consumer_app_id\":294100,\"visibility\":2,\"time_created\":" + fresh + ",\"banned\":0}]}}";
            var items = Features.Mods.KmhWorkshopDetails.Parse(body, new[] { "2009463077", "1111111111", "2222222222", "3333333333" });
            bool parsed = items.Count == 4 && !items[0].Refused && items[1].Refused && !items[2].Refused && items[2].Unlisted
                       && items[2].IsNew(now) && items[3].Refused && !items[3].Found;
            Features.Mods.KmhWorkshopDetails.ResetForTest();
            Features.Mods.KmhWorkshopDetails.SeedForTest(items);
            try
            {
                ModEntryDto Item(string id) => new ModEntryDto { Name = "Harmony", Id = id, Kind = ModKindNames.Workshop };
                bool okLine = !Dialog_KMHPrepareJoin.SubscribeLine(Item("2009463077"), items[0]).StartsWith("!", StringComparison.Ordinal);
                bool otherGame = Dialog_KMHPrepareJoin.SubscribeLine(Item("1111111111"), items[1]).StartsWith("!", StringComparison.Ordinal);
                string renamed = Dialog_KMHPrepareJoin.SubscribeLine(Item("2222222222"), items[2]);
                bool shown = renamed.StartsWith("!", StringComparison.Ordinal) && renamed.IndexOf("Free Mods Pack", StringComparison.Ordinal) >= 0;
                bool refuses = Dialog_KMHPrepareJoin.WorkshopRefusal(new List<ModEntryDto> { Item("2009463077"), Item("1111111111") }) != null
                            && Dialog_KMHPrepareJoin.WorkshopRefusal(new List<ModEntryDto> { Item("2009463077"), Item("2222222222") }) == null;
                bool waits = Dialog_KMHPrepareJoin.WorkshopRefusal(new List<ModEntryDto> { Item("4444444444") }) != null;
                bool ok = parsed && okLine && otherGame && shown && refuses && waits;
                r.Add(("subscribe: Steam's own title and status are shown beside the server's name, and an item Steam refuses blocks the start", ok,
                       $"parsed={parsed} plain={okLine} other game={otherGame} renamed shown={shown} start refused={refuses} waits for Steam={waits}"));
            }
            finally { Features.Mods.KmhWorkshopDetails.ResetForTest(); }
        }

        // A section that throws is one failure with its reason, not a silent gap in the count.
        private static void Guard(List<(string, bool, string)> r, string what, Action run)
        {
            try { run(); }
            catch (Exception ex) { r.Add(($"hardening: the {what} checks ran", false, ex.GetType().Name + ": " + ex.Message)); }
        }

        private static bool NoMarkup(string s) => s != null && s.IndexOf('<') < 0 && s.IndexOf('>') < 0;

        private static HttpResponseMessage Json(string json, HttpStatusCode code = HttpStatusCode.OK)
            => new HttpResponseMessage(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

        private static T Answering<T>(Func<HttpRequestMessage, HttpResponseMessage> responder, Func<T> run)
        {
            Func<HttpRequestMessage, HttpResponseMessage> prior = GlobalHttp.RespondForTest;
            GlobalHttp.RespondForTest = responder;
            try { return run(); }
            finally { GlobalHttp.RespondForTest = prior; }
        }

        private static GlobalHttp.Result Get(string path)
            => GlobalHttp.GetAsync(path, CancellationToken.None).GetAwaiter().GetResult();

        private sealed class CountingStream : Stream
        {
            private readonly long _length;
            public long Served;
            public CountingStream(long length) { _length = length; }
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => Served; set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override int Read(byte[] buffer, int offset, int count)
            {
                long left = _length - Served;
                if (left <= 0) return 0;
                int n = (int)Math.Min(count, left);
                Array.Clear(buffer, offset, n);
                Served += n;
                return n;
            }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }

        private static void BoundedBody(List<(string, bool, string)> r)
        {
            int max = GlobalHttp.MaxResponseBytes;

            var endless = new CountingStream(16L * 1024 * 1024);
            GlobalHttp.Result big = Answering(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(endless) },
                                              () => Get("/api/v1/directory/counters"));
            bool stopped = big.ErrorCode == "KMH-SEC-502" && endless.Served <= max + 64 * 1024;
            r.Add(("global http: a body with no length stops being read at the cap", stopped,
                   $"{endless.Served} byte(s) read of a {max}-byte cap, code {big.ErrorCode}"));

            var declared = new CountingStream(16L * 1024 * 1024);
            GlobalHttp.Result said = Answering(_ =>
            {
                var content = new StreamContent(declared);
                content.Headers.ContentLength = max + 1L;
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
            }, () => Get("/api/v1/directory/counters"));
            bool unread = said.ErrorCode == "KMH-SEC-502" && declared.Served == 0;
            r.Add(("global http: a body declared over the cap is refused without reading it", unread,
                   $"{declared.Served} byte(s) read, code {said.ErrorCode}"));

            byte[] body = Encoding.UTF8.GetBytes("{\"server_count\":7}");
            byte[] withBom = new byte[body.Length + 3];
            withBom[0] = 0xEF; withBom[1] = 0xBB; withBom[2] = 0xBF;
            Buffer.BlockCopy(body, 0, withBom, 3, body.Length);
            GlobalHttp.Result small = Answering(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(withBom) },
                                                () => Get("/api/v1/directory/counters"));
            bool read = small.Ok && small.Body != null && (int?)small.Body["server_count"] == 7;
            r.Add(("global http: an ordinary answer still reads, byte order mark and all", read,
                   read ? "" : $"ok={small.Ok} code={small.ErrorCode}"));

            // Headers then nothing: a headers-only read leaves the body outside the client timeout, so it needs its own.
            TimeSpan priorTimeout = GlobalHttp.BodyTimeout;
            var stall = new StallingStream();
            try
            {
                GlobalHttp.BodyTimeout = TimeSpan.FromMilliseconds(300);
                GlobalHttp.Result stalled = null;
                bool threw = false;
                Task call = Task.Run(() =>
                {
                    try { stalled = Answering(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stall) },
                                              () => Get("/api/v1/directory/counters")); }
                    catch (OperationCanceledException) { threw = true; }
                });
                bool ended = call.Wait(3000);
                bool refused = ended && (threw || (stalled != null && !stalled.Ok));
                r.Add(("global http: a body that stops arriving is given up on, never taken as the whole answer", refused,
                       ended ? (threw ? "cancelled" : $"ok={stalled?.Ok} code={stalled?.ErrorCode}") : "STILL WAITING AFTER 3S"));
            }
            finally
            {
                GlobalHttp.BodyTimeout = priorTimeout;
                stall.Release();
            }

            // The responder above answers in-process, so the HttpClient call itself is checked by what it calls.
            bool headersOnly = false, buffered = false;
            foreach (MethodBase m in KmhIlReader.MethodsCalledBy("KMHPatch.Features.Global.GlobalHttp", "SendAsync"))
            {
                if (m.DeclaringType != typeof(HttpClient) || m.Name != "SendAsync") continue;
                bool withOption = false;
                foreach (ParameterInfo p in m.GetParameters()) if (p.ParameterType == typeof(HttpCompletionOption)) withOption = true;
                if (withOption) headersOnly = true; else buffered = true;
            }
            r.Add(("global http: the request asks for headers only, so the client never buffers a body first",
                   headersOnly && !buffered, headersOnly ? (buffered ? "A BUFFERING SEND IS ALSO MADE" : "") : "NO HEADERS-ONLY SEND FOUND"));
        }

        // Blocks each read until released or closed, then reads as a clean end, which is what a closed socket can do.
        private sealed class StallingStream : Stream
        {
            private readonly ManualResetEventSlim _go = new ManualResetEventSlim(false);
            public void Release() => _go.Set();
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => 0; set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override int Read(byte[] buffer, int offset, int count) { _go.Wait(5000); return 0; }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            protected override void Dispose(bool disposing) { _go.Set(); base.Dispose(disposing); }
        }

        private static string CardJson(string id, int players)
            => "{\"server_id\":\"" + id + "\",\"name\":\"S" + players + "\",\"status\":\"Online\",\"players\":" + players
             + ",\"max_players\":8,\"requires_password\":false}";

        private static int PlayersOf(IReadOnlyList<ServerCardDto> cards, string id)
        {
            foreach (ServerCardDto c in cards) if (c.ServerId == id) return c.Players;
            return -1;
        }

        private static void DirectorySnapshots(List<(string, bool, string)> r, string scratch)
        {
            string cacheRoot = Path.Combine(scratch, "dircache");
            string seenRoot = Path.Combine(scratch, "dirseen");
            Directory.CreateDirectory(cacheRoot);
            Directory.CreateDirectory(seenRoot);
            string cachePath = Path.Combine(cacheRoot, "KMH_DirectoryCache.json");
            string priorCache = KmhDirectoryCache.RootOverrideForTest;
            string priorSeen = Features.Servers.SeenServersStore.RootOverrideForTest;
            Action priorResync = KmhDirectoryStream.Resync;
            int resyncs = 0;
            string A = new string('a', 32), B = new string('b', 32), C = new string('c', 32), D = new string('d', 32);

            void Push(string id, int players)
                => KmhDirectoryStream.ApplyFrom("{\"type\":\"server\",\"revision\":0,\"server\":" + CardJson(id, players) + "}",
                                                KmhDirectoryClient.Generation);

            HttpResponseMessage Page(HttpRequestMessage req, string cards)
                => req.RequestUri.AbsolutePath.EndsWith("/counters", StringComparison.Ordinal)
                    ? Json("{\"server_count\":3,\"player_count\":1}")
                    : Json("{\"servers\":[" + cards + "],\"next_cursor\":null}");

            int Refresh(string cursor)
            {
                KmhDirectoryClient.RefreshAsync(cursor, CancellationToken.None).GetAwaiter().GetResult();
                return 0;
            }

            try
            {
                KmhDirectoryCache.RootOverrideForTest = cacheRoot;
                Features.Servers.SeenServersStore.RootOverrideForTest = seenRoot;
                Features.Servers.SeenServersStore.ReloadForTest();
                KmhDirectoryStream.Resync = () => resyncs++;
                KmhDirectoryStream.Reset();
                KmhDirectoryClient.ResetForTest();

                // A reader enumerating the list must never see it change under it.
                Push(A, 1);
                IReadOnlyList<ServerCardDto> before = KmhDirectoryClient.Cards;
                Push(B, 2);
                Push(A, 5);
                IReadOnlyList<ServerCardDto> after = KmhDirectoryClient.Cards;
                bool frozen = before.Count == 1 && before[0].Players == 1 && after.Count == 2
                           && !ReferenceEquals(before, after) && PlayersOf(after, A) == 5;
                r.Add(("directory: a pushed card publishes a new list and leaves the one being drawn untouched", frozen,
                       $"held {before.Count} row(s) with {(before.Count > 0 ? before[0].Players : -1)} player(s), now {after.Count}"));

                IReadOnlyList<ServerCardDto> held = KmhDirectoryClient.Cards;
                int heldCount = held.Count;
                Answering(req => Page(req, CardJson(C, 3)), () => Refresh("page-2"));
                bool appended = held.Count == heldCount && KmhDirectoryClient.Cards.Count == heldCount + 1;
                r.Add(("directory: a second page appends to a new list, not the one on screen", appended,
                       $"held {held.Count} of {heldCount}, now {KmhDirectoryClient.Cards.Count}"));

                int saves = 0;
                bool readerFree = true;
                KmhDirectoryCache.DuringSaveForTest = () =>
                {
                    saves++;
                    var reader = new Thread(() => { var _ = KmhDirectoryClient.Cards; }) { IsBackground = true };
                    reader.Start();
                    if (!reader.Join(1000)) readerFree = false;
                };
                KmhDirectoryClient.ResetForTest();
                Push(A, 1);
                bool firstSaved = saves == 1;
                for (int i = 0; i < 4; i++) Push(D, 10 + i);
                r.Add(("directory: the cache is written with the list lock free, so a draw never waits on the disk",
                       firstSaved && readerFree, firstSaved ? (readerFree ? "" : "A READER BLOCKED FOR THE WHOLE WRITE") : $"{saves} save(s)"));
                r.Add(("directory: a burst of pushed cards writes the cache once, not once per heartbeat", saves == 1,
                       $"{saves} write(s) for 5 pushes"));

                Answering(req => Page(req, CardJson(C, 3)), () => Refresh(null));
                bool refreshSaved = saves == 2 && File.Exists(cachePath)
                                 && File.ReadAllText(cachePath).IndexOf(C, StringComparison.Ordinal) >= 0;
                r.Add(("directory: a full refresh still writes the cache at once", refreshSaved, $"{saves} write(s)"));
                KmhDirectoryCache.DuringSaveForTest = null;

                // Switching directory while a refresh is in flight: its answer belongs to the old one.
                Answering(req =>
                {
                    if (req.RequestUri.AbsolutePath.EndsWith("/servers", StringComparison.Ordinal)) KmhDirectoryClient.ForgetDirectory();
                    return Page(req, CardJson(B, 4));
                }, () => Refresh(null));
                bool dropped = KmhDirectoryClient.Cards.Count == 0 && !File.Exists(cachePath)
                            && KmhDirectoryClient.State != GlobalLinkState.Online;
                r.Add(("directory: a refresh that lands after a directory switch is dropped, list and cache alike", dropped,
                       $"{KmhDirectoryClient.Cards.Count} row(s), cache {(File.Exists(cachePath) ? "WRITTEN" : "absent")}, {KmhDirectoryClient.State}"));

                Answering(_ => { KmhDirectoryClient.ForgetDirectory(); return Json("{}", HttpStatusCode.InternalServerError); },
                          () => Refresh(null));
                bool quiet = KmhDirectoryClient.State == GlobalLinkState.Unknown && KmhDirectoryClient.LastErrorCode == "";
                r.Add(("directory: the old directory failing does not mark the new one offline", quiet,
                       $"{KmhDirectoryClient.State} {KmhDirectoryClient.LastErrorCode}"));

                int stale = KmhDirectoryClient.Generation;
                Push(A, 1);
                KmhDirectoryClient.ForgetDirectory();
                typeof(KmhDirectoryClient).GetMethod("Persist", Hidden).Invoke(null, new object[] { stale, true });
                r.Add(("directory: a cache write for the previous directory is never made", !File.Exists(cachePath),
                       File.Exists(cachePath) ? "THE OLD DIRECTORY'S ROWS WERE WRITTEN BACK" : ""));

                int resyncBefore = resyncs;
                KmhDirectoryStream.ApplyFrom("{\"type\":\"hello\",\"revision\":1,\"server_count\":9,\"player_count\":9}", stale);
                bool socketIgnored = resyncs == resyncBefore && KmhDirectoryClient.ServerCount == 0;
                r.Add(("directory: a socket opened to the previous directory neither counts nor re-reads", socketIgnored,
                       $"resyncs +{resyncs - resyncBefore}, servers {KmhDirectoryClient.ServerCount}"));

                KmhDirectoryClient.ApplyPushedCard(DirectoryParse.Card(JObject.Parse(CardJson(C, 1))), stale);
                KmhDirectoryClient.ApplyPushedCounters(5, 5, stale);
                bool listIgnored = KmhDirectoryClient.Cards.Count == 0 && KmhDirectoryClient.ServerCount == 0;
                r.Add(("directory: a card or count from the previous directory is dropped at the list", listIgnored,
                       $"{KmhDirectoryClient.Cards.Count} row(s), {KmhDirectoryClient.ServerCount} server(s)"));
            }
            finally
            {
                KmhDirectoryCache.DuringSaveForTest = null;
                KmhDirectoryStream.Resync = priorResync;
                KmhDirectoryStream.Reset();
                KmhDirectoryClient.ResetForTest();
                KmhDirectoryCache.RootOverrideForTest = priorCache;
                Features.Servers.SeenServersStore.RootOverrideForTest = priorSeen;
                Features.Servers.SeenServersStore.ReloadForTest();
            }
        }

        private static void ServerSuppliedText(List<(string, bool, string)> r, string scratch)
        {
            const string hostile = "<size=300><color=#00000000>";
            ServerCardDto card = DirectoryParse.Card(new JObject
            {
                ["server_id"] = "x", ["rimworld_version"] = hostile, ["rwt_version"] = hostile, ["kmh_version"] = hostile,
            });
            bool parsed = card != null && NoMarkup(card.RimWorldVersion) && NoMarkup(card.RwtVersion) && NoMarkup(card.KmhVersion);
            r.Add(("server text: version strings from the directory are inert when parsed", parsed, card?.RwtVersion));

            var cached = new ServerCardDto { RimWorldVersion = hostile, RwtVersion = hostile, KmhVersion = hostile };
            cached.MakeInert();
            bool cacheInert = NoMarkup(cached.RimWorldVersion) && NoMarkup(cached.RwtVersion) && NoMarkup(cached.KmhVersion);
            r.Add(("server text: version strings loaded from the cache file are inert too", cacheInert, cached.KmhVersion));

            ServerCardDto legit = DirectoryParse.Card(new JObject { ["server_id"] = "y", ["rwt_version"] = "26.8.31.1" });
            r.Add(("server text: a real version is left exactly as sent, so it still compares",
                   legit != null && legit.RwtVersion == "26.8.31.1", legit?.RwtVersion));

            string wasName = KmhDispatcher.ServerName, wasBuild = KmhDispatcher.ServerBuild;
            try
            {
                KmhDispatcher.ServerName = "<size=300>Huge</size>" + new string('n', 400);
                KmhDispatcher.ServerBuild = "<color=red>rel-1";
                bool hello = NoMarkup(KmhDispatcher.ServerName) && KmhDispatcher.ServerName.Length <= KmhDispatcher.MaxServerName + 1
                          && NoMarkup(KmhDispatcher.ServerBuild);
                r.Add(("server text: a hello's server name and build are inert and bounded when stored", hello,
                       KmhDispatcher.ServerName.Length + " char(s)"));
                KmhDispatcher.ServerBuild = KmhProtocol.BuildVersion;
                r.Add(("server text: a real build is stored as sent, so a current server still reads current",
                       KmhDispatcher.ServerSupportsCurrentBuild, KmhDispatcher.ServerBuild));
            }
            finally { KmhDispatcher.ServerName = wasName; KmhDispatcher.ServerBuild = wasBuild; }

            string seenRoot = Path.Combine(scratch, "seen");
            Directory.CreateDirectory(seenRoot);
            string priorSeen = Features.Servers.SeenServersStore.RootOverrideForTest;
            try
            {
                Features.Servers.SeenServersStore.RootOverrideForTest = seenRoot;
                Features.Servers.SeenServersStore.ReloadForTest();
                Features.Servers.SeenServersStore.Record("hardening-seen", "<b>Big</b><size=400>", "203.0.113.5:25555",
                                                          "<color=red>k", "<size=9>r");
                Features.Servers.SeenServersStore.Entry e =
                    Features.Servers.SeenServersStore.All().Find(x => x.ServerId == "hardening-seen");
                bool recorded = e != null && NoMarkup(e.Name) && NoMarkup(e.KmhVersion) && NoMarkup(e.RwtVersion);
                r.Add(("server text: the server history stores a visited server's words inert", recorded, e?.Name));

                File.WriteAllText(Path.Combine(seenRoot, "seen-servers.json"),
                    "{\"servers\":[{\"server_id\":\"hardening-old\",\"name\":\"<size=400>Old\",\"endpoint\":\"h:1\","
                  + "\"kmh\":\"<b>k\",\"rwt\":\"<i>r\",\"lastSeen\":" + DateTime.UtcNow.Ticks + "}]}");
                Features.Servers.SeenServersStore.ReloadForTest();
                Features.Servers.SeenServersStore.Entry old =
                    Features.Servers.SeenServersStore.All().Find(x => x.ServerId == "hardening-old");
                bool loaded = old != null && NoMarkup(old.Name) && NoMarkup(old.KmhVersion) && NoMarkup(old.RwtVersion);
                r.Add(("server text: a history file written by an older build is made inert on load", loaded, old?.Name));

                r.Add(("server text: an endpoint is inert where the Servers screen draws it",
                       NoMarkup(Features.Servers.Dialog_KMHServers.Show("<b>evil</b>:1")), ""));
            }
            finally
            {
                Features.Servers.SeenServersStore.RootOverrideForTest = priorSeen;
                Features.Servers.SeenServersStore.ReloadForTest();
            }
        }

        // Guild names stay raw in the caches because the server matches on them; these are the copies that get drawn.
        private static void GuildNamesDrawInert(List<(string, bool, string)> r)
        {
            const string hostile = "<size=60>X</size>";
            string title = Features.Guilds.Dialog_KMHGuildHall.TitleFor(hostile);
            string prefix = UI.KmhText.Get("KMH_GH_Title", "Guild Hall - {0}", "");
            r.Add(("guild name: the Guild Hall title shows a guild named with markup as inert text",
                   NoMarkup(title) && title.StartsWith(prefix, StringComparison.Ordinal) && title.Contains("size=60"),
                   title));

            string plain = Features.Guilds.Dialog_KMHGuildHall.TitleFor("Plain Guild 7");
            string none = Features.Guilds.Dialog_KMHGuildHall.TitleFor(null);
            r.Add(("guild name: an ordinary guild name is shown exactly as it is",
                   plain == prefix + "Plain Guild 7" && none == prefix + UI.KmhText.Get("KMH_GH_NoGuildTitle", "(no guild)"), plain + " | " + none));

            var held = new Features.Sites.Dto.SiteEntry { OwnerKind = Features.Sites.Dto.SiteEntry.OwnerGuildKind, ControllingGuild = hostile };
            var reclaimed = new Features.Sites.Dto.SiteEntry { OutpostTemplate = "watchtower", CapturedBy = hostile };
            string controller = Features.Sites.SiteLabels.Controller(held);
            string heritage = Features.Sites.SiteLabels.HeritageLine(reclaimed);
            r.Add(("guild name: a site or outpost held by a guild shows its name inert",
                   NoMarkup(controller) && heritage.Length > 0 && heritage.IndexOf(hostile, StringComparison.Ordinal) < 0
                   && heritage.Contains("size=60"),
                   controller + " | " + heritage));

            // An officer's MOTD is drawn for every member, so it cannot restyle their Guild Hall.
            string motd = UI.KmhDisplayText.Motd("<size=300>HUGE</size> welcome");
            string empty = UI.KmhDisplayText.Motd("  ");
            bool drawsHelper = KmhIlReader.CallsMadeBy("KMHPatch.Features.Guilds.Dialog_KMHGuildHall", "DrawContents").Contains("Motd");
            r.Add(("guild text: the MOTD is drawn as inert text, by the Guild Hall itself",
                   NoMarkup(motd) && motd.Contains("size=300") && motd.Contains("welcome") && empty.Contains("no message of the day")
                   && drawsHelper,
                   $"{motd} | drawn through the helper={drawsHelper}"));

            string named = Features.LinkedAccounts.LinkedAccountsCache.Format("<color=#00000000>Ghost</color>");
            r.Add(("guild text: a player name with markup is drawn as text wherever names are listed",
                   NoMarkup(named) && named.Contains("Ghost") && named.Contains("color=#00000000"), named));

            string label = Features.Guilds.Dialog_KMHGuildHall.TransferLabel(hostile);
            string confirm = Features.Guilds.Dialog_KMHGuildHall.TransferConfirm(hostile);
            List<string> drawn = KmhIlReader.CallsMadeBy("KMHPatch.Features.Guilds.Dialog_KMHGuildHall", "DrawMemberActionButton");
            bool used = drawn.Contains("TransferLabel") && drawn.Contains("TransferConfirm");
            r.Add(("guild text: the ownership transfer offer and its confirmation name the member as inert text",
                   NoMarkup(label) && NoMarkup(confirm) && label.Contains("size=60") && confirm.Contains("size=60") && used,
                   $"{label} | {confirm} | drawn through both={used}"));
        }

        // Each access starts on another thread while the lock is held; one that does not wait can race the worker slots.
        private static void DmMapsLocked(List<(string, bool, string)> r)
        {
            Type dm = typeof(KmhGlobalDmClient);
            object gate = dm.GetField("_mapsLock", Hidden)?.GetValue(null);
            if (gate == null) { r.Add(("global dm: the map lock exists", false, "_mapsLock is gone")); return; }
            MethodInfo applyHistory = dm.GetMethod("ApplyHistory", Hidden);
            MethodInfo apply = dm.GetMethod("Apply", Hidden);
            MethodInfo readThrough = dm.GetMethod("ReadThrough", Hidden);

            var page = new GlobalHttp.Result
            {
                Ok = true, Status = 200,
                Body = JObject.Parse("{\"messages\":[{\"message_id\":\"m1\",\"body\":\"hi\"}],\"next_cursor\":\"c\",\"read_through\":\"m1\"}"),
            };
            var list = new GlobalHttp.Result
            {
                Ok = true, Status = 200,
                Body = JObject.Parse("{\"conversations\":[{\"player_global_id\":\"hardening-peer\",\"may_reply\":false}]}"),
            };

            var probes = new List<(string What, Action Act)>
            {
                ("reading a conversation",           () => KmhGlobalDmClient.History("hardening-peer")),
                ("asking whether a reply is allowed", () => KmhGlobalDmClient.MayReply("hardening-peer")),
                ("asking for older pages",           () => KmhGlobalDmClient.HasMore("hardening-peer")),
                ("reading the read mark",            () => readThrough.Invoke(null, new object[] { "hardening-peer" })),
                ("storing a fetched page",           () => applyHistory.Invoke(null, new object[] { "hardening-peer", page, false })),
                ("storing the conversation list",    () => apply.Invoke(null, new object[] { list })),
                ("identity teardown",                () => KmhGlobalDmClient.Reset()),
            };

            try
            {
                foreach ((string what, Action act) in probes)
                {
                    bool waited = false, finished;
                    Thread t = null;
                    using (var started = new ManualResetEventSlim(false))
                    {
                        Monitor.Enter(gate);
                        try
                        {
                            t = new Thread(() => { started.Set(); try { act(); } catch (Exception) { } }) { IsBackground = true };
                            t.Start();
                            // Timed from the moment it runs, so a slow thread start cannot pass for waiting.
                            if (started.Wait(3000)) waited = !t.Join(150);
                        }
                        finally { Monitor.Exit(gate); }
                        finished = t.Join(3000);
                    }
                    r.Add(($"global dm: {what} waits for the lock the workers write under", waited && finished,
                           waited ? (finished ? "" : "never finished") : "RAN WITHOUT THE LOCK"));
                }
            }
            finally { KmhGlobalDmClient.Reset(); }
        }

        private static void AccountWriteIsAtomic(List<(string, bool, string)> r, string scratch)
        {
            string root = Path.Combine(scratch, "account");
            Directory.CreateDirectory(root);
            string path = Path.Combine(root, "account.json");
            string prior = Features.Account.KmhAccountStore.RootOverrideForTest;
            try
            {
                Features.Account.KmhAccountStore.RootOverrideForTest = root;
                Features.Account.KmhAccountStore.ResetForTest();
                const string dir = "https://directory.test";
                Features.Account.KmhAccountStore.Save(dir, "token-one", "pg-1", "One");

                bool fired = false, intact = false;
                Features.Account.KmhAccountStore.DuringWriteForTest = () =>
                {
                    fired = true;
                    try { intact = (string)JObject.Parse(File.ReadAllText(path))["PlayerToken"] == "token-one"; }
                    catch (Exception) { intact = false; }
                };
                Features.Account.KmhAccountStore.Save(dir, "token-two", "pg-2", "Two");
                Features.Account.KmhAccountStore.DuringWriteForTest = null;

                bool landed = File.Exists(path) && (string)JObject.Parse(File.ReadAllText(path))["PlayerToken"] == "token-two";
                bool noTemp = Directory.GetFiles(root, "*.tmp").Length == 0;
                r.Add(("account: the stored credential is replaced whole, never truncated in place",
                       fired && intact && landed && noTemp,
                       $"hooked={fired}, old file intact meanwhile={intact}, new token landed={landed}, temp left={!noTemp}"));
            }
            finally
            {
                Features.Account.KmhAccountStore.DuringWriteForTest = null;
                Features.Account.KmhAccountStore.RootOverrideForTest = prior;
                Features.Account.KmhAccountStore.ResetForTest();
            }
        }

        private static void SteamLinkUrl(List<(string, bool, string)> r)
        {
            string official = GlobalHttp.OfficialBaseUrl;
            bool accepts = Features.Account.KmhAccountClient.DirectoryLinkUrl(official + "/link-account", official) == official + "/link-account"
                        && Features.Account.KmhAccountClient.DirectoryLinkUrl("http://localhost:5080/link-account", "http://localhost:5080") != null;
            string[] hostile =
            {
                "", null, "file:///c:/windows/system32/cmd.exe", "steam://run/294100", "javascript:alert(1)",
                "https://evil.test/link-account", "http://global.knappe0.games/link-account",
                "https://global.knappe0.games.evil.test/x", "https://user:pw@global.knappe0.games/x",
                "\\\\server\\share\\x.exe", "C:\\Windows\\notepad.exe",
            };
            string let = null;
            foreach (string h in hostile)
                if (Features.Account.KmhAccountClient.DirectoryLinkUrl(h, official) != null) let = h;
            if (Features.Account.KmhAccountClient.DirectoryLinkUrl("http://localhost:5080/x", official) != null) let = "http://localhost:5080/x";
            r.Add(("steam link: only the directory's own https page is ever opened", accepts && let == null,
                   accepts ? (let == null ? "" : "WOULD OPEN: " + let) : "the real link page was refused"));

            string priorBase = GlobalHttp.BaseUrl;
            string offered = "file:///c:/windows/system32/cmd.exe";
            HttpResponseMessage Challenge(HttpRequestMessage _)
                => Json("{\"challenge_id\":\"ch\",\"code\":\"ABCD-EFGH\",\"link_url\":\"" + offered + "\",\"expires_at\":"
                        + (DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 600) + "}");
            bool Settled()
            {
                for (int i = 0; i < 100 && Features.Account.KmhAccountClient.Busy; i++) Thread.Sleep(30);
                return !Features.Account.KmhAccountClient.Busy;
            }
            try
            {
                GlobalHttp.BaseUrl = official;
                Features.Account.KmhAccountClient.ResetForTest();
                bool ran = Answering(Challenge, () => { Features.Account.KmhAccountClient.StartLink(); return Settled(); });
                bool refused = ran && Features.Account.KmhAccountClient.State == Features.Account.AccountState.Failed
                            && Features.Account.KmhAccountClient.LinkUrl == "" && Features.Account.KmhAccountClient.DeepLink == "";
                r.Add(("steam link: a link page on another scheme is refused before it can be opened", refused,
                       $"{Features.Account.KmhAccountClient.State}, link '{Features.Account.KmhAccountClient.DeepLink}'"));

                offered = official + "/link-account";
                Features.Account.KmhAccountClient.ResetForTest();
                ran = Answering(Challenge, () => { Features.Account.KmhAccountClient.StartLink(); return Settled(); });
                bool good = ran && Features.Account.KmhAccountClient.State == Features.Account.AccountState.Waiting
                         && Features.Account.KmhAccountClient.DeepLink.StartsWith(official + "/link-account?code=", StringComparison.Ordinal);
                r.Add(("steam link: the directory's own link page still opens with the code", good,
                       $"{Features.Account.KmhAccountClient.State}, link '{Features.Account.KmhAccountClient.DeepLink}'"));
            }
            finally
            {
                GlobalHttp.BaseUrl = priorBase;
                Features.Account.KmhAccountClient.ResetForTest();
            }
        }

        private static void EnforcementText(List<(string, bool, string)> r)
        {
            string friendly = Features.Enforcement.EnforcementLockUI.FriendlyMod("<size=300>server.mod");
            r.Add(("enforcement text: a safe-mod id the server sent is inert in the lock notice", NoMarkup(friendly), friendly));

            bool pEnabled = Features.Enforcement.EnforcementCache.Enabled, pBypass = Features.Enforcement.EnforcementCache.AdminBypass;
            bool pPreserve = Features.Enforcement.EnforcementCache.PreservePersonal, pAdmin = Features.Enforcement.EnforcementCache.IsAdmin;
            bool pProfile = Features.Enforcement.EnforcementCache.HasProfile;
            var pSafe = new List<string>(Features.Enforcement.EnforcementCache.SafeMods);
            string pHash = Features.Enforcement.EnforcementCache.ServerProfileHash;
            var pOrder = new List<string>(Features.Enforcement.EnforcementCache.ModOrder);
            string pOrderHash = Features.Enforcement.EnforcementCache.ModOrderHash;
            long pRevision = Features.Enforcement.EnforcementCache.ProfileRevision;
            int pFiles = Features.Enforcement.EnforcementCache.ProfileFiles;
            string pServer = KmhDispatcher.ServerId;
            var pNames = KmhLocalMods.ActiveNames() as List<string>;
            var pIds = KmhLocalMods.ActivePackageIds() as List<string>;
            const string server = "hardening-consent";
            try
            {
                Features.Enforcement.EnforcementCache.Apply(false, true, false, false, false,
                    new List<string> { "<color=#00000000>hidden.safe", "fine.mod" }, "",
                    new List<string> { "ludeon.rimworld", "<size=300>evil.mod" }, "H");
                string safe = Features.Enforcement.EnforcementCache.SafeModsShown();
                r.Add(("enforcement text: the safe-mods list in the enforcement window is inert",
                       NoMarkup(safe) && safe.IndexOf("fine.mod", StringComparison.Ordinal) >= 0, safe));

                KmhDispatcher.ServerId = server;
                ModProfileClient.SeedForTest(server, new ModProfileClient.Entry
                {
                    Profile = new ModProfileDto
                    {
                        ServerId = server,
                        Required = new List<string> { "<color=#00000000>hidden.required", "Harmony" },
                        Optional = new List<string> { "<size=200>optional" },
                    },
                });
                KmhLocalMods.SeedForTest(new List<string> { "Harmony", "<b>LocalOnly</b>" });
                KmhLocalMods.SeedIdsForTest(new List<string> { "ludeon.rimworld" });

                string summary = Features.Enforcement.Dialog_KMHEnforcementConsent.BuildSummary();
                bool drawn = summary.IndexOf("‹size=300›evil.mod", StringComparison.Ordinal) >= 0
                          && summary.IndexOf("‹color=#00000000›hidden.required", StringComparison.Ordinal) >= 0;
                string leaked = null;
                foreach (string raw in new[] { "<size=300>", "<color=#00000000>", "<size=200>", "<b>LocalOnly" })
                    if (summary.IndexOf(raw, StringComparison.Ordinal) >= 0) leaked = raw;
                r.Add(("enforcement text: the consent dialog draws the server's mod list and names inert",
                       drawn && leaked == null, leaked == null ? (drawn ? "" : "the entries were not drawn at all") : "RAW MARKUP: " + leaked));
            }
            finally
            {
                KmhDispatcher.ServerId = pServer;
                ModProfileClient.ResetForTest();
                if (pNames != null) KmhLocalMods.SeedForTest(pNames);
                if (pIds != null) KmhLocalMods.SeedIdsForTest(pIds);
                Features.Enforcement.EnforcementCache.Apply(pEnabled, pBypass, pPreserve, pAdmin, pProfile, pSafe, pHash,
                                                            pOrder, pOrderHash, pRevision, pFiles);
            }
        }

        private static void VideoDownloadBounds(List<(string, bool, string)> r, string scratch)
        {
            string folder = Path.Combine(scratch, "video");
            Directory.CreateDirectory(folder);
            string priorFolder = Features.Chat.ChatVideoCache.FolderOverride;
            try
            {
                Features.Chat.ChatVideoCache.FolderOverride = folder;
                const long ceiling = 1024 * 1024;

                var d = new Features.Chat.ChatVideoCache.Download { File = Path.Combine(folder, "relay_720.mp4"), MaxBytes = ceiling };
                string part = d.File + ".part";
                var relay = new CountingStream(8L * 1024 * 1024);
                Features.Chat.ChatVideoCache.Receive(d, -1, () => relay, part);
                long onDisk = File.Exists(part) ? new FileInfo(part).Length : 0;
                bool capped = d.TooLarge && onDisk <= ceiling && relay.Served <= ceiling + 64 * 1024;
                r.Add(("video: a relay that keeps sending is cut off at the ceiling", capped,
                       $"{relay.Served} byte(s) taken, {onDisk} on disk, ceiling {ceiling}"));

                d.WorkerDone = true;
                Features.Chat.ChatVideoCache.Poll(d, 10f);
                bool reported = d.Done && !string.IsNullOrEmpty(d.Error) && !File.Exists(part);
                r.Add(("video: a download stopped at the ceiling ends as an error and leaves no part file", reported, d.Error));

                var declared = new Features.Chat.ChatVideoCache.Download { File = Path.Combine(folder, "declared_720.mp4"), MaxBytes = ceiling };
                bool opened = false;
                Features.Chat.ChatVideoCache.Receive(declared, 50L * 1024 * 1024 * 1024,
                    () => { opened = true; return new CountingStream(8L * 1024 * 1024); }, declared.File + ".part");
                bool early = declared.TooLarge && !opened && !File.Exists(declared.File + ".part");
                r.Add(("video: a declared size over the ceiling is refused before a byte is written", early,
                       $"opened={opened}"));

                // No length at all, which is how a chunked relay answers: bytes arriving are what count.
                var flowing = new Features.Chat.ChatVideoCache.Download { File = Path.Combine(folder, "flow.mp4"), MaxBytes = long.MaxValue };
                Features.Chat.ChatVideoCache.Poll(flowing, 1f);
                Interlocked.Add(ref flowing.Read, 4096);
                Features.Chat.ChatVideoCache.Poll(flowing, 60f);
                Interlocked.Add(ref flowing.Read, 4096);
                Features.Chat.ChatVideoCache.Poll(flowing, 120f);
                var stuck = new Features.Chat.ChatVideoCache.Download { File = Path.Combine(folder, "stuck.mp4"), MaxBytes = long.MaxValue };
                Features.Chat.ChatVideoCache.Poll(stuck, 1f);
                Interlocked.Add(ref stuck.Read, 10);
                Features.Chat.ChatVideoCache.Poll(stuck, 2f);
                Features.Chat.ChatVideoCache.Poll(stuck, 95f);
                r.Add(("video: a stream with no length is alive while bytes arrive, and stalled once they stop",
                       !flowing.Cancelled && stuck.Cancelled, $"flowing cancelled={flowing.Cancelled}, stuck cancelled={stuck.Cancelled}"));

                long bounded = Features.Chat.ChatVideoCache.CeilingFor(1080, 600);
                long whole = Features.Chat.ChatVideoCache.CeilingFor(1080, 0);
                bool ceilings = bounded >= 64L * 1024 * 1024 && bounded < whole
                             && Features.Chat.ChatVideoCache.CeilingFor(0, 600) == whole;
                r.Add(("video: the ceiling follows the server's length limit and never passes the cache limit", ceilings,
                       $"{bounded / (1024 * 1024)} MB for 10 min at 1080p, {whole / (1024 * 1024)} MB with no limit"));
            }
            finally { Features.Chat.ChatVideoCache.FolderOverride = priorFolder; }
        }

        private static void SubscribeShowsIds(List<(string, bool, string)> r)
        {
            var mods = new List<ModEntryDto>
            {
                new ModEntryDto { Name = "Harmony", Id = "2009463077", Kind = ModKindNames.Workshop },
                new ModEntryDto { Name = "<color=#ff0000>Harmony</color>", Id = "1111111111", Kind = ModKindNames.Workshop },
            };
            string text = string.Join("\n", Dialog_KMHPrepareJoin.SubscribeLines(mods).ToArray());
            bool ids = text.IndexOf("2009463077", StringComparison.Ordinal) >= 0 && text.IndexOf("1111111111", StringComparison.Ordinal) >= 0;
            bool inert = text.IndexOf("<color=#ff0000>", StringComparison.Ordinal) < 0;
            r.Add(("subscribe: the confirmation shows the Workshop id Steam will act on beside each server-written name",
                   ids && inert, ids ? (inert ? "" : "RAW MARKUP IN A NAME") : "AN ID IS MISSING: " + text));
        }

        private static void PresenceNeedsTheDialledServer(List<(string, bool, string)> r)
        {
            bool same = Features.Discord.KmhRichPresence.SameEndpoint("play.example.net:25560", "PLAY.example.net:25560")
                     && Features.Discord.KmhRichPresence.SameEndpoint("203.0.113.4:25555", "[::ffff:203.0.113.4]:25555")
                     && !Features.Discord.KmhRichPresence.SameEndpoint("203.0.113.4:25555", "203.0.113.4:25556")
                     && !Features.Discord.KmhRichPresence.SameEndpoint("203.0.113.4:25555", "198.51.100.8:25555")
                     && !Features.Discord.KmhRichPresence.SameEndpoint("", "203.0.113.4:25555")
                     && !Features.Discord.KmhRichPresence.SameEndpoint("203.0.113.4:25555", ":0");
            r.Add(("presence: a listed endpoint matches only the host and port actually dialled", same, ""));

            bool wasKmh = KmhDispatcher.IsKmhServer;
            string wasId = KmhDispatcher.ServerId;
            var card = new ServerCardDto
            {
                ServerId = new string('f', 32), Name = "Official One", JoinEndpoint = "official.example.net:25555",
                Official = true, FromPublicDirectory = true, MaxPlayers = 16, Players = 2, RequiresPassword = false, Status = "Online",
            };
            try
            {
                KmhDirectoryClient.SeedForTest(new List<ServerCardDto> { card }, GlobalLinkState.Online, false);
                KmhDispatcher.IsKmhServer = true;
                KmhDispatcher.ServerId = card.ServerId;

                Features.Discord.KmhDiscordRpc.Presence Shown()
                {
                    var ses = Features.Discord.KmhRichPresence.Session(KMHPatchMod.SettingsOrDefault, DateTime.UtcNow);
                    ses.Playing = true;
                    var idle = new Features.Discord.PresenceChoice { Kind = Features.Discord.PresenceKind.Colony, SinceUtc = DateTime.UtcNow };
                    return Features.Discord.KmhPresenceFormat.Compose(idle, ses, KMHPatchMod.SettingsOrDefault, 1, DateTime.UtcNow);
                }

                Features.Discord.KmhRichPresence.DialledForTest = "198.51.100.9:25555";
                Features.Discord.KmhDiscordRpc.Presence p = Shown();
                string all = p.Details + "|" + p.State + "|" + p.SmallImageText;
                bool notBorrowed = all.IndexOf("Official One", StringComparison.Ordinal) < 0
                                && p.SmallImageKey != "kmh_official_server_v1" && string.IsNullOrEmpty(p.JoinSecret)
                                && string.IsNullOrEmpty(p.PartyId)
                                && Features.Discord.KmhSteamPresence.ConnectFor(Features.Discord.KmhRichPresence.CardForCurrentServer()) == "";
                r.Add(("presence: a server claiming a listed id is not named, badged or advertised as it", notBorrowed,
                       notBorrowed ? p.State : $"SHOWN AS '{all}' badge '{p.SmallImageKey}'"));

                Features.Discord.KmhRichPresence.DialledForTest = card.JoinEndpoint;
                Features.Discord.KmhDiscordRpc.Presence q = Shown();
                r.Add(("presence: the listed server itself is still named when it is the one dialled",
                       q.State == "Official One" || !KMHPatchMod.SettingsOrDefault.DiscordShowServerName, q.State));

                // After a Direct Connect the card comes from a lookup by id, not the browser's list.
                KmhDirectoryClient.ResetForTest();
                var fetched = new ServerCardDto { ServerId = card.ServerId, Name = "Fetched One", JoinEndpoint = "fetched.example.net:25555", FromPublicDirectory = true };
                var cached = new ServerCardDto { ServerId = new string('e', 32), Name = "Cached One", JoinEndpoint = "cached.example.net:25555", FromPublicDirectory = false };
                typeof(KmhDirectoryClient).GetField("_byId", Hidden).SetValue(null,
                    new Dictionary<string, ServerCardDto>(StringComparer.Ordinal) { [fetched.ServerId] = fetched, [cached.ServerId] = cached });
                bool byId = Features.Discord.KmhRichPresence.CardFor(fetched.ServerId, "fetched.example.net:25555") == fetched
                         && Features.Discord.KmhRichPresence.CardFor(fetched.ServerId, "198.51.100.9:25555") == null
                         && Features.Discord.KmhRichPresence.CardFor(cached.ServerId, "cached.example.net:25555") == null;
                r.Add(("presence: a card looked up by id after a Direct Connect vouches only for its own address", byId, ""));
            }
            finally
            {
                Features.Discord.KmhRichPresence.DialledForTest = null;
                KmhDispatcher.IsKmhServer = wasKmh;
                KmhDispatcher.ServerId = wasId;
                KmhDirectoryClient.ResetForTest();
            }
        }

        private static void PublishPartSize(List<(string, bool, string)> r, string scratch)
        {
            bool pure = !Features.Enforcement.ConfigPublish.TryPartBytes(new JValue(0), out _)
                     && !Features.Enforcement.ConfigPublish.TryPartBytes(new JValue(-1), out _)
                     && !Features.Enforcement.ConfigPublish.TryPartBytes(new JValue(1023), out _)
                     && Features.Enforcement.ConfigPublish.TryPartBytes(new JValue(1024), out int lo) && lo == 1024
                     && Features.Enforcement.ConfigPublish.TryPartBytes(new JValue(32 * 1024), out int hi) && hi == 32 * 1024
                     && !Features.Enforcement.ConfigPublish.TryPartBytes(new JValue(32 * 1024 + 1), out _)
                     && !Features.Enforcement.ConfigPublish.TryPartBytes(new JValue("4096"), out _)
                     && !Features.Enforcement.ConfigPublish.TryPartBytes(JToken.Parse("123456789012345678901234567890"), out _)
                     && Features.Enforcement.ConfigPublish.TryPartBytes(null, out int absent) && absent == 32 * 1024;
            r.Add(("publish: a part size is accepted only between 1KB and the server's 32KB frame", pure, ""));

            string cfg = Path.Combine(scratch, "publish");
            Directory.CreateDirectory(cfg);
            // Barely compressible, so the good path below really takes several parts.
            byte[] noise = new byte[12000];
            new Random(263).NextBytes(noise);
            File.WriteAllText(Path.Combine(cfg, "Mod_HardeningProbe.xml"), "<x>" + Convert.ToBase64String(noise) + "</x>");
            string asked = "0";
            int parts = 0, emptyParts = 0;
            Features.Enforcement.ConfigPublish.SendForTest = (kind, data) =>
            {
                if (kind == KmhProtocol.Kind.EnforcementPublishBegin)
                    Features.Enforcement.ConfigPublish.OnAccepted(JObject.Parse(
                        "{\"upload_id\":\"u\",\"part_bytes\":" + asked + ",\"credit\":8,\"wanted\":[0]}"));
                else if (kind == KmhProtocol.Kind.EnforcementPublishPart)
                {
                    Interlocked.Increment(ref parts);
                    if (string.IsNullOrEmpty((string)JObject.FromObject(data)["data"])) Interlocked.Increment(ref emptyParts);
                    Features.Enforcement.ConfigPublish.OnAck(null);
                }
                else if (kind == KmhProtocol.Kind.EnforcementPublishCommit)
                    Features.Enforcement.ConfigPublish.OnResult(JObject.Parse("{\"ok\":true,\"revision\":1,\"hash\":\"abc\",\"files\":1}"));
                return true;
            };

            bool Publish(out string detail)
            {
                parts = 0; emptyParts = 0;
                using (var cts = new CancellationTokenSource())
                {
                    // Off this thread: the loop this guards against never yields, so it would never hand back a task.
                    Task t = Task.Run(() => Features.Enforcement.ConfigPublish.RunForTest(cfg, cts.Token));
                    bool ended = t.Wait(3000);
                    if (!ended) { cts.Cancel(); try { t.Wait(3000); } catch (Exception) { } }
                    detail = $"part_bytes {asked}: {(ended ? "" : "STILL SENDING, ")}{Features.Enforcement.ConfigPublish.Phase}, "
                           + $"{parts} part(s), {emptyParts} empty";
                    return ended;
                }
            }

            try
            {
                string refusedDetail = "";
                bool allRefused = true;
                foreach (string bad in new[] { "0", "-5", "2147483647" })
                {
                    asked = bad;
                    bool ended = Publish(out string detail);
                    bool refused = ended && Features.Enforcement.ConfigPublish.Phase == Features.Enforcement.PublishPhase.Failed && parts == 0;
                    if (!refused) { allRefused = false; refusedDetail = detail; }
                }
                r.Add(("publish: a hostile part size fails the publish cleanly before any part is sent", allRefused, refusedDetail));

                asked = "4096";
                bool ok = Publish(out string good);
                bool published = ok && Features.Enforcement.ConfigPublish.Phase == Features.Enforcement.PublishPhase.Done
                              && parts >= 2 && emptyParts == 0;
                r.Add(("publish: a server asking for an ordinary part size still publishes", published, good));
            }
            finally
            {
                Features.Enforcement.ConfigPublish.SendForTest = null;
                typeof(Features.Enforcement.ConfigPublish).GetField("<Phase>k__BackingField", Hidden)
                    ?.SetValue(null, Features.Enforcement.PublishPhase.Idle);
                typeof(Features.Enforcement.ConfigPublish).GetField("<Message>k__BackingField", Hidden)?.SetValue(null, "");
            }
        }

        private static void DuplicateCopies(List<(string, bool, string)> r)
        {
            var two = new List<KmhDuplicateModCheck.Copy>
            {
                new KmhDuplicateModCheck.Copy { Path = "Mods/3005289691", Active = true },
                new KmhDuplicateModCheck.Copy { Path = "Mods/3005289691.kmh-previous-20260101000000" },
            };
            string said = KmhDuplicateModCheck.Describe(two, "RimWorld Together", KmhDuplicateModCheck.RwtPackageId);
            bool named = said.IndexOf(KmhDuplicateModCheck.RwtPackageId, StringComparison.Ordinal) >= 0
                      && said.IndexOf("[RUNNING]", StringComparison.Ordinal) >= 0
                      && KmhDuplicateModCheck.Describe(two.GetRange(0, 1), "RimWorld Together", KmhDuplicateModCheck.RwtPackageId) == "";
            r.Add(("duplicate copies: two copies of RimWorld Together are reported like two of KMH", named, said));

            var renamed = new List<KmhDuplicateModCheck.Copy>
            {
                new KmhDuplicateModCheck.Copy { Path = "Mods/ColonyNexus", Active = true },
                new KmhDuplicateModCheck.Copy { Path = "Mods/<b>KMHPatch</b>" },
            };
            string notice = KmhDuplicateModCheck.Notice(renamed);
            bool shown = notice.IndexOf("Mods/ColonyNexus", StringComparison.Ordinal) >= 0
                      && notice.IndexOf("KMHPatch", StringComparison.Ordinal) >= 0
                      && notice.IndexOf("<b>", StringComparison.Ordinal) < 0
                      && KmhDuplicateModCheck.Notice(renamed.GetRange(0, 1)) == "";
            r.Add(("duplicate copies: an old KMHPatch folder beside the new one is shown to the player, inert, and one copy says nothing",
                   shown, notice));
        }

        // RimWorld 1.7 leaves the ignored copy out of its own list, so the old folder is found on disk.
        private static void LegacyFolder(List<(string, bool, string)> r, string scratch)
        {
            string mods = Path.Combine(scratch, "legacy-mods"), old = Path.Combine(mods, "KMHPatch"), now = Path.Combine(mods, "ColonyNexus");
            Directory.CreateDirectory(Path.Combine(old, "About"));
            File.WriteAllText(Path.Combine(old, "About", "About.xml"), "<ModMetaData><packageId>knappe.kmh.patch</packageId></ModMetaData>");
            var found = KmhDuplicateModCheck.LegacyCopy(mods, now);
            bool seen = found != null && !found.Active && found.Path == old;
            bool selfIsNot = KmhDuplicateModCheck.LegacyCopy(mods, old) == null;
            File.WriteAllText(Path.Combine(old, "About", "About.xml"), "<ModMetaData><packageId>someone.else</packageId></ModMetaData>");
            bool otherIsNot = KmhDuplicateModCheck.LegacyCopy(mods, now) == null;
            bool absentIsNot = KmhDuplicateModCheck.LegacyCopy(Path.Combine(scratch, "no-mods"), now) == null;
            r.Add(("duplicate copies: an old Mods\\KMHPatch is found on disk, but not when it is the running copy or another mod",
                   seen && selfIsNot && otherIsNot && absentIsNot, $"seen={seen} self={selfIsNot} other={otherIsNot} absent={absentIsNot}"));
        }

        private static void DataFolderMove(List<(string, bool, string)> r, string scratch)
        {
            string Dir(string root, string name) => Path.Combine(root, name);
            string newName = Persistence.KmhDataFolder.Name, oldName = Persistence.KmhDataFolder.LegacyName;

            string a = Dir(scratch, "data-move");
            Directory.CreateDirectory(Dir(a, oldName));
            File.WriteAllText(Path.Combine(Dir(a, oldName), "account.json"), "mine");
            string got = Persistence.KmhDataFolder.Resolve(a, out bool moved);
            bool movedOver = moved && got == Dir(a, newName) && !Directory.Exists(Dir(a, oldName))
                          && File.ReadAllText(Path.Combine(got, "account.json")) == "mine";
            r.Add(("data folder: KMH-Patch moves to Colony Nexus once, account file included", movedOver, $"moved={moved} {got}"));

            string b = Dir(scratch, "data-both");
            Directory.CreateDirectory(Dir(b, newName));
            Directory.CreateDirectory(Dir(b, oldName));
            File.WriteAllText(Path.Combine(Dir(b, oldName), "older.json"), "old");
            string kept = Persistence.KmhDataFolder.Resolve(b, out bool movedB);
            bool untouched = !movedB && kept == Dir(b, newName) && File.Exists(Path.Combine(Dir(b, oldName), "older.json"));
            r.Add(("data folder: with both present the new one is used and the old one is left alone", untouched, $"moved={movedB} {kept}"));

            string c = Dir(scratch, "data-none");
            Directory.CreateDirectory(c);
            string fresh = Persistence.KmhDataFolder.Resolve(c, out bool movedC);
            r.Add(("data folder: a fresh install uses Colony Nexus and creates nothing yet",
                   !movedC && fresh == Dir(c, newName) && !Directory.Exists(Dir(c, oldName)), fresh));
        }
    }
}
