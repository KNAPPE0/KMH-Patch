using System;
using System.Collections.Generic;
using KMHPatch.Features.Federation.Dto;
using KMHPatch.UI;

namespace KMHPatch.Features.Federation
{
    // Every string here was written by another server or player, so it is made inert and bounded once, on the way in.
    public static class FederationCache
    {
        public static FederationSnapshot Snapshot { get; private set; }
        public static DateTime LastUpdatedUtc { get; private set; } = DateTime.MinValue;
        public static bool HasSnapshot => Snapshot != null;

        public static event Action Updated;

        internal const int MaxName = 64, MaxTag = 8, MaxShort = 80, MaxText = 240, MaxDescription = 400;
        internal const int MaxMembers = 200, MaxObjectives = 16, MaxAchievements = 32, MaxEvents = 100,
                           MaxCategories = 16, MaxInvites = 50, MaxBenefits = 16;

        internal static void Apply(FederationSnapshot snapshot)
        {
            // Transports reorder; the disconnect clear is what lets a new server's lower revision still apply.
            if (snapshot == null) return;
            if (Snapshot != null && snapshot.Revision < Snapshot.Revision) return;
            Snapshot = Sanitize(snapshot);
            LastUpdatedUtc = DateTime.UtcNow;
            KmhCacheEvents.Raise(Updated, "Federation");
        }

        public static void Clear()
        {
            Snapshot = null;
            LastUpdatedUtc = DateTime.MinValue;
            KmhCacheEvents.Raise(Updated, "Federation");
        }

        public static bool IsLiveLink(FederationSnapshot s)
            => s != null && s.Linked && FederationRules.IsLiveLinkState(s.Link?.State);

        // A link Global has ended: the snapshot then carries only the profile and this server's own record.
        public static bool IsEndedLink(FederationSnapshot s)
            => s != null && !string.IsNullOrEmpty(s.Link?.LinkId) && !string.IsNullOrEmpty(s.Link?.State)
            && !FederationRules.IsLiveLinkState(s.Link.State);

        public static bool IsDisbanded(FederationSnapshot s)
            => s?.Federation != null && s.Federation.State == FederationRules.FederationDisbanded;

        public static string TitleTagFor(string guildName)
        {
            FederationSnapshot s = Snapshot;
            if (s == null || !s.Verified || !IsLiveLink(s) || s.Federation == null) return "";
            if (s.Federation.State != FederationRules.FederationActive || string.IsNullOrEmpty(s.Federation.Tag)) return "";
            if (string.IsNullOrEmpty(guildName) || !string.Equals(s.Guild, Text(guildName, MaxName), StringComparison.OrdinalIgnoreCase)) return "";
            return TagMarkup(s.Federation);
        }

        public static string TagMarkup(FederationProfile f)
        {
            if (f == null || string.IsNullOrEmpty(f.Tag)) return "";
            UnityEngine.Color c = KmhTheme.Accent;
            if (!string.IsNullOrEmpty(f.Color) && KmhTheme.TryHex(f.Color, out UnityEngine.Color parsed)) c = parsed;
            return "<color=" + KmhTheme.Hex(c) + ">[" + f.Tag + "]</color>";
        }

        private static string Text(string s, int max) => KmhDisplayText.Inert(s ?? "", max) ?? "";

