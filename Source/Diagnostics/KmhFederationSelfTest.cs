using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using KMHPatch.Features.Federation;
using KMHPatch.Features.Federation.Dto;
using KMHPatch.SubProtocol;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace KMHPatch.Diagnostics
{
    internal static class KmhFederationSelfTest
    {
        public static List<(string, bool, string)> Run()
        {
            var r = new List<(string, bool, string)>();
            bool muted = KmhLog.Muted, suppressed = KmhDispatcher.SuppressSendForTest;
            var caps = KmhCapabilities.SaveForTest();
            var switches = UI.KmhDashboardState.SaveForTest();
            KmhLog.Muted = true;
            KmhDispatcher.SuppressSendForTest = true;
            try
            {
                Guard(r, "wire name", () => WireNames(r));
                Guard(r, "parsing", () => Parsing(r));
                Guard(r, "ordering", () => Ordering(r));
                Guard(r, "inert text", () => Inert(r));
                Guard(r, "gate", () => Gates(r));
                Guard(r, "offer", () => Offers(r));
                Guard(r, "request", () => Requests(r));
                Guard(r, "disband", () => Disband(r));
                Guard(r, "vocabulary", () => Vocabulary(r));
                Guard(r, "wiring", () => Wiring(r));
            }
            finally
            {
                FederationHandler.SenderForTest = null;
                FederationHandler.NoticeForTest = null;
                FederationCache.Clear();
                KmhCapabilities.RestoreForTest(caps);
                UI.KmhDashboardState.RestoreForTest(switches);
                KmhDispatcher.SuppressSendForTest = suppressed;
                KmhLog.Muted = muted;
            }
            return r;
        }

        // A section that throws is one failure with its reason, not a silent gap in the count.
        private static void Guard(List<(string, bool, string)> r, string what, Action run)
        {
            try { run(); }
            catch (Exception ex) { r.Add(($"federation: the {what} checks ran", false, ex.GetType().Name + ": " + ex.Message)); }
        }

        private const string Markup = "<size=400><color=#00000000>x</color>";

        private static bool NoMarkup(string s) => s != null && s.IndexOf('<') < 0 && s.IndexOf('>') < 0;

        private static void OpenGate()
        {
            KmhCapabilities.Apply(KmhCapabilities.Federation, present: true);
            Features.KmhFeatures.SetDisabled("");
        }

        // The contract's snapshot, field for field. Written out rather than read from the DTOs, because it is the pin.
        private static readonly (Type Type, string[] Names)[] Spec =
        {
            (typeof(FederationSnapshot), new[] { "revision", "available", "unavailable", "verified", "verified_why", "in_guild",
                "guild", "rank", "may_manage", "manage_why", "may_lead", "lead_why", "verified_why_code", "manage_why_code",
                "lead_why_code", "server_id", "linked", "link", "federation",
                "members", "objectives", "achievements", "events", "categories", "invites", "benefits", "fresh_invite",
                "synced_at", "stale", "benefits_until", "pending_reports", "lost_reports" }),
            (typeof(FederationLink), new[] { "link_id", "state", "detail", "joined_at", "is_leader" }),
            (typeof(FederationProfile), new[] { "id", "name", "tag", "description", "color", "state", "created_at",
                "disbanded_at", "disband_reason", "founder_server", "founder_guild", "leader_link_id", "leader_server",
                "leader_guild", "level", "renown", "level_floor", "next_level_at", "active_members", "lifetime_members",
                "verified_players_30d", "lifetime_verified_players", "objectives_completed", "renown_today", "renown_day_cap" }),
            (typeof(FederationMember), new[] { "link_id", "server_id", "server_name", "guild", "state", "joined_at",
                "is_leader", "is_self", "online", "renown_total", "renown_7d" }),
            (typeof(FederationObjective), new[] { "id", "category", "target", "progress", "starts_at", "ends_at", "state",
                "reward_renown", "my_progress", "reward_eligible", "reward_granted", "reward_text" }),
            (typeof(FederationAchievement), new[] { "key", "achieved_at" }),
            (typeof(FederationEvent), new[] { "at", "kind", "server", "guild", "detail" }),
            (typeof(FederationCategory), new[] { "category", "total", "last_7d" }),
            (typeof(FederationInvite), new[] { "invite_id", "target_server", "target_name", "expires_at", "mine" }),
            (typeof(FederationBenefit), new[] { "key", "level", "active", "value" }),
            (typeof(FederationFreshInvite), new[] { "code", "target_server", "expires_at" }),
        };

        private static void WireNames(List<(string, bool, string)> r)
        {
            foreach ((Type type, string[] names) in Spec)
            {
                PropertyInfo[] props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
                var declared = props.Select(p => p.GetCustomAttribute<JsonPropertyAttribute>()?.PropertyName).Where(n => n != null).ToList();
                var unnamed = props.Where(p => p.GetCustomAttribute<JsonPropertyAttribute>() == null
                                            && p.GetCustomAttribute<JsonIgnoreAttribute>() == null).Select(p => p.Name).ToList();
                var missing = names.Where(n => !declared.Contains(n)).ToList();
                var extra = declared.Where(n => !names.Contains(n)).ToList();
                bool ok = missing.Count == 0 && extra.Count == 0 && unnamed.Count == 0 && declared.Count == names.Length;
                r.Add(($"federation: {type.Name} carries exactly the contract's field names", ok,
                       ok ? $"{names.Length} field(s)"
                          : $"missing [{string.Join(",", missing)}] extra [{string.Join(",", extra)}] unnamed [{string.Join(",", unnamed)}]"));
            }
        }

        private static JObject FullSnapshot(long revision) => new JObject
        {
            ["revision"] = revision, ["available"] = true, ["unavailable"] = "", ["verified"] = true, ["verified_why"] = "vw",
            ["in_guild"] = true, ["guild"] = "Ironclad", ["rank"] = "owner",
            ["may_manage"] = true, ["manage_why"] = "mw", ["may_lead"] = true, ["lead_why"] = "lw",
            ["server_id"] = "srv-self", ["linked"] = true,
            ["link"] = new JObject { ["link_id"] = "link-self", ["state"] = "Active", ["detail"] = "ld", ["joined_at"] = 1700000001L, ["is_leader"] = true },
            ["federation"] = new JObject
            {
                ["id"] = "fed-1", ["name"] = "Iron Pact", ["tag"] = "IRON", ["description"] = "We hold the line.", ["color"] = "#2980B9",
                ["state"] = "Active", ["created_at"] = 1700000002L, ["disbanded_at"] = 1700000003L, ["disband_reason"] = "dr",
                ["founder_server"] = "Alpha", ["founder_guild"] = "Founders", ["leader_link_id"] = "link-self",
                ["leader_server"] = "Alpha", ["leader_guild"] = "Ironclad", ["level"] = 3, ["renown"] = 812L,
                ["level_floor"] = 450L, ["next_level_at"] = 900L, ["active_members"] = 3, ["lifetime_members"] = 5,
                ["verified_players_30d"] = 12, ["lifetime_verified_players"] = 40, ["objectives_completed"] = 7,
                ["renown_today"] = 120L, ["renown_day_cap"] = 420L,
            },
            ["members"] = new JArray
            {
                new JObject { ["link_id"] = "link-2", ["server_id"] = "srv-2", ["server_name"] = "Beta", ["guild"] = "Allies",
                              ["state"] = "Active", ["joined_at"] = 1700000010L, ["is_leader"] = false, ["is_self"] = false,
                              ["online"] = true, ["renown_total"] = 300L, ["renown_7d"] = 30L },
                new JObject { ["link_id"] = "link-self", ["server_id"] = "srv-self", ["server_name"] = "Alpha", ["guild"] = "Ironclad",
                              ["state"] = "Active", ["joined_at"] = 1700000001L, ["is_leader"] = true, ["is_self"] = true,
                              ["online"] = true, ["renown_total"] = 500L, ["renown_7d"] = 50L },
            },
            ["objectives"] = new JArray
            {
                new JObject { ["id"] = "obj-1", ["category"] = "quest", ["target"] = 20, ["progress"] = 9, ["starts_at"] = 1700000020L,
                              ["ends_at"] = 1700600000L, ["state"] = "Active", ["reward_renown"] = 120, ["my_progress"] = 4,
                              ["reward_eligible"] = true, ["reward_granted"] = true, ["reward_text"] = "Steel x80" },
            },
            ["achievements"] = new JArray { new JObject { ["key"] = "founded", ["achieved_at"] = 1700000002L } },
            ["events"] = new JArray
            {
                new JObject { ["at"] = 1700000002L, ["kind"] = "Created", ["server"] = "Alpha", ["guild"] = "Ironclad", ["detail"] = "" },
                new JObject { ["at"] = 1700000050L, ["kind"] = "Joined", ["server"] = "Beta", ["guild"] = "Allies", ["detail"] = "ed" },
            },
            ["categories"] = new JArray { new JObject { ["category"] = "quest", ["total"] = 55, ["last_7d"] = 9 } },
            ["invites"] = new JArray
            {
                new JObject { ["invite_id"] = "inv-1", ["target_server"] = "srv-x", ["target_name"] = "Gamma", ["expires_at"] = 1700001800L, ["mine"] = true },
            },
            ["benefits"] = new JArray { new JObject { ["key"] = "site_output", ["level"] = 3, ["active"] = true, ["value"] = 1.5 } },
            ["fresh_invite"] = new JObject { ["code"] = "ABCD-EFGH", ["target_server"] = "srv-x", ["expires_at"] = 1700001800L },
            ["synced_at"] = 1700000100L, ["stale"] = true, ["benefits_until"] = 1700259300L, ["pending_reports"] = 7, ["lost_reports"] = 2,
        };

        // Through the same parse and handler a real frame takes, so a name the code reads differently fails here.
        private static FederationSnapshot Receive(JObject data)
        {
            var frame = new JObject { ["kind"] = "kmh.federation.snapshot", ["v"] = KmhProtocol.CurrentVersion, ["data"] = data };
            FederationHandler.OnSnapshot(KmhEnvelope.TryParse(frame.ToString(Formatting.None)));
            return FederationCache.Snapshot;
        }

        private static void Parsing(List<(string, bool, string)> r)
        {
            FederationCache.Clear();
            FederationSnapshot s = Receive(FullSnapshot(41));
            r.Add(("federation: a snapshot from the wire lands in the cache", s != null, s == null ? "nothing applied" : ""));
            if (s == null) return;

            r.Add(("federation: the snapshot's own fields are read by their contract names",
                   s.Revision == 41 && s.Available && s.Unavailable == "" && s.Verified && s.VerifiedWhy == "vw" && s.InGuild
                   && s.Guild == "Ironclad" && s.Rank == "owner" && s.MayManage && s.ManageWhy == "mw" && s.MayLead
                   && s.LeadWhy == "lw" && s.ServerId == "srv-self" && s.Linked && s.SyncedAt == 1700000100L && s.Stale
                   && s.BenefitsUntil == 1700259300L && s.PendingReports == 7 && s.LostReports == 2, $"rev={s.Revision} guild={s.Guild} server={s.ServerId} pending={s.PendingReports} lost={s.LostReports}"));

            FederationLink k = s.Link;
            r.Add(("federation: this server's link is read by its contract names",
                   k.LinkId == "link-self" && k.State == "Active" && k.Detail == "ld" && k.JoinedAt == 1700000001L && k.IsLeader,
                   $"{k.LinkId} {k.State} {k.JoinedAt}"));

            FederationProfile f = s.Federation;
            r.Add(("federation: the profile is read by its contract names",
                   f.Id == "fed-1" && f.Name == "Iron Pact" && f.Tag == "IRON" && f.Description == "We hold the line."
                   && f.Color == "#2980B9" && f.State == "Active" && f.CreatedAt == 1700000002L && f.DisbandedAt == 1700000003L
                   && f.DisbandReason == "dr" && f.FounderServer == "Alpha" && f.FounderGuild == "Founders"
                   && f.LeaderLinkId == "link-self" && f.LeaderServer == "Alpha" && f.LeaderGuild == "Ironclad",
                   $"{f.Id} {f.Name} [{f.Tag}] {f.Color}"));
            r.Add(("federation: the profile's level and counts are read by their contract names",
                   f.Level == 3 && f.Renown == 812 && f.LevelFloor == 450 && f.NextLevelAt == 900 && f.ActiveMembers == 3
                   && f.LifetimeMembers == 5 && f.VerifiedPlayers30d == 12 && f.LifetimeVerifiedPlayers == 40
                   && f.ObjectivesCompleted == 7 && f.RenownToday == 120 && f.RenownDayCap == 420,
                   $"level={f.Level} renown={f.Renown} members={f.ActiveMembers}/{f.LifetimeMembers}"));

            FederationMember other = s.Members.FirstOrDefault(m => m.LinkId == "link-2");
            r.Add(("federation: a member is read by its contract names",
                   s.Members.Count == 2 && other != null && other.ServerId == "srv-2" && other.ServerName == "Beta"
                   && other.Guild == "Allies" && other.State == "Active" && other.JoinedAt == 1700000010L && !other.IsLeader
                   && !other.IsSelf && other.Online && other.RenownTotal == 300 && other.Renown7d == 30,
                   other == null ? "member missing" : $"{other.ServerName} {other.Guild} {other.RenownTotal}/{other.Renown7d}"));
            r.Add(("federation: the leader's row comes first",
                   s.Members[0].IsLeader && s.Members[0].IsSelf, s.Members[0].LinkId));

            FederationObjective o = s.Objectives.FirstOrDefault();
            r.Add(("federation: an objective is read by its contract names",
                   o != null && o.Id == "obj-1" && o.Category == "quest" && o.Target == 20 && o.Progress == 9
                   && o.StartsAt == 1700000020L && o.EndsAt == 1700600000L && o.State == "Active" && o.RewardRenown == 120
                   && o.MyProgress == 4 && o.RewardEligible && o.RewardGranted && o.RewardText == "Steel x80",
                   o == null ? "objective missing" : $"{o.Category} {o.Progress}/{o.Target} mine={o.MyProgress}"));

            FederationAchievement a = s.Achievements.FirstOrDefault();
            FederationCategory c = s.Categories.FirstOrDefault();
            r.Add(("federation: achievements, events and categories are read by their contract names",
                   a != null && a.Key == "founded" && a.AchievedAt == 1700000002L
                   && s.Events.Count == 2 && s.Events.Any(e => e.Kind == "Joined" && e.Server == "Beta" && e.Guild == "Allies" && e.Detail == "ed")
                   && c != null && c.Category == "quest" && c.Total == 55 && c.Last7d == 9,
                   $"achievements={s.Achievements.Count} events={s.Events.Count} categories={s.Categories.Count}"));
            r.Add(("federation: history shows the newest event first",
                   s.Events[0].At == 1700000050L && s.Events[1].At == 1700000002L, $"{s.Events[0].At} then {s.Events[1].At}"));

            FederationInvite i = s.Invites.FirstOrDefault();
            FederationBenefit b = s.Benefits.FirstOrDefault();
            FederationFreshInvite fresh = s.FreshInvite;
            r.Add(("federation: invites, benefits and the fresh invite are read by their contract names",
                   i != null && i.InviteId == "inv-1" && i.TargetServer == "srv-x" && i.TargetName == "Gamma"
                   && i.ExpiresAt == 1700001800L && i.Mine
                   && b != null && b.Key == "site_output" && b.Level == 3 && b.Active && Math.Abs(b.Value - 1.5) < 1e-9
                   && fresh.Code == "ABCD-EFGH" && fresh.TargetServer == "srv-x" && fresh.ExpiresAt == 1700001800L,
                   $"invite={i?.InviteId} benefit={b?.Key}:{b?.Value} fresh={fresh.Code}"));

            // An older or careless server may leave a list out or send null; the window then reads empty, never null.
            FederationCache.Clear();
            FederationSnapshot bare = Receive(new JObject { ["revision"] = 1, ["available"] = true, ["members"] = null, ["link"] = null });
            r.Add(("federation: a snapshot missing its lists and objects reads as empty rather than null",
                   bare != null && bare.Members != null && bare.Objectives != null && bare.Achievements != null && bare.Events != null
                   && bare.Categories != null && bare.Invites != null && bare.Benefits != null && bare.Link != null
                   && bare.Federation != null && bare.FreshInvite != null && bare.Members.Count == 0 && bare.Link.LinkId == "",
                   bare == null ? "nothing applied" : "normalized"));

            Receive(FullSnapshot(2));
            JObject broken = FullSnapshot(99);
            broken["revision"] = "not a number";
            FederationSnapshot kept = Receive(broken);
            r.Add(("federation: a snapshot that does not parse is dropped and the last good one stays",
                   kept != null && kept.Revision == 2, $"revision={kept?.Revision}"));
        }

        private static FederationSnapshot Snap(long revision, string guild)
            => new FederationSnapshot { Revision = revision, Available = true, InGuild = true, Guild = guild };

        private static void Ordering(List<(string, bool, string)> r)
        {
            FederationCache.Clear();
            FederationCache.Apply(Snap(5, "five"));
            FederationCache.Apply(Snap(3, "three"));
            r.Add(("federation: a snapshot that arrives late does not move the window backwards",
                   FederationCache.Snapshot.Revision == 5 && FederationCache.Snapshot.Guild == "five",
                   $"revision={FederationCache.Snapshot.Revision} guild={FederationCache.Snapshot.Guild}"));

            FederationCache.Apply(Snap(7, "seven"));
            r.Add(("federation: a newer snapshot replaces the one shown",
                   FederationCache.Snapshot.Revision == 7 && FederationCache.Snapshot.Guild == "seven",
                   $"revision={FederationCache.Snapshot.Revision}"));

            FederationCache.Apply(Snap(7, "seven again"));
            r.Add(("federation: the same revision delivered twice never lowers what is shown",
                   FederationCache.Snapshot.Revision == 7, $"revision={FederationCache.Snapshot.Revision}"));

            Receive(FullSnapshot(6));
            r.Add(("federation: an older frame through the handler is dropped too",
                   FederationCache.Snapshot.Revision == 7 && FederationCache.Snapshot.Guild == "seven again",
                   $"revision={FederationCache.Snapshot.Revision}"));

            // A different server counts from its own start, so the switch clears first and its first snapshot applies.
            FederationCache.Clear();
            FederationCache.Apply(Snap(1, "next server"));
            r.Add(("federation: after a server switch the next server's first snapshot applies",
                   FederationCache.Snapshot != null && FederationCache.Snapshot.Revision == 1, "cleared then applied"));
        }

        // Found by reflection, so a string field added later is covered without anyone remembering to add it here.
        private static object Hostile(Type t)
        {
            object o = Activator.CreateInstance(t);
            foreach (PropertyInfo p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!p.CanWrite) continue;
                Type pt = p.PropertyType;
                if (pt == typeof(string)) p.SetValue(o, Markup);
                else if (pt.IsGenericType && pt.GetGenericTypeDefinition() == typeof(List<>))
                {
                    IList list = (IList)Activator.CreateInstance(pt);
                    list.Add(Hostile(pt.GetGenericArguments()[0]));
                    p.SetValue(o, list);
                }
                else if (pt.IsClass && pt.Namespace == typeof(FederationSnapshot).Namespace) p.SetValue(o, Hostile(pt));
            }
            return o;
        }

        private static void Walk(object o, string path, List<string> leaks, ref int strings)
        {
            foreach (PropertyInfo p in o.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (p.GetIndexParameters().Length > 0) continue;
                object v = p.GetValue(o);
                if (p.PropertyType == typeof(string))
                {
                    strings++;
                    if (!NoMarkup((string)v ?? "")) leaks.Add(path + p.Name);
                }
                else if (v is IList list)
                {
                    int n = 0;
                    foreach (object item in list) if (item != null) Walk(item, path + p.Name + "[" + n++ + "].", leaks, ref strings);
                }
                else if (v != null && p.PropertyType.IsClass && p.PropertyType.Namespace == typeof(FederationSnapshot).Namespace)
                    Walk(v, path + p.Name + ".", leaks, ref strings);
            }
        }

        private static void Inert(List<(string, bool, string)> r)
        {
            FederationCache.Clear();
            FederationCache.Apply((FederationSnapshot)Hostile(typeof(FederationSnapshot)));
            var leaks = new List<string>();
            int strings = 0;
            Walk(FederationCache.Snapshot, "", leaks, ref strings);
            r.Add(("federation: every server-supplied string is inert by the time the window can draw it",
                   leaks.Count == 0 && strings >= 40,
                   leaks.Count == 0 ? $"{strings} string field(s) examined" : "RAW MARKUP IN: " + string.Join(", ", leaks)));

            var huge = new FederationSnapshot { Revision = 2, Federation = new FederationProfile { Name = new string('n', 10000), Tag = "IRON" } };
            FederationCache.Apply(huge);
            string name = FederationCache.Snapshot.Federation.Name;
            r.Add(("federation: a name far past any real one is cut to a size the window can draw",
                   name.Length < 10000 && name.Length <= FederationCache.MaxName + 1, $"{name.Length} char(s) kept of 10000"));

            var flood = new FederationSnapshot { Revision = 3 };
            for (int n = 0; n < 5000; n++) flood.Events.Add(new FederationEvent { At = n, Kind = "Joined" });
            flood.Members.Add(null);
            flood.Members.Add(new FederationMember { LinkId = "m-1", State = "Active" });
            FederationCache.Apply(flood);
            r.Add(("federation: a flood of rows is bounded, and null rows are dropped",
                   FederationCache.Snapshot.Events.Count <= FederationCache.MaxEvents && FederationCache.Snapshot.Events.Count < 5000
                   && FederationCache.Snapshot.Members.Count == 1,
                   $"events={FederationCache.Snapshot.Events.Count} members={FederationCache.Snapshot.Members.Count}"));

            const string id = "0123456789abcdef0123456789abcdef";
            var legit = new FederationSnapshot
            {
                Revision = 4, ServerId = id,
                Link = new FederationLink { LinkId = id, State = "Active" },
                Federation = new FederationProfile { Id = id, Tag = "IRON", Color = "#2980B9", Name = "Iron Pact" },
                FreshInvite = new FederationFreshInvite { Code = "ABCD-EFGH-JKMN", TargetServer = id },
            };
            FederationCache.Apply(legit);
            FederationSnapshot s = FederationCache.Snapshot;
            r.Add(("federation: real ids, tags, colours and invite codes are kept exactly, so a copied code still works",
                   s.ServerId == id && s.Link.LinkId == id && s.Federation.Id == id && s.Federation.Tag == "IRON"
                   && s.Federation.Color == "#2980B9" && s.FreshInvite.Code == "ABCD-EFGH-JKMN" && s.FreshInvite.TargetServer == id,
                   $"{s.FreshInvite.Code} {s.Federation.Color}"));

            FederationCache.Apply(new FederationSnapshot { Revision = 5, Federation = new FederationProfile { Color = "#2980B9><size=99>" } });
            string closing = FederationCache.Snapshot.Federation.Color;
            FederationCache.Apply(new FederationSnapshot { Revision = 6, Federation = new FederationProfile { Color = "red" } });
            r.Add(("federation: a colour that could close its own tag is dropped",
                   closing == "" && FederationCache.Snapshot.Federation.Color == "", $"'{closing}'"));

            Titles(r);
        }

        private static FederationSnapshot Member(long revision, bool verified = true, string state = "Active", bool linked = true,
                                                 string fedState = "Active", string guild = "Ironclad")
            => new FederationSnapshot
            {
                Revision = revision, Available = true, Verified = verified, InGuild = true, Guild = guild, Linked = linked,
                MayManage = true, MayLead = true, ServerId = "srv-self",
                Link = new FederationLink { LinkId = "link-self", State = state, IsLeader = true },
                Federation = new FederationProfile { Id = "fed-1", Name = "Iron Pact", Tag = "IRON", Color = "#2980B9", State = fedState, ActiveMembers = 2 },
                Members = new List<FederationMember>
                {
                    new FederationMember { LinkId = "link-self", State = "Active", IsLeader = true, IsSelf = true },
                    new FederationMember { LinkId = "link-2", State = "Active" },
                    new FederationMember { LinkId = "link-old", State = "Left" },
                },
            };

        private static void Titles(List<(string, bool, string)> r)
        {
            FederationCache.Clear();
            FederationCache.Apply(Member(1));
            string shown = FederationCache.TitleTagFor("Ironclad");
            r.Add(("federation: a verified member of an active federation sees its tag beside the guild name",
                   shown.Contains("[IRON]") && shown.StartsWith("<color=#", StringComparison.Ordinal), shown));

            FederationCache.Clear(); FederationCache.Apply(Member(1, verified: false));
            bool unverified = FederationCache.TitleTagFor("Ironclad") == "";
            FederationCache.Clear(); FederationCache.Apply(Member(1, state: "Left", linked: false));
            bool ended = FederationCache.TitleTagFor("Ironclad") == "";
            FederationCache.Clear(); FederationCache.Apply(Member(1, fedState: "Disbanded"));
            bool disbanded = FederationCache.TitleTagFor("Ironclad") == "";
            FederationCache.Clear(); FederationCache.Apply(Member(1));
            bool otherGuild = FederationCache.TitleTagFor("Somebody Else") == "";
            r.Add(("federation: no tag for an unverified player, an ended link, a disbanded federation or another guild",
                   unverified && ended && disbanded && otherGuild,
                   $"unverified={unverified} ended={ended} disbanded={disbanded} otherGuild={otherGuild}"));
        }

        private static void Gates(List<(string, bool, string)> r)
        {
            bool table = true;
            for (int i = 0; i < 8; i++)
            {
                bool a = (i & 1) != 0, g = (i & 2) != 0, f = (i & 4) != 0;
                if (FederationGate.Decide(a, g, f) != (a && g && f)) table = false;
            }
            r.Add(("federation: the entry needs the capability and both guilds and federation switched on", table, "8 combinations"));

            KmhCapabilities.Apply("roadworks,site_meta", present: true);
            Features.KmhFeatures.SetDisabled("");
            bool noCap = !FederationGate.FederationOffered;
            KmhCapabilities.Reset();
            bool oldServer = !FederationGate.FederationOffered;
            r.Add(("federation: Guild Federation is hidden on a server that does not advertise it",
                   noCap && oldServer, $"manifest without it={noCap} no manifest={oldServer}"));

            OpenGate();
            r.Add(("federation: Guild Federation is offered when the server advertises it and nothing is disabled",
                   FederationGate.FederationOffered, "federation advertised"));

            Features.KmhFeatures.SetDisabled("guilds");
            bool guildsOff = !FederationGate.FederationOffered;
            Features.KmhFeatures.SetDisabled("federation");
            bool fedOff = !FederationGate.FederationOffered;
            r.Add(("federation: Guild Federation is hidden when guilds or federation is disabled",
                   guildsOff && fedOff, $"guilds off={guildsOff} federation off={fedOff}"));

            var sent = new List<(string, object, string)>();
            FederationHandler.SenderForTest = (k, d, o) => { sent.Add((k, d, o)); return true; };
            bool refused = !FederationHandler.RequestSnapshot(false) && sent.Count == 0;
            r.Add(("federation: no request leaves for a server that has federation switched off", refused, $"{sent.Count} sent"));
            FederationHandler.SenderForTest = null;

            Features.KmhFeatures.SetDisabled("connected");
            bool treasuryHidden = !FederationGate.TreasuryOffered;
            bool rowHidden = !UI.KMHDashboard.RowShownFor("connected");
            r.Add(("federation: the Connected Treasury entries are hidden while connected is disabled",
                   treasuryHidden && rowHidden, $"entries hidden={treasuryHidden} dashboard row hidden={rowHidden}"));

            Features.KmhFeatures.SetDisabled("");
            r.Add(("federation: the Connected Treasury entries return when connected is enabled",
                   FederationGate.TreasuryOffered && UI.KMHDashboard.RowShownFor("connected"), "connected enabled"));

            KmhCapabilities.Apply("roadworks", present: true);
            bool rowWithout = !UI.KMHDashboard.RowShownFor("federation");
            OpenGate();
            bool rowWith = UI.KMHDashboard.RowShownFor("federation");
            r.Add(("federation: the dashboard summarises Federation only where the server advertises it",
                   rowWithout && rowWith, $"without={rowWithout} with={rowWith}"));
        }

        private static void Offers(List<(string, bool, string)> r)
        {
            FederationSnapshot lead = Member(1);
            FederationSnapshot officer = Member(1); officer.MayLead = false;
            FederationSnapshot member = Member(1); member.MayLead = false; member.MayManage = false;
            FederationSnapshot offline = Member(1); offline.Available = false;
            FederationSnapshot ended = Member(1, state: "Removed", linked: false);
            FederationSnapshot gone = Member(1, fedState: "Disbanded");

            r.Add(("federation: leader actions appear only for a leader the server says may lead",
                   FederationGate.LeaderActionsOffered(lead) && !FederationGate.LeaderActionsOffered(officer)
                   && !FederationGate.LeaderActionsOffered(member) && !FederationGate.LeaderActionsOffered(offline)
                   && !FederationGate.LeaderActionsOffered(ended) && !FederationGate.LeaderActionsOffered(gone), "may_lead"));
            r.Add(("federation: officer actions appear only where the server says may manage, and Global answers",
                   FederationGate.OfficerActionsOffered(officer) && !FederationGate.OfficerActionsOffered(member)
                   && !FederationGate.OfficerActionsOffered(offline), "may_manage"));

            FederationMember self = lead.Members[0], other = lead.Members[1], left = lead.Members[2];
            r.Add(("federation: a leader may remove or promote another live member, never its own row or a member who left",
                   FederationGate.MayTarget(lead, other) && !FederationGate.MayTarget(lead, self) && !FederationGate.MayTarget(lead, left)
                   && !FederationGate.MayTarget(officer, other), "targets"));
        }

        // Each row is the contract's own: the kind, the payload keys, and the action key under the server's scope.
        private static void Expect(List<(string, bool, string)> r, List<(string, object, string)> sent, string what,
                                   Func<bool> call, string kind, string[] keys, string actionKey, Func<JObject, bool> values = null)
        {
            sent.Clear();
            bool ok = call();
            if (!ok || sent.Count != 1)
            {
                r.Add(($"federation: {what} sends {kind}", false, $"returned {ok}, {sent.Count} send(s)"));
                return;
            }
            (string k, object d, string op) = sent[0];
            JObject data = d == null ? new JObject() : JObject.FromObject(d);
            string[] got = data.Properties().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
            string[] want = keys.OrderBy(n => n, StringComparer.Ordinal).ToArray();
            bool opOk = actionKey == null ? op == null : op != null && op == KmhOpId.For(actionKey);
            if (actionKey != null) KmhOpId.Settled(actionKey);
            bool valOk = values == null || values(data);
            r.Add(($"federation: {what} sends {kind} with exactly [{string.Join(",", want)}]" + (actionKey == null ? " and no op id" : $" under {actionKey.Split('|')[0]}"),
                   k == kind && got.SequenceEqual(want) && opOk && valOk,
                   $"kind={k} keys=[{string.Join(",", got)}] op {(opOk ? "matches" : "DOES NOT MATCH " + actionKey)} values={(valOk ? "ok" : "WRONG " + data.ToString(Formatting.None))}"));
        }

        private static void Requests(List<(string, bool, string)> r)
        {
            OpenGate();
            FederationCache.Clear();
            FederationCache.Apply(Member(1));
            var sent = new List<(string, object, string)>();
            var notices = new List<string>();
            FederationHandler.SenderForTest = (k, d, o) => { sent.Add((k, d, o)); return true; };
            FederationHandler.NoticeForTest = notices.Add;

            Expect(r, sent, "a refresh", () => FederationHandler.RequestSnapshot(true), "kmh.federation.request",
                   new[] { "refresh" }, null, d => (bool)d["refresh"]);
            Expect(r, sent, "create", () => FederationHandler.TryCreate("  Iron   Pact ", "iron", "We hold the line.", "#2980b9"),
                   "kmh.federation.create", new[] { "name", "tag", "description", "color" }, "federation.create|IRON",
                   d => (string)d["name"] == "Iron Pact" && (string)d["tag"] == "IRON" && (string)d["description"] == "We hold the line."
                     && (string)d["color"] == "#2980B9");
            Expect(r, sent, "join", () => FederationHandler.TryJoin(" abcd-efgh "), "kmh.federation.join",
                   new[] { "invite" }, "federation.join|ABCDEFGH", d => (string)d["invite"] == "abcd-efgh");
            Expect(r, sent, "invite", () => FederationHandler.TryInvite(" srv-target "), "kmh.federation.invite",
                   new[] { "target_server" }, "federation.invite|srv-target", d => (string)d["target_server"] == "srv-target");
            Expect(r, sent, "withdrawing an invite", () => FederationHandler.TryRevokeInvite("inv-9"), "kmh.federation.invite_revoke",
                   new[] { "invite_id" }, "federation.invite_revoke|inv-9", d => (string)d["invite_id"] == "inv-9");
            Expect(r, sent, "leave", () => FederationHandler.TryLeave(), "kmh.federation.leave",
                   new string[0], "federation.leave|link-self");
            Expect(r, sent, "remove", () => FederationHandler.TryRemove("link-2"), "kmh.federation.remove",
                   new[] { "link_id" }, "federation.remove|link-2", d => (string)d["link_id"] == "link-2");
            Expect(r, sent, "transfer", () => FederationHandler.TryTransfer("link-3"), "kmh.federation.transfer",
                   new[] { "link_id" }, "federation.transfer|link-3", d => (string)d["link_id"] == "link-3");
            Expect(r, sent, "update", () => FederationHandler.TryUpdate("Iron Pact", "irn", "", ""), "kmh.federation.update",
                   new[] { "name", "tag", "description", "color" }, "federation.update|IRN", d => (string)d["tag"] == "IRN");
            Expect(r, sent, "disband", () => FederationHandler.TryDisband("IRON", "done here"), "kmh.federation.disband",
                   new[] { "confirm", "reason" }, "federation.disband|link-self",
                   d => (string)d["confirm"] == "IRON" && (string)d["reason"] == "done here");
            Expect(r, sent, "re-prove", () => FederationHandler.TryReprove(), "kmh.federation.reprove",
                   new string[0], "federation.reprove|link-self");

            // A double click or a retry is the same action, so it reuses the id; a different federation is a new one.
            sent.Clear();
            FederationHandler.TryCreate("Iron Pact", "IRON", "", "");
            FederationHandler.TryCreate("Iron Pact", "IRON", "", "");
            FederationHandler.TryCreate("Iron Pact", "OTHER", "", "");
            bool reused = sent.Count == 3 && sent[0].Item3 == sent[1].Item3 && sent[0].Item3 != sent[2].Item3;
            KmhOpId.Settled("federation.create|IRON");
            KmhOpId.Settled("federation.create|OTHER");
            r.Add(("federation: a repeated create reuses its op id and a different one does not", reused,
                   sent.Count == 3 ? "" : $"{sent.Count} send(s)"));

            sent.Clear();
            notices.Clear();
            bool none = !FederationHandler.TryCreate("ab", "IRON", "", "") && !FederationHandler.TryCreate("Iron Pact", "I", "", "")
                     && !FederationHandler.TryCreate("Iron Pact", "IR-N", "", "") && !FederationHandler.TryCreate("Iron Pact", "IRON", "a<b", "")
                     && !FederationHandler.TryCreate("Iron Pact", "IRON", "", "#123456") && !FederationHandler.TryCreate("<b>Iron</b>", "IRON", "", "")
                     && !FederationHandler.TryJoin("  -  ") && !FederationHandler.TryInvite("   ");
            r.Add(("federation: a profile or code Global would refuse is not sent at all", none && sent.Count == 0, $"{sent.Count} sent"));
            r.Add(("federation: each of those refusals tells the player why",
                   notices.Count == 8 && notices.All(n => !string.IsNullOrEmpty(n)) && notices.Distinct().Count() >= 5,
                   $"{notices.Count} notice(s), {notices.Distinct().Count()} distinct"));

            FederationCache.Clear();
            sent.Clear();
            bool unlinked = !FederationHandler.TryLeave() && !FederationHandler.TryReprove() && sent.Count == 0;
            r.Add(("federation: leaving or re-proving without a link sends nothing", unlinked, $"{sent.Count} sent"));
            FederationHandler.SenderForTest = null;
            FederationHandler.NoticeForTest = null;
        }

        private static void Disband(List<(string, bool, string)> r)
        {
            r.Add(("federation: the disband confirmation is the tag exactly, capitals included",
                   FederationRules.DisbandConfirmed("IRON", "IRON") && FederationRules.DisbandConfirmed(" IRON ", "IRON")
                   && !FederationRules.DisbandConfirmed("iron", "IRON") && !FederationRules.DisbandConfirmed("IRONS", "IRON")
                   && !FederationRules.DisbandConfirmed("IRO", "IRON") && !FederationRules.DisbandConfirmed(null, "IRON")
                   && !FederationRules.DisbandConfirmed("", ""), "exact, trimmed"));

            OpenGate();
            FederationCache.Clear();
            FederationCache.Apply(Member(1));
            var sent = new List<(string, object, string)>();
            var notices = new List<string>();
            FederationHandler.SenderForTest = (k, d, o) => { sent.Add((k, d, o)); return true; };
            FederationHandler.NoticeForTest = notices.Add;

            bool wrong = !FederationHandler.TryDisband("iron", "") && !FederationHandler.TryDisband("IRO", "")
                      && !FederationHandler.TryDisband("", "") && !FederationHandler.TryDisband(null, "");
            r.Add(("federation: a disband without the exact tag never leaves the client", wrong && sent.Count == 0, $"{sent.Count} sent"));
            r.Add(("federation: a refused disband says the tag must be typed exactly",
                   notices.Count == 4 && notices.Distinct().Count() == 1 && notices[0].Length > 0, notices.FirstOrDefault()));

            sent.Clear();
            bool right = FederationHandler.TryDisband(" IRON ", new string('r', 500));
            JObject d = sent.Count == 1 ? JObject.FromObject(sent[0].Item2) : null;
            KmhOpId.Settled("federation.disband|link-self");
            r.Add(("federation: the exact tag sends the disband, its reason cut to what Global keeps",
                   right && d != null && (string)d["confirm"] == "IRON" && ((string)d["reason"]).Length == FederationRules.ReasonMax,
                   d == null ? $"{sent.Count} sent" : $"confirm={d["confirm"]} reason={((string)d["reason"]).Length} char(s)"));

            FederationCache.Clear();
            sent.Clear();
            r.Add(("federation: with no federation known there is no tag to confirm, so nothing is sent",
                   !FederationHandler.TryDisband("IRON", "") && sent.Count == 0, $"{sent.Count} sent"));
            FederationHandler.SenderForTest = null;
            FederationHandler.NoticeForTest = null;
        }

        private static void Vocabulary(List<(string, bool, string)> r)
        {
            const string unknown = "SomethingNewer";
            void Check(string name, Func<string, string> map, string[] known, bool codes)
            {
                var blank = known.Where(v => string.IsNullOrEmpty(map(v))).ToList();
                var unmapped = codes ? known.Where(v => map(v) == v).ToList() : new List<string>();
                bool passThrough = map(unknown) == unknown;
                r.Add(($"federation: every {name} has words, and an unknown one shows as itself",
                       blank.Count == 0 && unmapped.Count == 0 && passThrough,
                       $"blank=[{string.Join(",", blank)}] unmapped=[{string.Join(",", unmapped)}] unknown->{map(unknown)}"));
            }

            Check("unavailable reason", FederationText.Unavailable,
                  new[] { "feature.off", "global.off", "global.setup", "global.unreachable", "global.unsupported", "global.revoked" }, true);
            Check("link state", FederationText.LinkState,
                  new[] { "Active", "ReproofRequired", "Revoked", "Left", "Removed", "Disbanded" }, false);
            Check("federation state", FederationText.FederationState, new[] { "Active", "Disbanded" }, false);
            Check("objective state", FederationText.ObjectiveState, new[] { "Active", "Completed", "Expired" }, false);
            Check("category", FederationText.Category, new[] { "quest", "world", "site", "road", "frontier", "activity" }, true);
            Check("event kind", FederationText.EventKind,
                  new[] { "Created", "Joined", "Left", "Removed", "LeaderTransferred", "LeaderSucceeded", "ProfileUpdated",
                          "InviteIssued", "InviteRevoked", "ObjectiveCompleted", "LevelReached", "AchievementUnlocked",
                          "Disbanded", "Reproved" }, false);
            string[] achievements = { "founded", "first_ally", "five_servers", "level_3", "level_5", "level_10",
                                      "objectives_5", "objectives_25", "verified_25", "veteran" };
            Check("achievement", FederationText.Achievement, achievements, true);
            Check("benefit", k => FederationText.Benefit(k, 2), new[] { "tag", "site_output", "roadworks", "extra_objective", "renowned" }, true);

            r.Add(("federation: the window lists every achievement the contract names",
                   FederationText.AchievementKeys.SequenceEqual(achievements)
                   && achievements.All(k => FederationText.AchievementWhy(k).Length > 0),
                   string.Join(",", FederationText.AchievementKeys)));

            string objective = FederationText.EventDetail("ObjectiveCompleted", "quest:12");
            string level = FederationText.EventDetail("LevelReached", "5");
            string unlocked = FederationText.EventDetail("AchievementUnlocked", "first_ally");
            string ended = FederationText.EventDetail("Disbanded", "last member left");
            string left = FederationText.EventDetail("Left", "identity_proven");
            string ownWords = FederationText.EventDetail("Disbanded", "merged into the Iron League");
            string profile = FederationText.EventDetail("ProfileUpdated", "name,color");
            string invited = FederationText.EventDetail("InviteIssued", "0123456789abcdef0123456789abcdef");
            string founded = FederationText.EventDetail("Created", "Iron League");
            r.Add(("federation: an event's detail is read the way its kind defines it",
                   objective.Contains(FederationText.Category("quest")) && objective.Contains("12") && level.Contains("5")
                   && unlocked == FederationText.Achievement("first_ally")
                   && ended == FederationText.Reason("last member left") && ended != "last member left"
                   && left == FederationText.Reason("identity_proven") && !left.Contains("identity_proven")
                   && ownWords == "merged into the Iron League" && profile.Contains("name") && profile.Contains("colour")
                   && invited.Contains("01234567") && !invited.Contains("0123456789abcdef0123456789abcdef")
                   && founded == "Iron League",
                   $"{objective} | {level} | {unlocked} | {ended} | {left} | {ownWords} | {profile} | {invited} | {founded}"));

            Check("departure reason", FederationText.Reason,
                  new[] { "guild_disbanded", "unlinked", "removed", "federation disbanded", "last member left",
                          "no active members at upgrade", "identity_proven", "identity_rebound", "server_revoked",
                          "generation_changed", "signing generation changed", "reproof_required", "link_unknown",
                          "data_set_changed", "left_by:Ann", "disbanded_by:Ann" }, true);
            Check("link detail written by an older server", FederationText.LinkDetail,
                  new[] { "the guild was disbanded", "this server's signing generation changed since the link was proved",
                          "Colony Nexus Global requires this link to be proved again",
                          "Colony Nexus Global no longer knows this link",
                          "this data set identity changed since the link was proved", "left by Ann", "disbanded by Ann" }, true);
            string by = FederationText.Reason("left_by:Ann");
            r.Add(("federation: a departure names who made it, and a voluntary one adds nothing",
                   by.Contains("Ann") && !by.Contains("left_by") && FederationText.Reason("") == ""
                   && FederationText.LinkDetail("left by Ann") == by,
                   $"'{by}' | '{FederationText.Reason("")}' | '{FederationText.LinkDetail("left by Ann")}'"));
            r.Add(("federation: the notices draw a reason through the words layer, never as stored",
                   Calls(typeof(Dialog_KMHFederation).FullName, "DrawNotices", typeof(FederationText), nameof(FederationText.Reason))
                   && Calls(typeof(Dialog_KMHFederation).FullName, "DrawNotices", typeof(FederationText), nameof(FederationText.LinkDetail))
                   && Calls(typeof(Dialog_KMHFederation).FullName, "DrawHistory", typeof(FederationText), nameof(FederationText.EventDetail)),
                   "DrawNotices -> Reason + LinkDetail, DrawHistory -> EventDetail"));

            r.Add(("federation: the renown bar fills from this level's floor to the next level",
                   Math.Abs(Dialog_KMHFederation.LevelProgress(new FederationProfile { Renown = 675, LevelFloor = 450, NextLevelAt = 900 }) - 0.5f) < 1e-4
                   && Dialog_KMHFederation.LevelProgress(new FederationProfile { Renown = 12500, LevelFloor = 12000, NextLevelAt = 0 }) == 1f
                   && Dialog_KMHFederation.LevelProgress(new FederationProfile { Renown = 10, LevelFloor = 150, NextLevelAt = 450 }) == 0f,
                   "half, full at the top, empty below the floor"));
        }

        private static bool Calls(string type, string method, Type target, string name)
            => KmhIlReader.MethodsCalledBy(type, method).Any(m => m.DeclaringType == target && m.Name == name);

        private static void Wiring(List<(string, bool, string)> r)
        {
            r.Add(("federation: the Colony Nexus tab gates Guild Federation and the Connected Treasury on their switches",
                   Calls("KMHPatch.Tabs.MainTabWindow_KMH", "FeatureTiles", typeof(FederationGate), "get_FederationOffered")
                   && Calls("KMHPatch.Tabs.MainTabWindow_KMH", "FeatureTiles", typeof(FederationGate), "get_TreasuryOffered"),
                   "FeatureTiles"));
            r.Add(("federation: each window refuses to open while its own switch is off",
                   Calls("KMHPatch.Features.Federation.Dialog_KMHFederation", "Open", typeof(FederationGate), "get_FederationOffered")
                   && Calls("KMHPatch.Dialogs.Dialog_KMHConnectedTreasury", "Open", typeof(FederationGate), "get_TreasuryOffered")
                   && Calls("KMHPatch.Dialogs.Dialog_KMHConnectedGuildTreasury", "Open", typeof(FederationGate), "get_TreasuryOffered"),
                   "Open()"));
            r.Add(("federation: a server switch drops the cached federation",
                   Calls("KMHPatch.SubProtocol.KmhClientCaches", "ClearAll", typeof(FederationCache), "Clear"), "KmhClientCaches.ClearAll"));

            var touched = new List<string>();
            int methods = 0, calls = 0;
            foreach (MethodInfo m in typeof(Dialog_KMHFederation).GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                                                                            | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                methods++;
                foreach (MethodBase called in KmhIlReader.MethodsCalledBy(typeof(Dialog_KMHFederation).FullName, m.Name))
                {
                    calls++;
                    string ns = called.DeclaringType?.Namespace ?? "";
                    if (ns.StartsWith("KMHPatch.Features.ConnectedGuild", StringComparison.Ordinal)
                        || ns.StartsWith("KMHPatch.Features.ConnectedTreasury", StringComparison.Ordinal)
                        || ns.StartsWith("KMHPatch.Features.Treasury", StringComparison.Ordinal))
                        touched.Add(called.DeclaringType.Name + "." + called.Name);
                }
            }
            r.Add(("federation: the Federation window has no treasury balance, deposit or withdraw anywhere in it",
                   touched.Count == 0 && calls > 100,
                   touched.Count == 0 ? $"{calls} call(s) in {methods} method(s) examined" : "REACHES: " + string.Join(", ", touched.Distinct())));

            FederationHandler.Register();
            FederationCache.Clear();
            bool raised = false;
            Action<KMH.Sdk.Client.Events.FederationUpdatedEvent> listener = _ => raised = true;
            Extensibility.KmhClientEventBus.Instance.FederationUpdated += listener;
            try
            {
                var frame = new JObject { ["kind"] = "kmh.federation.snapshot", ["v"] = KmhProtocol.CurrentVersion, ["data"] = FullSnapshot(12) };
                KmhEnvelope env = KmhEnvelope.TryParse(frame.ToString(Formatting.None));
                KmhMainThread.Post(() => KmhDispatcher.Receive(env));
                KmhMainThread.PumpForTest();
            }
            finally { Extensibility.KmhClientEventBus.Instance.FederationUpdated -= listener; }
            r.Add(("federation: a snapshot frame reaches the cache through the dispatcher, and extensions hear of it",
                   KmhDispatcher.IsRegistered("kmh.federation.snapshot") && FederationCache.Snapshot?.Revision == 12 && raised,
                   $"revision={FederationCache.Snapshot?.Revision} event={raised}"));

            string[] legacy = { "GuildConnectedLink", "GuildConnectedReprove", "GuildConnectedRevoke", "GuildConnectedInvite", "GuildConnectedInviteRevoke" };
            var kinds = typeof(KmhProtocol.Kind).GetFields(BindingFlags.Public | BindingFlags.Static)
                                                .Where(f => f.IsLiteral).ToDictionary(f => f.Name, f => (string)f.GetRawConstantValue());
            var federation = new Dictionary<string, string>
            {
                ["FederationRequest"] = "kmh.federation.request", ["FederationCreate"] = "kmh.federation.create",
                ["FederationJoin"] = "kmh.federation.join", ["FederationInvite"] = "kmh.federation.invite",
                ["FederationInviteRevoke"] = "kmh.federation.invite_revoke", ["FederationLeave"] = "kmh.federation.leave",
                ["FederationRemove"] = "kmh.federation.remove", ["FederationTransfer"] = "kmh.federation.transfer",
                ["FederationUpdate"] = "kmh.federation.update", ["FederationDisband"] = "kmh.federation.disband",
                ["FederationReprove"] = "kmh.federation.reprove", ["FederationSnapshot"] = "kmh.federation.snapshot",
            };
            var stillThere = legacy.Where(kinds.ContainsKey).ToList();
            var wrongKinds = federation.Where(kv => !kinds.TryGetValue(kv.Key, out string v) || v != kv.Value).Select(kv => kv.Key).ToList();
            r.Add(("federation: the twelve federation kinds are on the wire and the five legacy link kinds are gone",
                   stillThere.Count == 0 && wrongKinds.Count == 0 && KmhCapabilities.Federation == "federation",
                   $"legacy left=[{string.Join(",", stillThere)}] wrong=[{string.Join(",", wrongKinds)}]"));
        }
    }
}
