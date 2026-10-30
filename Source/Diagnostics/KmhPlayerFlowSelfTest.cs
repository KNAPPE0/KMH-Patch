using System;
using System.Collections.Generic;
using KMHPatch.Features.Federation;
using KMHPatch.Features.Federation.Dto;
using KMHPatch.Features.Global;
using KMHPatch.Features.Guilds;
using KMHPatch.Features.Guilds.Dto;
using KMHPatch.Features.Quests.Dto;
using KMHPatch.Features.Sites;
using KMHPatch.Features.Sites.Dto;
using Newtonsoft.Json;

namespace KMHPatch.Diagnostics
{
    internal static class KmhPlayerFlowSelfTest
    {
        public static List<(string, bool, string)> Run()
        {
            var r = new List<(string, bool, string)>();
            void Check(string name, Func<(bool ok, string detail)> probe)
            {
                try { (bool ok, string detail) = probe(); r.Add((name, ok, detail)); }
                catch (Exception ex) { r.Add((name, false, ex.GetType().Name + ": " + ex.Message)); }
            }

            Check("typed numbers: '1,5' and '1.5' are one and a half, '1000' a thousand, '0.125' kept", () =>
            {
                bool Is(string s, decimal want) => UI.KmhNumberInput.TryParse(s, out decimal v, out bool amb) && !amb && v == want;
                bool ok = Is("1,5", 1.5m) && Is("1.5", 1.5m) && Is("1000", 1000m) && Is(" 12 ", 12m) && Is("0.125", 0.125m) && Is("0,55", 0.55m);
                return (ok, "1,5 / 1.5 / 1000 / 12 / 0.125 / 0,55");
            });

            Check("typed numbers: '1,000' and '1.000' are refused as ambiguous, never read as 1", () =>
            {
                bool Refused(string s) => !UI.KmhNumberInput.TryParse(s, out decimal v, out bool amb) && amb;
                bool ok = Refused("1,000") && Refused("1.000") && Refused("12.500") && Refused("1,000.50") && Refused("1.000,5");
                return (ok, "1,000 / 1.000 / 12.500 / 1,000.50 / 1.000,5");
            });

            // A want must take a decimal price where a listing does, such as 0.55.
            Check("want board: a price per unit takes 0.55 where the server does, whole silver where it does not, and the escrow rounds up", () =>
            {
                bool dec = Features.WantBoard.Dialog_KMHPostWant.TryParsePrice("0,55", true, out int m1, out _);
                bool whole = Features.WantBoard.Dialog_KMHPostWant.TryParsePrice("12", false, out int m2, out _);
                bool oldServer = Features.WantBoard.Dialog_KMHPostWant.TryParsePrice("0.55", false, out _, out string why);
                bool zero = Features.WantBoard.Dialog_KMHPostWant.TryParsePrice("0", true, out _, out _);
                long escrow = Features.WantBoard.Dialog_KMHPostWant.EscrowFor(550, 7);
                var saved = SubProtocol.KmhCapabilities.SaveForTest();
                bool offered, notOffered;
                try
                {
                    SubProtocol.KmhCapabilities.Apply(SubProtocol.KmhCapabilities.WantDecimalPrices + ",federation", present: true);
                    offered = Features.WantBoard.Dialog_KMHPostWant.Decimals;
                    SubProtocol.KmhCapabilities.Apply("federation", present: true);
                    notOffered = !Features.WantBoard.Dialog_KMHPostWant.Decimals;
                }
                finally { SubProtocol.KmhCapabilities.RestoreForTest(saved); }
                if (!offered || !notOffered) return (false, $"decimals offered with the capability={offered}, withheld without it={notOffered}");
                var legacy = new Features.WantBoard.Dto.WantDto { UnitPriceSilver = 3 };
                var exact = new Features.WantBoard.Dto.WantDto { UnitPriceSilver = 1, UnitPriceMilli = 550 };
                bool ok = dec && m1 == 550 && whole && m2 == 12000 && !oldServer && !string.IsNullOrEmpty(why) && !zero && escrow == 4
                       && legacy.EffectiveMilli == 3000 && exact.EffectiveMilli == 550 && UI.SilverFmt.FormatMilli(3000) == UI.SilverFmt.Format(3);
                return (ok, $"0,55 -> {m1}, 12 -> {m2}, 0.55 on an older server refused={!oldServer} ({why}), 0 refused={!zero}, 7 x 0.55 escrows {escrow}, "
                          + $"older want {legacy.EffectiveMilli}, exact {exact.EffectiveMilli}");
            });

            Check("guild vault: the snapshot's goods are read, counted and named largest first, inert", () =>
            {
                GuildSnapshot g = JsonConvert.DeserializeObject<GuildSnapshot>(
                    "{\"guild_items\":{\"Steel\":80,\"Gold\":5,\"Plasteel\":12},"
                    + "\"guild_payloads\":[{\"def_name\":\"MedicineIndustrial\",\"stack_count\":30,\"fingerprint\":\"fp\"}]}");
                string two = Dialog_KMHGuildHall.VaultItemsSummary(g, k => k == "Gold" ? "<color=red>Gold</color>" : k, 2);
                string all = Dialog_KMHGuildHall.VaultItemsSummary(g, k => k == "Gold" ? "<color=red>Gold</color>" : k, int.MaxValue);
                string none = Dialog_KMHGuildHall.VaultItemsSummary(new GuildSnapshot(), k => k, 4);
                bool ok = Dialog_KMHGuildHall.VaultUnits(g) == 127
                          && two.IndexOf("80× Steel", StringComparison.Ordinal) >= 0 && two.IndexOf("30× MedicineIndustrial", StringComparison.Ordinal) >= 0
                          && two.Contains(UI.KmhText.Get("KMH_GH_PlusMore", "(+{0} more)", 2)) && !two.Contains("Plasteel")
                          && all.Contains("12× Plasteel") && !all.Contains("<color=red>") && none.Contains(UI.KmhText.Get("KMH_GH_None", "none"));
                return (ok, $"units={Dialog_KMHGuildHall.VaultUnits(g)} two='{two}' all='{all}'");
            });

            Check("guild vault: the withdraw button follows the server's rank rule - a rank capped at 0 cannot take goods", () =>
            {
                var s = new GuildSettingsDto { MemberDailyWithdrawCap = 0, OfficerDailyWithdrawCap = 100, ModeratorDailyWithdrawCap = 0, AdminDailyWithdrawCap = 0 };
                var g = new GuildSnapshot { Settings = s };
                string got = string.Join(",", new[]
                {
                    Dialog_KMHGuildHall.MayWithdrawFromVault(g, GuildMemberDto.RankOwner),
                    Dialog_KMHGuildHall.MayWithdrawFromVault(g, GuildMemberDto.RankAdmin),
                    Dialog_KMHGuildHall.MayWithdrawFromVault(g, GuildMemberDto.RankModerator),
                    Dialog_KMHGuildHall.MayWithdrawFromVault(g, GuildMemberDto.RankOfficer),
                    Dialog_KMHGuildHall.MayWithdrawFromVault(g, GuildMemberDto.RankMember),
                    Dialog_KMHGuildHall.MayWithdrawFromVault(g, null),
                });
                return (got == "True,False,False,True,False,False", got);
            });

            // A proposal must show at the guild it was sent to, and its sender must not be offered "Accept".
            Check("guild hall: a proposal to this guild shows with its sender, ours shows as sent, and a rival reads as a label", () =>
            {
                GuildSnapshot g = JsonConvert.DeserializeObject<GuildSnapshot>(
                    "{\"relationships\":{\"Sent\":\"allied_requested\",\"Ally\":\"allied\",\"Foe\":\"hostile\"},\"incoming_proposals\":[\"Asker\"]}");
                string line = Dialog_KMHGuildHall.FormatDiplomacy(g.Relationships, g.IncomingProposals);
                bool ok = Dialog_KMHGuildHall.RelationLabel(g, "Asker") == UI.KmhText.Get("KMH_Guild_RelIncoming", "proposes an alliance to you")
                          && Dialog_KMHGuildHall.RelationLabel(g, "sent") == UI.KmhText.Get("KMH_Guild_RelSent", "proposal sent")
                          && Dialog_KMHGuildHall.RelationLabel(g, "Foe") == UI.KmhText.Get("KMH_Guild_RelRival", "rival")
                          && Dialog_KMHGuildHall.RelationLabel(g, "Nobody") == ""
                          && line.Contains("Asker") && line.Contains(UI.KmhText.Get("KMH_Guild_DipIncoming", "Proposals to you:"))
                          && line.Contains(UI.KmhText.Get("KMH_Guild_DipSent", "Proposal sent:")) && !line.Contains("Hostile")
                          && new GuildSnapshot().IncomingProposals != null;
                return (ok, line);
            });

            Check("federation window: the sync line names the player's real state, and 'not read yet' only for a linked guild", () =>
            {
                const long now = 1700100000L;
                FederationSnapshot Snap(bool inGuild, bool linked, long synced, bool stale, bool available, long until) => new FederationSnapshot
                {
                    InGuild = inGuild, Linked = linked, SyncedAt = synced, Stale = stale, Available = available,
                    Unavailable = available ? "" : "global.unreachable", BenefitsUntil = until,
                    Link = new FederationLink { LinkId = linked ? "link-self" : "", State = linked ? "Active" : "" },
                };
                string noGuild = FederationText.SyncLine(Snap(false, false, 0, false, true, 0), now);
                string unlinked = FederationText.SyncLine(Snap(true, false, 0, false, true, 0), now);
                string notRead = FederationText.SyncLine(Snap(true, true, 0, false, true, 0), now);
                string fresh = FederationText.SyncLine(Snap(true, true, now - 300, false, true, now + 3600), now);
                string down = FederationText.SyncLine(Snap(true, true, now - 7200, true, false, now + 250000), now);
                string paused = FederationText.SyncLine(Snap(true, true, now - 400000, true, false, now - 100), now);
                string never = UI.KmhText.Get("KMH_Fed_NotSynced", "Not read from Colony Nexus Global yet");
                bool ok = noGuild == UI.KmhText.Get("KMH_Fed_SyncNoGuild", "Join a guild first")
                          && unlinked == UI.KmhText.Get("KMH_Fed_SyncNotLinked", "Your guild is not in a federation")
                          && notRead == never && fresh != never && fresh.Contains("5")
                          && down.Contains(FederationText.When(now + 250000)) && paused != down && !paused.Contains(FederationText.When(now - 100));
                return (ok, $"{noGuild} | {unlinked} | {notRead} | {fresh} | {down} | {paused}");
            });

            Check("federation window: 'You can' follows the server's may_manage and may_lead, and says what does not count yet", () =>
            {
                var live = new FederationLink { LinkId = "link-self", State = "Active" };
                var lead = new FederationSnapshot { InGuild = true, Linked = true, Link = live, MayManage = true, MayLead = true, Verified = true };
                var member = new FederationSnapshot { InGuild = true, Linked = true, Link = live, ManageWhyCode = "rank.manage" };
                List<string> leadCan = Dialog_KMHFederation.YouCan(lead), memberCan = Dialog_KMHFederation.YouCan(member);
                List<string> leadNot = Dialog_KMHFederation.NotYet(lead), memberNot = Dialog_KMHFederation.NotYet(member);
                string leads = UI.KmhText.Get("KMH_Fed_CanLead", "Lead it: edit the profile, remove member servers, hand over leadership or disband");
                string counts = UI.KmhText.Get("KMH_Fed_NotCounting", "What you do does not count toward a federation until Colony Nexus Global confirms who you are on this server.");
                bool ok = leadCan.Contains(leads) && leadCan.Count == 4 && leadNot.Count == 0
                          && memberCan.Count == 1 && !memberCan.Contains(leads)
                          && memberNot.Contains(FederationText.Why("rank.manage", "")) && memberNot.Contains(counts);
                return (ok, $"lead can {leadCan.Count}/not {leadNot.Count}; member can {memberCan.Count}/not {memberNot.Count}");
            });

            Check("join ticket: loopback, private, link-local and carrier-grade NAT addresses read as local; public ones do not", () =>
            {
                string[] local = { "127.0.0.1:25555", "192.168.1.5:25555", "10.0.0.2:1", "172.20.1.1:1", "100.64.0.1:1",
                                   "169.254.0.1:1", "localhost:25555", "box.lan:1", "[::1]:25555", "[fd00::1]:1", "[fe80::1]:1" };
                string[] remote = { "8.8.8.8:25555", "rwt.knappe0.games:25555", "172.32.0.1:1", "100.128.0.1:1", "[2001:db8::1]:1", "" };
                var wrong = new List<string>();
                foreach (string a in local) if (!KmhJoinTicketClient.IsLocalAddress(a)) wrong.Add("local " + a);
                foreach (string a in remote) if (KmhJoinTicketClient.IsLocalAddress(a)) wrong.Add("remote " + a);
                return (wrong.Count == 0, wrong.Count == 0 ? $"{local.Length} local, {remote.Length} not" : string.Join(", ", wrong));
            });

            Check("join ticket: a refusal keeps Global's code, and the words name both addresses for a local connection", () =>
            {
                string code = KmhJoinTicketClient.RefusalCode(new GlobalHttp.Result { Status = 409, ErrorCode = KmhJoinTicketClient.EndpointMismatch });
                string down = KmhJoinTicketClient.RefusalCode(new GlobalHttp.Result { Status = 0 });
                string bare = KmhJoinTicketClient.RefusalCode(new GlobalHttp.Result { Status = 404 });
                string local = KmhJoinTicketClient.ProblemText(code, "", "127.0.0.1:25555", "rwt.knappe0.games:25555");
                string unknownPublic = KmhJoinTicketClient.ProblemText(code, "", "192.168.0.9:25555", "");
                string other = KmhJoinTicketClient.ProblemText(code, "", "203.0.113.7:25555", "rwt.knappe0.games:25555");
                string signedOut = KmhJoinTicketClient.ProblemText(KmhJoinTicketClient.SignedOut, "", "", "");
                bool ok = code == "KMH-SRV-321" && down == KmhJoinTicketClient.Unreachable && bare == "http.404"
                          && local.Contains("local address (127.0.0.1:25555)") && local.Contains("(rwt.knappe0.games:25555)")
                          && unknownPublic.Contains("server browser")
                          && other.Contains("203.0.113.7:25555") && !other.Contains("local address")
                          && signedOut.Contains("not signed in") && KmhJoinTicketClient.ProblemText("", "", "x", "y") == "";
                return (ok, $"code={code} down={down} bare={bare} | {local} | {other}");
            });

            Check("join ticket: the server hears only which kind of unverified, never an address", () =>
            {
                string got = string.Join(",", new[]
                {
                    KmhJoinTicketClient.ServerClass(KmhJoinTicketClient.SignedOut, ""),
                    KmhJoinTicketClient.ServerClass(KmhJoinTicketClient.EndpointMismatch, "127.0.0.1:1"),
                    KmhJoinTicketClient.ServerClass(KmhJoinTicketClient.EndpointMismatch, "203.0.113.7:1"),
                    KmhJoinTicketClient.ServerClass(KmhJoinTicketClient.Unreachable, ""),
                    KmhJoinTicketClient.ServerClass("KMH-PLY-999", "127.0.0.1:1"),
                });
                return (got == "signed_out,local_address,other_address,unconfirmed,unconfirmed", got);
            });

            Check("join ticket: a refusal is retried on a growing delay, and a sign-in mid-session presents at once", () =>
            {
                var delays = new List<double>();
                for (int n = 1; n <= 9; n++) delays.Add(KmhJoinTicketClient.Backoff(n).TotalSeconds);
                bool growing = true;
                for (int i = 1; i < delays.Count; i++) if (delays[i] < delays[i - 1]) growing = false;
                DateTime now = DateTime.UtcNow, later = now.AddSeconds(30);
                bool due = !KmhJoinTicketClient.RetryDue("", true, now, now)
                           && KmhJoinTicketClient.RetryDue(KmhJoinTicketClient.SignedOut, true, now, DateTime.MaxValue)
                           && !KmhJoinTicketClient.RetryDue(KmhJoinTicketClient.SignedOut, false, now, now)
                           && !KmhJoinTicketClient.RetryDue(KmhJoinTicketClient.EndpointMismatch, true, now, later)
                           && KmhJoinTicketClient.RetryDue(KmhJoinTicketClient.EndpointMismatch, true, later, later)
                           && !KmhJoinTicketClient.RetryDue(KmhJoinTicketClient.EndpointMismatch, false, later, later);
                bool ok = delays[0] == 15 && delays[1] == 30 && delays[delays.Count - 1] == 600 && growing && due;
                return (ok, $"delays={string.Join("/", delays)} retryRules={due}");
            });

            Check("federation: a reason code is shown in the player's words, the server's English kept for a code this build lacks", () =>
            {
                string local = FederationText.Why("account.local_address", "server words");
                string rank = FederationText.Why("rank.manage", "server words");
                string unknown = FederationText.Why("account.something_new", "server words");
                string none = FederationText.Why("", "server words");
                string off = FederationText.Why("global.off", "server words");
                var s = new FederationSnapshot { VerifiedWhyCode = "account.signed_out", VerifiedWhy = "server says sign in" };
                string fromClient = Dialog_KMHFederation.UnverifiedWhy(s, "client knows better");
                string fromServer = Dialog_KMHFederation.UnverifiedWhy(s, "");
                bool ok = local.Contains("local or private address") && rank.Contains("Admin") && unknown == "server words"
                          && none == "server words" && off == FederationText.Unavailable("global.off")
                          && fromClient == "client knows better" && fromServer.Contains("Account") && fromServer != "server says sign in";
                return (ok, $"local='{local}' unknown='{unknown}' fromServer='{fromServer}'");
            });

            Check("account: this server's verification reads as the server's acceptance, as the client's reason, or as waiting", () =>
            {
                string yes = Dialogs.Dialog_KMHAccount.ServerVerificationText(true, "ignored");
                string why = Dialogs.Dialog_KMHAccount.ServerVerificationText(false, "because local");
                string wait = Dialogs.Dialog_KMHAccount.ServerVerificationText(false, "");
                bool ok = yes.Contains("This server accepted") && why == "because local" && wait.Contains("Waiting");
                return (ok, $"'{yes}' / '{why}' / '{wait}'");
            });

            Check("account: signed out, the Account window points at its own Steam section, not at itself; other reasons pass through", () =>
            {
                string general = KmhJoinTicketClient.ProblemText(KmhJoinTicketClient.SignedOut, "", "", "");
                string here = Dialogs.Dialog_KMHAccount.ProblemInThisWindow(KmhJoinTicketClient.SignedOut, general);
                string other = Dialogs.Dialog_KMHAccount.ProblemInThisWindow("something.else", "because local");
                bool ok = general.Contains("under Account") && !here.Contains("under Account") && here.Contains("this window")
                          && other == "because local";
                return (ok, $"here='{here}' other='{other}'");
            });

            Check("dashboard: a poster's quest awaiting review counts as theirs and as awaiting review", () =>
            {
                var qs = new List<QuestEntry>
                {
                    new QuestEntry { State = QuestEntry.StateOpen, PosterUsername = "me" },
                    new QuestEntry { State = QuestEntry.StatePendingReview, Kind = QuestEntry.KindCustom, PosterUsername = "me", ClaimedByUsername = "bob" },
                    new QuestEntry { State = QuestEntry.StateSubmitted, Kind = QuestEntry.KindBounty, PosterUsername = "me", ClaimedByUsername = "bob" },
                    new QuestEntry { State = QuestEntry.StateClaimed, Kind = QuestEntry.KindBounty, PosterUsername = "me", ClaimedByUsername = "bob" },
                    new QuestEntry { State = QuestEntry.StatePendingReview, Kind = QuestEntry.KindCustom, PosterUsername = "ann", ClaimedByUsername = "me" },
                    new QuestEntry { State = QuestEntry.StateCompleted, PosterUsername = "me" },
                    null,
                };
                var (open, inFlight, posted, review) = UI.KMHDashboard.QuestCounts(qs, "me");
                bool ok = open == 1 && inFlight == 1 && posted == 4 && review == 2;
                return (ok, $"open={open} inFlight={inFlight} posted={posted} awaitingReview={review}");
            });

            Check("sites: a guild site's own output is offered only the guild vault and storage, named as the guild's", () =>
            {
                var guild = Dialog_KMHSites.DestChoices(guildOutput: true, hasStorage: true);
                var own = Dialog_KMHSites.DestChoices(guildOutput: false, hasStorage: false);
                string g = string.Join(",", guild.ConvertAll(c => c.Dest));
                string o = string.Join(",", own.ConvertAll(c => c.Dest));
                bool ok = g == SiteEntry.DestTreasury + "," + SiteEntry.DestStorage && guild[0].Label == "Guild vault"
                          && o == string.Join(",", SiteEntry.DestTreasury, SiteEntry.DestCaravan, SiteEntry.DestMarketplace, SiteEntry.DestStorage)
                          && own[0].Label == "Treasury" && own[3].Label.Contains("build a storage");
                return (ok, $"guild=[{g}] own=[{o}]");
            });

            Social(Check);
            ColonyStats(Check);
            Standings(Check);
            QuickAdd(Check);
            return r;
        }

