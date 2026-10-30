using System.Collections.Generic;
using Newtonsoft.Json;

namespace KMHPatch.Features.Federation.Dto
{
    // Shown, never acted on: the server re-checks every permission here when a request lands.
    public class FederationSnapshot
    {
        [JsonProperty("revision")]        public long   Revision { get; set; }
        [JsonProperty("available")]       public bool   Available { get; set; }
        [JsonProperty("unavailable")]     public string Unavailable { get; set; } = "";
        [JsonProperty("verified")]        public bool   Verified { get; set; }
        [JsonProperty("verified_why")]    public string VerifiedWhy { get; set; } = "";
        [JsonProperty("in_guild")]        public bool   InGuild { get; set; }
        [JsonProperty("guild")]           public string Guild { get; set; } = "";
        [JsonProperty("rank")]            public string Rank { get; set; } = "";
        [JsonProperty("may_manage")]      public bool   MayManage { get; set; }
        [JsonProperty("manage_why")]      public string ManageWhy { get; set; } = "";
        [JsonProperty("may_lead")]        public bool   MayLead { get; set; }
        [JsonProperty("lead_why")]        public string LeadWhy { get; set; } = "";
        // The reasons above as codes, so they can be shown translated; the English text stays the fallback.
        [JsonProperty("verified_why_code")] public string VerifiedWhyCode { get; set; } = "";
        [JsonProperty("manage_why_code")]   public string ManageWhyCode { get; set; } = "";
        [JsonProperty("lead_why_code")]     public string LeadWhyCode { get; set; } = "";
        [JsonProperty("server_id")]       public string ServerId { get; set; } = "";
        [JsonProperty("linked")]          public bool   Linked { get; set; }
        [JsonProperty("link")]            public FederationLink Link { get; set; } = new FederationLink();
        [JsonProperty("federation")]      public FederationProfile Federation { get; set; } = new FederationProfile();
        [JsonProperty("members")]         public List<FederationMember> Members { get; set; } = new List<FederationMember>();
        [JsonProperty("objectives")]      public List<FederationObjective> Objectives { get; set; } = new List<FederationObjective>();
        [JsonProperty("achievements")]    public List<FederationAchievement> Achievements { get; set; } = new List<FederationAchievement>();
        [JsonProperty("events")]          public List<FederationEvent> Events { get; set; } = new List<FederationEvent>();
        [JsonProperty("categories")]      public List<FederationCategory> Categories { get; set; } = new List<FederationCategory>();
        [JsonProperty("invites")]         public List<FederationInvite> Invites { get; set; } = new List<FederationInvite>();
        [JsonProperty("benefits")]        public List<FederationBenefit> Benefits { get; set; } = new List<FederationBenefit>();
        [JsonProperty("fresh_invite")]    public FederationFreshInvite FreshInvite { get; set; } = new FederationFreshInvite();
        [JsonProperty("synced_at")]       public long   SyncedAt { get; set; }
        [JsonProperty("stale")]           public bool   Stale { get; set; }
        // When this server stops the level bonuses if Global stays silent; 0 when none apply, or from an older server.
        [JsonProperty("benefits_until")]  public long   BenefitsUntil { get; set; }
        [JsonProperty("pending_reports")] public long   PendingReports { get; set; }
        [JsonProperty("lost_reports")]    public long   LostReports { get; set; }
    }

    public class FederationLink
    {
        [JsonProperty("link_id")]   public string LinkId { get; set; } = "";
        [JsonProperty("state")]     public string State { get; set; } = "";
        [JsonProperty("detail")]    public string Detail { get; set; } = "";
        [JsonProperty("joined_at")] public long   JoinedAt { get; set; }
        [JsonProperty("is_leader")] public bool   IsLeader { get; set; }
    }

    public class FederationProfile
    {
        [JsonProperty("id")]                        public string Id { get; set; } = "";
        [JsonProperty("name")]                      public string Name { get; set; } = "";
        [JsonProperty("tag")]                       public string Tag { get; set; } = "";
        [JsonProperty("description")]               public string Description { get; set; } = "";
        [JsonProperty("color")]                     public string Color { get; set; } = "";
        [JsonProperty("state")]                     public string State { get; set; } = "";
        [JsonProperty("created_at")]                public long   CreatedAt { get; set; }
        [JsonProperty("disbanded_at")]              public long   DisbandedAt { get; set; }
        [JsonProperty("disband_reason")]            public string DisbandReason { get; set; } = "";
        [JsonProperty("founder_server")]            public string FounderServer { get; set; } = "";
        [JsonProperty("founder_guild")]             public string FounderGuild { get; set; } = "";
        [JsonProperty("leader_link_id")]            public string LeaderLinkId { get; set; } = "";
        [JsonProperty("leader_server")]             public string LeaderServer { get; set; } = "";
        [JsonProperty("leader_guild")]              public string LeaderGuild { get; set; } = "";
        [JsonProperty("level")]                     public int    Level { get; set; } = 1;
        [JsonProperty("renown")]                    public long   Renown { get; set; }
        [JsonProperty("level_floor")]               public long   LevelFloor { get; set; }
        [JsonProperty("next_level_at")]             public long   NextLevelAt { get; set; }
        [JsonProperty("active_members")]            public long   ActiveMembers { get; set; }
        [JsonProperty("lifetime_members")]          public long   LifetimeMembers { get; set; }
        [JsonProperty("verified_players_30d")]      public long   VerifiedPlayers30d { get; set; }
        [JsonProperty("lifetime_verified_players")] public long   LifetimeVerifiedPlayers { get; set; }
        [JsonProperty("objectives_completed")]      public long   ObjectivesCompleted { get; set; }
        [JsonProperty("renown_today")]              public long   RenownToday { get; set; }
        [JsonProperty("renown_day_cap")]            public long   RenownDayCap { get; set; }
    }