        // Not "Prepare": the patch loop hands every class to Harmony, which calls a static Prepare as its own hook.
        private static FederationSnapshot Sanitize(FederationSnapshot s)
        {
            s.Unavailable = Text(s.Unavailable, MaxShort);
            s.VerifiedWhy = Text(s.VerifiedWhy, MaxText);
            s.Guild       = Text(s.Guild, MaxName);
            s.Rank        = Text(s.Rank, MaxShort);
            s.ManageWhy   = Text(s.ManageWhy, MaxText);
            s.LeadWhy     = Text(s.LeadWhy, MaxText);
            s.VerifiedWhyCode = Text(s.VerifiedWhyCode, MaxShort);
            s.ManageWhyCode   = Text(s.ManageWhyCode, MaxShort);
            s.LeadWhyCode     = Text(s.LeadWhyCode, MaxShort);
            s.ServerId    = Text(s.ServerId, MaxShort);

            FederationLink link = s.Link ?? new FederationLink();
            link.LinkId = Text(link.LinkId, MaxShort);
            link.State  = Text(link.State, MaxShort);
            link.Detail = Text(link.Detail, MaxText);
            s.Link = link;

            FederationProfile f = s.Federation ?? new FederationProfile();
            f.Id            = Text(f.Id, MaxShort);
            f.Name          = Text(f.Name, MaxName);
            f.Tag           = Text(f.Tag, MaxTag);
            f.Description   = Text(f.Description, MaxDescription);
            f.Color         = FederationRules.IsHexColour(f.Color) ? f.Color : "";
            f.State         = Text(f.State, MaxShort);
            f.DisbandReason = Text(f.DisbandReason, MaxText);
            f.FounderServer = Text(f.FounderServer, MaxShort);
            f.FounderGuild  = Text(f.FounderGuild, MaxName);
            f.LeaderLinkId  = Text(f.LeaderLinkId, MaxShort);
            f.LeaderServer  = Text(f.LeaderServer, MaxShort);
            f.LeaderGuild   = Text(f.LeaderGuild, MaxName);
            s.Federation = f;

            var members = new List<FederationMember>();
            foreach (FederationMember m in Bounded(s.Members, MaxMembers))
            {
                m.LinkId     = Text(m.LinkId, MaxShort);
                m.ServerId   = Text(m.ServerId, MaxShort);
                m.ServerName = Text(m.ServerName, MaxShort);
                m.Guild      = Text(m.Guild, MaxName);
                m.State      = Text(m.State, MaxShort);
                // The server marks this server's own row; the link id is the fallback when it does not.
                m.IsSelf     = m.IsSelf || (m.LinkId.Length > 0 && m.LinkId == link.LinkId);
                members.Add(m);
            }
            members.Sort((a, b) =>
            {
                int live = FederationRules.IsLiveLinkState(b.State).CompareTo(FederationRules.IsLiveLinkState(a.State));
                if (live != 0) return live;
                int lead = b.IsLeader.CompareTo(a.IsLeader);
                return lead != 0 ? lead : a.JoinedAt.CompareTo(b.JoinedAt);
            });
            s.Members = members;

            var objectives = new List<FederationObjective>();
            foreach (FederationObjective o in Bounded(s.Objectives, MaxObjectives))
            {
                o.Id         = Text(o.Id, MaxShort);
                o.Category   = Text(o.Category, MaxShort);
                o.State      = Text(o.State, MaxShort);
                o.RewardText = Text(o.RewardText, MaxText);
                objectives.Add(o);
            }
            s.Objectives = objectives;

            var achievements = new List<FederationAchievement>();
            foreach (FederationAchievement a in Bounded(s.Achievements, MaxAchievements))
            {
                a.Key = Text(a.Key, MaxShort);
                achievements.Add(a);
            }
            s.Achievements = achievements;

            var events = new List<FederationEvent>();
            foreach (FederationEvent e in Bounded(s.Events, MaxEvents))
            {
                e.Kind   = Text(e.Kind, MaxShort);
                e.Server = Text(e.Server, MaxShort);
                e.Guild  = Text(e.Guild, MaxName);
                e.Detail = Text(e.Detail, MaxText);
                events.Add(e);
            }
            events.Sort((a, b) => b.At.CompareTo(a.At));
            s.Events = events;

            var categories = new List<FederationCategory>();
            foreach (FederationCategory c in Bounded(s.Categories, MaxCategories))
            {
                c.Category = Text(c.Category, MaxShort);
                categories.Add(c);
            }
            s.Categories = categories;

            var invites = new List<FederationInvite>();
            foreach (FederationInvite i in Bounded(s.Invites, MaxInvites))
            {
                i.InviteId     = Text(i.InviteId, MaxShort);
                i.TargetServer = Text(i.TargetServer, MaxShort);
                i.TargetName   = Text(i.TargetName, MaxShort);
                invites.Add(i);
            }
            s.Invites = invites;

            var benefits = new List<FederationBenefit>();
            foreach (FederationBenefit b in Bounded(s.Benefits, MaxBenefits))
            {
                b.Key = Text(b.Key, MaxShort);
                benefits.Add(b);
            }
            s.Benefits = benefits;

            FederationFreshInvite fresh = s.FreshInvite ?? new FederationFreshInvite();
            fresh.Code         = Text(fresh.Code, MaxShort);
            fresh.TargetServer = Text(fresh.TargetServer, MaxShort);
            s.FreshInvite = fresh;
            return s;
        }

        // Nulls a hostile or older server put in a list are dropped, and a list too long to draw is cut short.
        private static IEnumerable<T> Bounded<T>(List<T> list, int max) where T : class
        {
            if (list == null) yield break;
            int n = 0;
            foreach (T item in list)
            {
                if (item == null) continue;
                if (n++ >= max) yield break;
                yield return item;
            }
        }
    }
}