        private static void QuickAdd(Action<string, Func<(bool ok, string detail)>> Check)
        {
            Check("friends: Quick Add sends a code as a target and an ID as an ID, says sent only on Global's yes, and says why on a no", () =>
            {
                Features.Friends.KmhFriendsClient.Saved saved = Features.Friends.KmhFriendsClient.SaveForTest();
                bool http = GlobalHttp.SuppressForTest, muted = Notifications.KmhNotifications.Muted;
                bool noSteam = Features.Account.KmhSteamPersona.AssumeNoSteamForTest;
                string root = Features.Account.KmhAccountStore.RootOverrideForTest;
                Func<System.Net.Http.HttpRequestMessage, System.Net.Http.HttpResponseMessage> prior = GlobalHttp.RespondForTest;
                var gate = new System.Threading.ManualResetEventSlim(false);
                string sentBody = "", answer = ""; int status = 200;
                string bea = new string('b', 32);
                Features.Friends.KmhFriendsClient.AddStage Settle()
                {
                    for (int i = 0; i < 200 && Features.Friends.KmhFriendsClient.AddState == Features.Friends.KmhFriendsClient.AddStage.Sending; i++)
                        System.Threading.Thread.Sleep(10);
                    for (int i = 0; i < 100 && Features.Friends.KmhFriendsClient.Busy; i++) System.Threading.Thread.Sleep(10);
                    return Features.Friends.KmhFriendsClient.AddState;
                }
                try
                {
                    GlobalHttp.SuppressForTest = true;
                    Notifications.KmhNotifications.Muted = true;
                    Features.Account.KmhSteamPersona.AssumeNoSteamForTest = true;
                    Features.Account.KmhAccountStore.RootOverrideForTest = System.IO.Path.Combine(
                        System.IO.Path.GetTempPath(), "kmh-quickadd-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                    Features.Account.KmhAccountStore.ResetForTest();
                    Features.Account.KmhAccountStore.Save(GlobalHttp.BaseUrl, "test-token", new string('f', 32), "me");
                    GlobalHttp.RespondForTest = req =>
                    {
                        sentBody = req.Content?.ReadAsStringAsync().Result ?? "";
                        gate.Wait(2000);
                        return new System.Net.Http.HttpResponseMessage((System.Net.HttpStatusCode)status)
                            { Content = new System.Net.Http.StringContent(answer, System.Text.Encoding.UTF8, "application/json") };
                    };
                    Features.Friends.KmhFriendsClient.ForgetForTest();

                    answer = "{\"outgoing\":[{\"player_global_id\":\"" + bea + "\",\"username\":\"bea\",\"state\":\"Pending\"}],"
                           + "\"resolved\":{\"player_global_id\":\"" + bea + "\",\"shown\":\"bea\"}}";
                    gate.Reset();
                    bool went = Features.Friends.KmhFriendsClient.QuickAdd(" kx7p-2m9q ");
                    var during = Features.Friends.KmhFriendsClient.AddState;
                    gate.Set();
                    var after = Settle();
                    string msg = Features.Friends.KmhFriendsClient.AddMessage, codeBody = sentBody;

                    // A pasted 32-character ID goes as an ID, which every Global understands.
                    gate.Set();
                    Features.Friends.KmhFriendsClient.ClearAdd();
                    Features.Friends.KmhFriendsClient.QuickAdd(bea.ToUpperInvariant());
                    Settle();
                    string idBody = sentBody;

                    status = 404; answer = "{\"error\":{\"code\":\"KMH-FRD-752\"}}";
                    Features.Friends.KmhFriendsClient.ClearAdd();
                    Features.Friends.KmhFriendsClient.QuickAdd("nobody-here");
                    var refused = Settle();
                    string why = Features.Friends.KmhFriendsClient.AddMessage;

                    status = 503; answer = "";
                    Features.Friends.KmhFriendsClient.ClearAdd();
                    Features.Friends.KmhFriendsClient.QuickAdd("someone");
                    var failed = Settle();
                    bool kept = Features.Friends.KmhFriendsClient.Outgoing.Count == 1 && Features.Friends.KmhFriendsClient.Offline;

                    bool ok = went && during == Features.Friends.KmhFriendsClient.AddStage.Sending
                              && after == Features.Friends.KmhFriendsClient.AddStage.Sent && msg.Contains("bea")
                              && codeBody.Contains("\"target\":\"kx7p-2m9q\"") && !codeBody.Contains("player_global_id")
                              && idBody.Contains("\"player_global_id\":\"" + bea + "\"")
                              && refused == Features.Friends.KmhFriendsClient.AddStage.Refused && why.Contains("friend code or username")
                              && failed == Features.Friends.KmhFriendsClient.AddStage.Failed && kept;
                    return (ok, $"during={during} after={after} '{msg}' code={codeBody} id={idBody} refused={refused} '{why}' failed={failed} kept={kept}");
                }
                finally
                {
                    gate.Set();
                    GlobalHttp.RespondForTest = prior;
                    GlobalHttp.SuppressForTest = http;
                    Notifications.KmhNotifications.Muted = muted;
                    Features.Account.KmhSteamPersona.AssumeNoSteamForTest = noSteam;
                    Features.Account.KmhAccountStore.RootOverrideForTest = root;
                    Features.Account.KmhAccountStore.ResetForTest();
                    Features.Friends.KmhFriendsClient.RestoreForTest(saved);
                    Features.Friends.KmhFriendsClient.ClearAdd();
                }
            });

            // A first launch must not stack What's New, the welcome pages and the link prompt every time.
            Check("first run: the welcome once, What's New only after an update, and a turned-off welcome does not hide it", () =>
            {
                var fresh   = Patches.Patch_MainMenuDrawer_WhatsNew.Wanted(true, false, true, "", "1.3.1");
                var again   = Patches.Patch_MainMenuDrawer_WhatsNew.Wanted(true, true, true, "1.3.1", "1.3.1");
                var updated = Patches.Patch_MainMenuDrawer_WhatsNew.Wanted(true, true, true, "1.3.0", "1.3.1");
                var noWelc  = Patches.Patch_MainMenuDrawer_WhatsNew.Wanted(false, false, true, "1.3.0", "1.3.1");
                bool ok = fresh == (true, false) && again == (false, false) && updated == (false, true) && noWelc == (false, true);
                return (ok, $"fresh={fresh} again={again} updated={updated} welcomeOff={noWelc}");
            });

            Check("friends: Quick Add reads a typed value back before it is sent", () =>
            {
                string id = Dialogs.Dialog_KMHFriends.Hint("0123456789abcdef0123456789ABCDEF");
                string part = Dialogs.Dialog_KMHFriends.Hint("0123456789abcdef0123");
                string code = Dialogs.Dialog_KMHFriends.Hint("KX7P-2M9Q");
                string name = Dialogs.Dialog_KMHFriends.Hint("Ada_Lovelace");
                bool ok = id.Contains("player ID") && part.Contains("20") && code.Contains("friend code") && name.Contains("username")
                          && Dialogs.Dialog_KMHFriends.Hint("  ") == ""
                          && Features.Friends.KmhFriendsClient.FormatCode("kx7p2m9q") == "KX7P-2M9Q";
                return (ok, $"{id} | {part} | {code} | {name}");
            });
        }

        private static void Standings(Action<string, Func<(bool ok, string detail)>> Check)
        {
            Check("standings: a profile's ranks come best first from player boards only", () =>
            {
                var snap = new Features.Leaderboards.Dto.LeaderboardsSnapshot();
                Features.Leaderboards.Dto.LeaderboardBoard Board(string title, string scope, string source, int rank)
                {
                    var b = new Features.Leaderboards.Dto.LeaderboardBoard { Title = title, Scope = scope, Source = source, Total = 9 };
                    b.Rows.Add(new Features.Leaderboards.Dto.LeaderboardRow { Rank = rank, Name = "Ada" });
                    return b;
                }
                snap.Boards.Add(Board("Economy score", "player", "verified", 4));
                snap.Boards.Add(Board("Guild members", "guild", "verified", 1));
                snap.Boards.Add(Board("Kills", "player", "reported", 2));
                var ranks = Features.PlayerStats.PlayerProfilePanel.RanksFor(snap, "ada");
                bool ok = ranks.Count == 2 && ranks[0].title == "Kills" && ranks[0].reported && ranks[1].rank == 4;
                return (ok, string.Join(", ", ranks.ConvertAll(k => $"#{k.rank} {k.title}")));
            });

            // At 1280x720 the board keeps its width and the profile docks as a window; at 1920 it sits beside the board.
            Check("standings: the profile drawer opens only where the board keeps 720px", () =>
                (!Features.Standings.ProfileDrawer.Fits(1038f) && Features.Standings.ProfileDrawer.Fits(1330f),
                 $"drawer at 1330 = {Features.Standings.ProfileDrawer.Width(1330f)}"));
        }

        private static void Social(Action<string, Func<(bool ok, string detail)>> Check)
        {
            string Plan(UI.KmhPlayerMenu.Facts f)
                => string.Join(",", UI.KmhPlayerMenu.Plan(f).ConvertAll(c => c.ToString()));
            string id = new string('a', 32);

            Check("player menu: a name on this server gets profile, server DM, mail and a server block; adding says it needs their ID", () =>
            {
                string got = Plan(new UI.KmhPlayerMenu.Facts
                {
                    LocalName = "bob", NameIsPlayer = true, Linked = true, OnServer = true, ChatOn = true, MailOn = true, BlockingOn = true,
                });
                return (got == "Profile+,AddFriend-NoGlobalId,Message+ServerDm,Mail+,BlockHere+", got);
            });

            Check("player menu: a Global account is added, messaged and blocked on Global, never as a name on this server", () =>
            {
                string got = Plan(new UI.KmhPlayerMenu.Facts
                {
                    GlobalId = id, Linked = true, OnServer = true, ChatOn = true, MailOn = true, BlockingOn = true,
                });
                return (got == "AddFriend+,Message+GlobalDm,BlockEverywhere+,CopyId+", got);
            });

            Check("player menu: unlinked, every Global action says to link first and messaging falls back to this server", () =>
            {
                string both = Plan(new UI.KmhPlayerMenu.Facts
                {
                    LocalName = "bob", NameIsPlayer = true, GlobalId = id, OnServer = true, ChatOn = true, MailOn = true, BlockingOn = true,
                });
                string globalOnly = Plan(new UI.KmhPlayerMenu.Facts { GlobalId = id });
                string mailOnly = Plan(new UI.KmhPlayerMenu.Facts
                {
                    LocalName = "bob", NameIsPlayer = true, OnServer = true, MailOn = true, BlockingOn = true,
                });
                bool ok = both == "Profile+,AddFriend-NotLinked,Message+ServerDm,Mail+,BlockHere+,BlockEverywhere-NotLinked,CopyId+"
                          && globalOnly == "AddFriend-NotLinked,Message-NotLinked,BlockEverywhere-NotLinked,CopyId+"
                          && mailOnly == "Profile+,AddFriend-NotLinked,Message+Mail,BlockHere+";
                return (ok, $"both={both} | globalOnly={globalOnly} | mailOnly={mailOnly}");
            });

            Check("player menu: friendship state, a block, yourself and a relayed name each change what is offered", () =>
            {
                string friend = Plan(new UI.KmhPlayerMenu.Facts { GlobalId = id, Linked = true, Friendship = "friend" });
                string asked = Plan(new UI.KmhPlayerMenu.Facts { GlobalId = id, Linked = true, Friendship = "incoming" });
                string blocked = Plan(new UI.KmhPlayerMenu.Facts { GlobalId = id, Linked = true, BlockedEverywhere = true });
                string self = Plan(new UI.KmhPlayerMenu.Facts { LocalName = "me", NameIsPlayer = true, IsSelf = true, OnServer = true });
                string relayed = Plan(new UI.KmhPlayerMenu.Facts { LocalName = "disc", OnServer = true, BlockingOn = true });
                bool ok = friend == "RemoveFriend+,Message+GlobalDm,BlockEverywhere+,CopyId+"
                          && asked == "AcceptFriend+,DeclineFriend+,Message+GlobalDm,BlockEverywhere+,CopyId+"
                          && blocked == "AddFriend-YouBlocked,Message-YouBlocked,UnblockEverywhere+,CopyId+"
                          && self == "Profile+,Self-" && relayed == "NotAPlayer-,BlockHere+";
                return (ok, $"{friend} | {asked} | {blocked} | {self} | {relayed}");
            });

            Check("global chat: the sender's Global id survives the bridge, a relayed line carries none, and the menu acts on that account", () =>
            {
                string owner = new string('0', 32);
                Features.Chat.Dto.ChatMessage own = Features.Chat.GlobalChatBridge.ToMessage(new GlobalChatEntry
                    { MessageId = "m1", UserId = id, Username = "ann", SentAtUnix = 1789200000 });
                Features.Chat.Dto.ChatMessage relayed = Features.Chat.GlobalChatBridge.ToMessage(new GlobalChatEntry
                    { MessageId = "m2", UserId = owner, Username = "disc", RelaySource = "discord", SentAtUnix = 1789200000 });
                var g = UI.KmhPlayerMenu.TargetFor(own, Features.Chat.ChatChannels.Global);
                var rg = UI.KmhPlayerMenu.TargetFor(relayed, Features.Chat.ChatChannels.Global);
                var s = UI.KmhPlayerMenu.TargetFor(new Features.Chat.Dto.ChatMessage { FromUsername = "ann" }, Features.Chat.ChatChannels.Server);
                bool ok = own.SenderGlobalId == id && relayed.SenderGlobalId == ""
                          && g.GlobalId == id && string.IsNullOrEmpty(g.LocalName)
                          && string.IsNullOrEmpty(rg.GlobalId) && string.IsNullOrEmpty(rg.LocalName)
                          && s.LocalName == "ann" && string.IsNullOrEmpty(s.GlobalId) && s.NameIsPlayer;
                return (ok, $"own={own.SenderGlobalId} relayed='{relayed.SenderGlobalId}' global=({g.LocalName}|{g.GlobalId}) server=({s.LocalName}|{s.GlobalId})");
            });

            Check("friends: the presence answer fills the request lists and the tab count, and announces only a new request", () =>
            {
                Features.Friends.KmhFriendsClient.Saved saved = Features.Friends.KmhFriendsClient.SaveForTest();
                bool http = GlobalHttp.SuppressForTest, muted = Notifications.KmhNotifications.Muted;
                bool noSteam = Features.Account.KmhSteamPersona.AssumeNoSteamForTest;
                string root = Features.Account.KmhAccountStore.RootOverrideForTest;
                Func<System.Net.Http.HttpRequestMessage, System.Net.Http.HttpResponseMessage> prior = GlobalHttp.RespondForTest;
                var told = new List<string>();
                string answer = "";
                string Row(char c, string name)
                    => "{\"player_global_id\":\"" + new string(c, 32) + "\",\"username\":\"" + name + "\",\"state\":\"Pending\"}";
                try
                {
                    // In process only, as nobody: an empty account root, no Steam, and a responder for the answer.
                    GlobalHttp.SuppressForTest = true;
                    Notifications.KmhNotifications.Muted = true;
                    Features.Account.KmhSteamPersona.AssumeNoSteamForTest = true;
                    Features.Account.KmhAccountStore.RootOverrideForTest = System.IO.Path.Combine(
                        System.IO.Path.GetTempPath(), "kmh-presence-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                    Features.Account.KmhAccountStore.ResetForTest();
                    GlobalHttp.RespondForTest = req => new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
                        { Content = new System.Net.Http.StringContent(answer, System.Text.Encoding.UTF8, "application/json") };
                    Features.Friends.KmhFriendsClient.ForgetForTest();
                    Features.Friends.KmhFriendsClient.Announce = fresh => { lock (told) foreach (var d in fresh) told.Add(d.Username); };

                    answer = "{\"incoming\":[" + Row('a', "ann") + "]}";
                    bool done = Features.Friends.KmhPresenceReporter.ReportForTest("");
                    int first = Features.KmhUnread.FriendRequests(), toldFirst = told.Count;

                    answer = "{\"incoming\":[" + Row('a', "ann") + "," + Row('b', "bea") + "],\"outgoing\":[" + Row('c', "cid") + "]}";
                    long older = Features.Friends.KmhFriendsClient.NextRead();
                    done &= Features.Friends.KmhPresenceReporter.ReportForTest("");
                    int second = Features.KmhUnread.FriendRequests(), outgoing = Features.Friends.KmhFriendsClient.Outgoing.Count;
                    done &= Features.Friends.KmhPresenceReporter.ReportForTest("");

                    // An answer to a request that left before the newest applied one must not replace it.
                    Features.Friends.KmhFriendsClient.ApplyPresence(Newtonsoft.Json.Linq.JObject.Parse("{\"incoming\":[]}"), older);
                    int stale = Features.KmhUnread.FriendRequests();

                    bool ok = done && first == 1 && toldFirst == 0 && second == 2 && outgoing == 1
                              && told.Count == 1 && told[0] == "bea" && stale == 2;
                    return (ok, $"first={first} told={toldFirst} second={second} out={outgoing} announced=[{string.Join(",", told)}] afterStale={stale}");
                }
                finally
                {
                    GlobalHttp.RespondForTest = prior;
                    GlobalHttp.SuppressForTest = http;
                    Notifications.KmhNotifications.Muted = muted;
                    Features.Account.KmhSteamPersona.AssumeNoSteamForTest = noSteam;
                    Features.Account.KmhAccountStore.RootOverrideForTest = root;
                    Features.Account.KmhAccountStore.ResetForTest();
                    Features.Friends.KmhFriendsClient.RestoreForTest(saved);
                }
            });

            Check("friends: a pasted player ID is taken whole in any case or spacing, anything else is refused, and yours is shown whole", () =>
            {
                string mixed = "0123456789abcdef0123456789ABCDEF";
                string norm = Features.Friends.KmhFriendsClient.NormalizeId("  " + mixed + " ");
                string shown = Dialogs.Dialog_KMHAccount.PlayerIdText(norm);
                bool ok = norm == mixed.ToLowerInvariant()
                          && Features.Friends.KmhFriendsClient.NormalizeId(mixed.Substring(1)) == ""
                          && Features.Friends.KmhFriendsClient.NormalizeId(mixed + "0") == ""
                          && Features.Friends.KmhFriendsClient.NormalizeId("0123456789abcdef0123456789abcdeg") == ""
                          && shown.Contains(norm);
                return (ok, $"norm={norm} shown='{shown}'");
            });

            Check("friends: Steam matching reports only friendships it made, and the tab names the requests waiting", () =>
            {
                var state = Features.Friends.KmhSteamDiscoveryClient.State;
                int added = Features.Friends.KmhSteamDiscoveryClient.LastAdded;
                try
                {
                    Features.Friends.KmhSteamDiscoveryClient.SetResultForTest(Features.Friends.SteamDiscoveryState.Done, 2);
                    string some = Features.Friends.KmhSteamDiscoveryClient.Summary();
                    Features.Friends.KmhSteamDiscoveryClient.SetResultForTest(Features.Friends.SteamDiscoveryState.Done, 0);
                    string none = Features.Friends.KmhSteamDiscoveryClient.Summary();
                    // One number only: Global never sends a pending count, so a second one would always read 0.
                    int numbers = System.Text.RegularExpressions.Regex.Matches(some, @"\d+").Count;
                    string tab = Tabs.MainTabWindow_KMH.FriendsLabel(3), plain = Tabs.MainTabWindow_KMH.FriendsLabel(0);
                    bool ok = numbers == 1 && some.Contains("2") && none.Length > 0
                              && Features.Friends.KmhSteamDiscoveryClient.Explain().Length > 0
                              && tab.Contains("3") && !System.Text.RegularExpressions.Regex.IsMatch(plain, @"\d");
                    return (ok, $"some='{some}' none='{none}' tab='{tab}' plain='{plain}'");
                }
                finally { Features.Friends.KmhSteamDiscoveryClient.SetResultForTest(state, added); }
            });
        }

        private static void ColonyStats(Action<string, Func<(bool ok, string detail)>> Check)
        {
            const string Reporter = "KMHPatch.Features.PlayerStats.GameComponent_KMHColonyReporter";
            List<string> Calls(string type, string method) => KmhIlReader.CallsMadeBy(type, method);

            Check("colony report: the roster is the true top ten - most kills, then longest service - not the first ten in map order", () =>
            {
                var all = new List<Features.PlayerStats.Dto.ColonistEntry>();
                for (int i = 0; i < 12; i++) all.Add(new Features.PlayerStats.Dto.ColonistEntry { Name = "C" + i, Kills = i, Days = 100 - i });
                all.Add(new Features.PlayerStats.Dto.ColonistEntry { Name = "Vet", Kills = 11, Days = 400 });
                var top = Features.PlayerStats.GameComponent_KMHColonyReporter.TopRoster(all, Features.PlayerStats.GameComponent_KMHColonyReporter.RosterMax);
                string got = string.Join(",", top.ConvertAll(c => c.Name));
                bool used = Calls(Reporter, "BuildRoster").Contains("TopRoster");
                return (got == "Vet,C11,C10,C9,C8,C7,C6,C5,C4,C3" && used, $"{got} (report uses it: {used})");
            });

            Check("colony kills: a fallen colonist's kills stay counted, one raised again is not counted twice, and the report adds them", () =>
            {
                var tally = new Patches.GameComponent_KMHKillTally(null);
                tally.RecordFallen(new Patches.GameComponent_KMHKillTally.FallenColonist { Id = "Human1", Name = "Ada", Kills = 5, Humanlike = 3, Animal = 2 });
                tally.RecordFallen(new Patches.GameComponent_KMHKillTally.FallenColonist { Id = "Human2", Name = "Bo", Kills = 4, Mechanoid = 4 });
                var none = tally.FallenKills(new HashSet<string>());
                var raised = tally.FallenKills(new HashSet<string> { "Human2" });
                tally.RecordFallen(new Patches.GameComponent_KMHKillTally.FallenColonist { Id = "Human2", Name = "Bo", Kills = 6, Mechanoid = 6 });
                var again = tally.FallenKills(new HashSet<string>());
                bool adds = Calls(Reporter, "BuildReport").Contains("FallenKills");
                bool ok = none.Kills == 9 && none.Humanlike == 3 && none.Mechanoid == 4 && none.Animal == 2 && raised.Kills == 5
                          && again.Kills == 11 && tally.FallenCount == 2 && tally.ColonistDeaths == 3 && adds;
                return (ok, $"all={none.Kills} raised={raised.Kills} diedAgain={again.Kills} rows={tally.FallenCount} deaths={tally.ColonistDeaths} reportAdds={adds}");
            });

            Check("colony kills: past the cap the oldest fallen fold into a total, so no kill is lost", () =>
            {
                var tally = new Patches.GameComponent_KMHKillTally(null);
                int n = Patches.GameComponent_KMHKillTally.MaxFallen + 3;
                for (int i = 0; i < n; i++) tally.RecordFallen(new Patches.GameComponent_KMHKillTally.FallenColonist { Id = "Human" + i, Kills = 1 });
                long total = tally.FallenKills(new HashSet<string>()).Kills;
                bool ok = tally.FallenCount == Patches.GameComponent_KMHKillTally.MaxFallen && total == n;
                return (ok, $"rows={tally.FallenCount} kills={total}/{n}");
            });

            Check("colonists: population, deaths and kills share one rule, RimWorld's IsColonist", () =>
            {
                bool rule = Calls(Reporter, "IsCountedColonist").Contains("get_IsColonist");
                bool living = Calls(Reporter, "LivingColonists").Contains("IsCountedColonist");
                bool death = Calls("KMHPatch.Patches.Patch_Pawn_Kill_HuntTally", "Prefix").Contains("IsCountedColonist");
                return (rule && living && death, $"rule={rule} population={living} deaths={death}");
            });

            Check("colonists: counted on every map, in caravans and transporters, aboard a gravship and at KMH site work", () =>
            {
                List<string> c = Calls(Reporter, "LivingColonists");
                bool ok = c.Contains("get_AllMapsCaravansAndTravellingTransporters_Alive_Colonists") && c.Contains("get_CurrentGravship")
                          && c.Contains("AppendHeldPawns");
                return (ok, string.Join(",", c.FindAll(x => x.StartsWith("get_All") || x.Contains("Gravship") || x.Contains("Held"))));
            });

            Check("playtime: RimWorld's own per-game interacting time, not a clock that kept running idle and across games", () =>
            {
                bool ok = Calls(Reporter, "BuildReport").Contains("PlayedHours") && Calls(Reporter, "PlayedHours").Contains("get_RealPlayTimeInteracting");
                return (ok, ok ? "" : "the report's hours are not RimWorld's RealPlayTimeInteracting");
            });

            Check("recent combat: a colonist's own kills, newest first, bounded, inert where written and where received", () =>
            {
                var tally = new Patches.GameComponent_KMHKillTally(null);
                for (int i = 0; i < 45; i++) tally.NoteCombat(i % 2 == 0 ? "Human1" : "Human2", $"Killed <color=red>Raider{i}</color> · day {i}", i);
                List<string> mine = tally.RecentCombatOf("Human1", 8);
                bool shipped = Calls(Reporter, "BuildColonistProfile").Contains("RecentCombatOf");
                bool received = Calls("KMHPatch.Features.PlayerStats.PlayerStatsHandler", "OnColonistProfile").Contains("Inert");
                bool ok = tally.CombatCount == Patches.GameComponent_KMHKillTally.MaxCombatEvents && mine.Count == 8
                          && mine[0].Contains("Raider44") && mine[1].Contains("Raider42") && !mine.Exists(s => s.Contains("<"))
                          && tally.RecentCombatOf("nobody", 8).Count == 0 && shipped && received;
                return (ok, $"kept={tally.CombatCount} first='{(mine.Count > 0 ? mine[0] : "")}' shipped={shipped} received={received}");
            });

            Check("traffic: a window asks for a colony report only while this connection has none", () =>
            {
                bool fresh = Features.PlayerStats.GameComponent_KMHColonyReporter.UiMaySend(-1, 3);
                bool sent = !Features.PlayerStats.GameComponent_KMHColonyReporter.UiMaySend(3, 3);
                bool next = Features.PlayerStats.GameComponent_KMHColonyReporter.UiMaySend(3, 4);
                bool gated = Calls(Reporter, "SendNow").Contains("UiMaySend");
                return (fresh && sent && next && gated, $"fresh={fresh} alreadySent={sent} newConnection={next} gated={gated}");
            });

            Check("traffic: the enforcement poll counts real seconds, not game ticks, and a snapshot that just arrived counts as asked", () =>
            {
                const long F = 1000, T = 1_000_000;
                bool first = Features.Enforcement.GameComponent_KMHEnforcementPoll.Due(T, 0, 0, F);
                bool soon = !Features.Enforcement.GameComponent_KMHEnforcementPoll.Due(T + 59 * F, T, 0, F);
                bool after = Features.Enforcement.GameComponent_KMHEnforcementPoll.Due(T + 60 * F, T, 0, F);
                bool pushed = !Features.Enforcement.GameComponent_KMHEnforcementPoll.Due(T + 90 * F, T, T + 50 * F, F);
                bool noTick = typeof(Features.Enforcement.GameComponent_KMHEnforcementPoll).GetMethod("GameComponentTick",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.DeclaredOnly) == null;
                bool stamped = Calls("KMHPatch.Features.Enforcement.EnforcementHandler", "OnSnapshot").Contains("GetTimestamp")
                               && Calls("KMHPatch.Features.Enforcement.GameComponent_KMHEnforcementPoll", "GameComponentUpdate").Contains("get_LastSnapshotStamp");
                bool ok = first && soon && after && pushed && noTick && stamped;
                return (ok, $"first={first} soon={soon} after={after} pushedCounts={pushed} offGameTicks={noTick} snapshotStamped={stamped}");
            });
        }
    }
}