    public class FederationMember
    {
        [JsonProperty("link_id")]      public string LinkId { get; set; } = "";
        [JsonProperty("server_id")]    public string ServerId { get; set; } = "";
        [JsonProperty("server_name")]  public string ServerName { get; set; } = "";
        [JsonProperty("guild")]        public string Guild { get; set; } = "";
        [JsonProperty("state")]        public string State { get; set; } = "";
        [JsonProperty("joined_at")]    public long   JoinedAt { get; set; }
        [JsonProperty("is_leader")]    public bool   IsLeader { get; set; }
        [JsonProperty("is_self")]      public bool   IsSelf { get; set; }
        [JsonProperty("online")]       public bool   Online { get; set; }
        [JsonProperty("renown_total")] public long   RenownTotal { get; set; }
        [JsonProperty("renown_7d")]    public long   Renown7d { get; set; }
    }

    public class FederationObjective
    {
        [JsonProperty("id")]              public string Id { get; set; } = "";
        [JsonProperty("category")]        public string Category { get; set; } = "";
        [JsonProperty("target")]          public long   Target { get; set; }
        [JsonProperty("progress")]        public long   Progress { get; set; }
        [JsonProperty("starts_at")]       public long   StartsAt { get; set; }
        [JsonProperty("ends_at")]         public long   EndsAt { get; set; }
        [JsonProperty("state")]           public string State { get; set; } = "";
        [JsonProperty("reward_renown")]   public long   RewardRenown { get; set; }
        [JsonProperty("my_progress")]     public long   MyProgress { get; set; }
        [JsonProperty("reward_eligible")] public bool   RewardEligible { get; set; }
        [JsonProperty("reward_granted")]  public bool   RewardGranted { get; set; }
        [JsonProperty("reward_text")]     public string RewardText { get; set; } = "";
    }

    public class FederationAchievement
    {
        [JsonProperty("key")]         public string Key { get; set; } = "";
        [JsonProperty("achieved_at")] public long   AchievedAt { get; set; }
    }

    public class FederationEvent
    {
        [JsonProperty("at")]     public long   At { get; set; }
        [JsonProperty("kind")]   public string Kind { get; set; } = "";
        [JsonProperty("server")] public string Server { get; set; } = "";
        [JsonProperty("guild")]  public string Guild { get; set; } = "";
        [JsonProperty("detail")] public string Detail { get; set; } = "";
    }

    public class FederationCategory
    {
        [JsonProperty("category")] public string Category { get; set; } = "";
        [JsonProperty("total")]    public long   Total { get; set; }
        [JsonProperty("last_7d")]  public long   Last7d { get; set; }
    }

    public class FederationInvite
    {
        [JsonProperty("invite_id")]     public string InviteId { get; set; } = "";
        [JsonProperty("target_server")] public string TargetServer { get; set; } = "";
        [JsonProperty("target_name")]   public string TargetName { get; set; } = "";
        [JsonProperty("expires_at")]    public long   ExpiresAt { get; set; }
        [JsonProperty("mine")]          public bool   Mine { get; set; }
    }

    public class FederationBenefit
    {
        [JsonProperty("key")]    public string Key { get; set; } = "";
        [JsonProperty("level")]  public int    Level { get; set; }
        [JsonProperty("active")] public bool   Active { get; set; }
        // A percentage for the output bonuses; fractional so a scaled value cannot fail the whole snapshot's parse.
        [JsonProperty("value")]  public double Value { get; set; }
    }

    // Filled only for the session that just created the invite; the server holds it in memory, never on disk.
    public class FederationFreshInvite
    {
        [JsonProperty("code")]          public string Code { get; set; } = "";
        [JsonProperty("target_server")] public string TargetServer { get; set; } = "";
        [JsonProperty("expires_at")]    public long   ExpiresAt { get; set; }
    }
}
