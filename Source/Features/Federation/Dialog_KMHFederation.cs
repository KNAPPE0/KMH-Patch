using System;
using System.Collections.Generic;
using KMHPatch.Features.Federation.Dto;
using KMHPatch.UI;
using RimWorld;
using UnityEngine;
using Verse;

namespace KMHPatch.Features.Federation
{
    // Membership, renown and objectives only. No silver, item or resource moves through this window.
    public class Dialog_KMHFederation : Window_KMHBase
    {
        public override Vector2 InitialSize => DialogLayout.FitToScreen(780f, 660f);

        private enum Tab { Overview, Members, Objectives, History, Manage }

        private Tab _tab = Tab.Overview;
        private readonly string[] _tabLabels;
        private readonly Action<int> _onSelectTab;

        private Vector2 _scroll;
        // BeginScrollView reads _viewHeight and the draw writes _measuredHeight, so the viewRect cannot change mid-frame.
        private float _viewHeight = 600f;
        private float _measuredHeight = 600f;
        private int _lastScrollFrame = -1;

        private bool _askedFresh;
        private float _nextFreshRealtime;
        private bool _typing;

        // The server reads Global for this window at most once per 30 s per link, so asking sooner is wasted.
        private const float FreshEverySeconds = 30f;
        private const string FieldPrefix = "kmhFed_";
        private const float LabelW = 170f;
        private const float MemberRowH = 50f;
        // KMH Global's EventLimit: the view carries this many of the newest events.
        private const int HistoryShown = 40;

        private string _createName = "", _createTag = "", _createDescription = "", _createColor = "";
        private string _joinCode = "";
        private string _inviteTarget = "";
        private string _editName = "", _editTag = "", _editDescription = "", _editColor = "";
        private string _editLoadedFor;
        private string _disbandConfirm = "", _disbandReason = "";

        private FederationSnapshot _achievedFor;
        private readonly Dictionary<string, long> _achieved = new Dictionary<string, long>(StringComparer.Ordinal);

        public Dialog_KMHFederation()
        {
            doCloseX = true;
            forcePause = false;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
            draggable = true;
            resizeable = true;
            minSize = new Vector2(560f, 420f);

            _tabLabels = new[]
            {
                KmhText.Get("KMH_Fed_TabOverview", "Overview"),
                KmhText.Get("KMH_Fed_TabMembers", "Member servers"),
                KmhText.Get("KMH_Fed_TabObjectives", "Objectives"),
                KmhText.Get("KMH_Fed_TabHistory", "History"),
                KmhText.Get("KMH_Fed_TabManage", "Manage"),
            };
            _onSelectTab = SelectTab;

            FederationCache.Updated += OnUpdated;
            EnableAutoRefresh(Ask);
            // The Overview's guild box reads the guild's own snapshot, which this window may be the first to need.
            Guilds.GuildHandler.RequestSnapshot();
        }

        public static void Open()
        {
            if (!FederationGate.FederationOffered) return;
            if (Find.WindowStack.IsOpen<Dialog_KMHFederation>()) return;
            Find.WindowStack.Add(new Dialog_KMHFederation());
        }

        public override void PostClose()
        {
            base.PostClose();
            FederationCache.Updated -= OnUpdated;
        }

        private void OnUpdated()
        {
            if (FederationCache.HasSnapshot) MarkRefreshed();
        }

        // The first ask reads Global again; the timer after it only asks for the server's cached view.
        private void Ask()
        {
            FederationHandler.RequestSnapshot(!_askedFresh);
            if (!_askedFresh) _nextFreshRealtime = Time.realtimeSinceStartup + FreshEverySeconds;
            _askedFresh = true;
        }

        private void SelectTab(int i)
        {
            if (i == (int)_tab) return;
            _tab = (Tab)i;
            _scroll = Vector2.zero;
        }

        protected override Features.Discord.KmhActivity PresenceActivity => Features.Discord.KmhActivity.Guild;

        protected override bool TypingInThisWindow => _typing;

        protected override void DrawContents(Rect inRect)
        {
            // A new hello can switch the feature off while this is open, and nothing behind it would answer.
            if (!FederationGate.FederationOffered) { Close(false); return; }
            if (KmhScroll.NewFrame(ref _lastScrollFrame)) _viewHeight = _measuredHeight;

            float y = DialogLayout.DrawTitle(inRect, KmhText.Get("KMH_Fed_Title", "Guild Federation"), DialogLayout.LiveBadgeReserve);
            DialogLayout.DrawLiveBadge(inRect, SecondsSinceRefresh, HasReceivedData, 0f);

            FederationSnapshot s = FederationCache.Snapshot;
            if (s == null)
            {
                string wait = WaitedTooLong
                    ? KmhText.Get("KMH_Fed_NoAnswer", "The server has not answered yet. This window keeps asking.")
                    : KmhText.Get("KMH_Fed_Waiting", "Asking the server for your guild's federation…");
                if (WaitedTooLong) DialogLayout.LabelTrunc(new Rect(0f, y + 4f, inRect.width, 24f), Muted(wait));
                else DialogLayout.DrawWaiting(new Rect(0f, y + 4f, inRect.width, 32f), wait);
                _typing = false;
                if (DialogLayout.DrawCloseButton(inRect)) Close();
                return;
            }

            y = DialogLayout.TabRow(y, inRect.width, _tabLabels, (int)_tab, _onSelectTab);
            Rect body = new Rect(0f, y, inRect.width, DialogLayout.BodyHeight(inRect, y));
            Widgets.DrawMenuSection(body);
            DrawBody(body.ContractedBy(6f), s);
            DrawFooter(inRect, s);
            if (DialogLayout.DrawCloseButton(inRect)) Close();

            string focused = GUI.GetNameOfFocusedControl();
            _typing = !string.IsNullOrEmpty(focused) && focused.StartsWith(FieldPrefix, StringComparison.Ordinal);
        }

        private void DrawBody(Rect box, FederationSnapshot s)
        {
            if (box.height <= 0f) return;
            // Always reserved: reserving only on overflow oscillates, as the bar narrows text and wrapping grows it.
            float viewW = Mathf.Max(1f, box.width - DialogLayout.ScrollbarReserveWidth);
            float viewH = Mathf.Max(_viewHeight, box.height);
            Rect viewRect = new Rect(0f, 0f, viewW, viewH);
            _scroll.y = KmhScroll.Clamp(_scroll.y, viewH, box.height);

            Widgets.BeginScrollView(box, ref _scroll, viewRect);
            Listing_Standard l = new Listing_Standard();
            l.Begin(viewRect);
            float measured = viewH;
            try
            {
                DrawNotices(l, s);
                switch (_tab)
                {
                    case Tab.Overview:   DrawOverview(l, s); break;
                    case Tab.Members:    DrawMembers(l, s); break;
                    case Tab.Objectives: DrawObjectives(l, s); break;
                    case Tab.History:    DrawHistory(l, s); break;
                    default:             DrawManage(l, s); break;
                }
            }
            finally
            {
                measured = l.CurHeight;
                l.End();
                Widgets.EndScrollView();
            }
            _measuredHeight = Mathf.Max(measured + 8f, box.height);
        }

        private void DrawFooter(Rect inRect, FederationSnapshot s)
        {
            float fy = inRect.height - DialogLayout.FooterReserve + 4f;
            bool canAsk = Time.realtimeSinceStartup >= _nextFreshRealtime;
            Rect refresh = new Rect(0f, fy, 120f, DialogLayout.CloseBtnHeight);
            if (Widgets.ButtonText(refresh, KmhText.Get("KMH_Fed_Refresh", "Refresh"), active: canAsk) && canAsk)
            {
                FederationHandler.RequestSnapshot(true);
                _nextFreshRealtime = Time.realtimeSinceStartup + FreshEverySeconds;
            }
            // A greyed button with no reason reads as broken.
            if (!canAsk && Mouse.IsOver(refresh))
                TooltipHandler.TipRegion(refresh, KmhText.Get("KMH_Fed_RefreshWait", "Just refreshed. You can ask again in {0} s.",
                    Mathf.CeilToInt(_nextFreshRealtime - Time.realtimeSinceStartup)));
            string synced = FederationText.SyncLine(s, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            float w = inRect.width - 128f - DialogLayout.CloseBtnWidth - 12f;
            if (w > 20f) DialogLayout.LabelTrunc(new Rect(128f, fy + 5f, w, 24f), Muted(synced));
        }

        private static void DrawNotices(Listing_Standard l, FederationSnapshot s)
        {
            bool any = false;
            if (!s.Available)
            {
                l.Label(Warn(FederationText.Unavailable(s.Unavailable.Length > 0 ? s.Unavailable : "global.unreachable")));
                if (s.InGuild)
                    l.Label(Muted(KmhText.Get("KMH_Fed_RefusedWhileDown", "Until it answers, federation changes are refused, not queued: try again later. Your guild works normally.")));
                any = true;
            }
            else if (s.Stale)
            {
                l.Label(Warn(KmhText.Get("KMH_Fed_Stale", "Colony Nexus Global has not answered since {0}, so this is the last view this server had.",
                                         FederationText.When(s.SyncedAt))));
                any = true;
            }
            // The footer cuts this short at narrow widths, and when bonuses stop is what a player needs from an outage.
            if ((s.Stale || !s.Available) && s.BenefitsUntil > 0)
                l.Label(Muted(s.BenefitsUntil > DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                    ? KmhText.Get("KMH_Fed_BonusesUntil", "Bonuses on this server continue until {0}, then pause until Colony Nexus Global answers.", FederationText.When(s.BenefitsUntil))
                    : KmhText.Get("KMH_Fed_BonusesPaused", "Bonuses on this server are paused until Colony Nexus Global answers.")));

            if (!s.InGuild)
            {
                l.Label(KmhText.Get("KMH_Fed_NoGuild", "You are not in a guild. A federation joins guilds together, so create or join one in the Guild Hall first."));
                any = true;
            }
            else if (!s.Verified)
            {
                l.Label(Warn(KmhText.Get("KMH_Fed_Unverified", "Colony Nexus Global has not verified who you are on this server, so you can look but not manage, and nothing you do counts toward the federation.")));
                string why = UnverifiedWhy(s, Global.KmhJoinTicketClient.CurrentProblemText());
                if (why.Length > 0) l.Label(Muted(why));
                any = true;
            }

            if (FederationCache.IsDisbanded(s))
            {
                string reason = s.Federation.DisbandReason.Length > 0
                    ? FederationText.Reason(s.Federation.DisbandReason)
                    : KmhText.Get("KMH_Fed_NoReason", "no reason given");
                l.Label(Warn(KmhText.Get("KMH_Fed_DisbandedNotice", "This federation was disbanded on {0}: {1}. It can never become active again.",
                                         FederationText.When(s.Federation.DisbandedAt), reason)));
                any = true;
            }
            else if (FederationCache.IsEndedLink(s))
            {
                l.Label(Warn(EndedText(s.Link.State)));
                if (s.Link.Detail.Length > 0) l.Label(Muted(FederationText.LinkDetail(s.Link.Detail)));
                any = true;
            }
            else if (s.Link.State == FederationRules.LinkReproof)
            {
                l.Label(Warn(KmhText.Get("KMH_Fed_ReproofNotice", "This server's link must be proved again before it counts for the federation. A guild Admin or the Owner can do that under Manage.")));
                if (s.Link.Detail.Length > 0) l.Label(Muted(FederationText.LinkDetail(s.Link.Detail)));
                any = true;
            }

            if (any) l.GapLine(8f);
        }

        private static string EndedText(string state)
        {
            switch (state)
            {
                case "Left":      return KmhText.Get("KMH_Fed_EndedLeft", "This server's guild has left the federation.");
                case "Removed":   return KmhText.Get("KMH_Fed_EndedRemoved", "The federation's leader removed this server's guild from it.");
                case "Disbanded": return KmhText.Get("KMH_Fed_EndedDisbanded", "The federation this server's guild belonged to was disbanded.");
                case "Revoked":   return KmhText.Get("KMH_Fed_EndedRevoked", "This server's link to the federation was revoked.");
                default:          return KmhText.Get("KMH_Fed_EndedOther", "This server's guild is no longer a member ({0}).", FederationText.LinkState(state));
            }
        }

        private void DrawOverview(Listing_Standard l, FederationSnapshot s)
        {
            bool linked = FederationCache.IsLiveLink(s) || FederationCache.IsEndedLink(s);
            Box(l, "place", KmhBrand.Accent, KmhText.Get("KMH_Fed_PlaceTitle", "Your place"), null, b => DrawPlace(b, s));
            Box(l, "guild", KmhBrand.Ok, KmhText.Get("KMH_Fed_BoxGuildTitle", "Your guild — this server only"), null, b => DrawGuildBox(b, s));
            Box(l, "federation", KmhBrand.Info, KmhText.Get("KMH_Fed_BoxFedTitle", "Your federation — kept by Colony Nexus Global, as this server passes it on"),
                null, b => DrawFederationBox(b, s, linked));
            Box(l, "server", KmhBrand.Warn, KmhText.Get("KMH_Fed_BoxServerTitle", "What each server does for itself"), null, b => DrawServerBox(b, s));
            l.Label("<b>" + KmhText.Get("KMH_Fed_NeverShared", "Never shared: silver, items, colonists, vault contents, chat, member names and ranks.") + "</b>");

            if (!linked) return;
            l.GapLine(8f);
            Heading(l, KmhText.Get("KMH_Fed_Achievements", "Achievements"));
            DrawAchievements(l, s);

            if (s.Categories.Count == 0) return;
            l.GapLine(8f);
            Heading(l, KmhText.Get("KMH_Fed_Contributions", "Contributions"));
            foreach (FederationCategory c in s.Categories)
                Pair(l, FederationText.Category(c.Category),
                     KmhText.Get("KMH_Fed_CategoryCounts", "{0} in all · {1} in the last 7 days", c.Total.ToString("N0"), c.Last7d.ToString("N0")));
        }

        // Measured last frame, so a box is as tall as its text in any language.
        private void Box(Listing_Standard l, string id, Color stripe, string title, string subtitle, Action<Listing_Standard> body)
        {
            float h = _boxH.TryGetValue(id, out float known) ? known : 80f;
            Rect r = l.GetRect(h);
            Widgets.DrawMenuSection(r);
            Widgets.DrawBoxSolid(new Rect(r.x, r.y, 3f, r.height), stripe);
            Rect inner = new Rect(r.x + 12f, r.y + 6f, Mathf.Max(1f, r.width - 18f), Mathf.Max(1f, r.height - 12f));
            var b = new Listing_Standard();
            b.Begin(inner);
            float used;
            try
            {
                b.Label("<b>" + title + "</b>");
                if (!string.IsNullOrEmpty(subtitle)) b.Label(Muted(subtitle));
                body(b);
            }
            finally
            {
                used = b.CurHeight;
                b.End();
            }
            _boxH[id] = Mathf.Ceil(used + 14f);
            l.Gap(6f);
        }

        private readonly Dictionary<string, float> _boxH = new Dictionary<string, float>(StringComparer.Ordinal);

        private static void DrawPlace(Listing_Standard l, FederationSnapshot s)
        {
            // The directory's name for this server, or its own if that does not pose as Colony Nexus.
            string server = KMHPatch.SubProtocol.KmhDispatcher.ServerName.Length > 0 ? KMHPatch.SubProtocol.KmhServerLabel.Current() : "";
            Pair(l, KmhText.Get("KMH_Fed_PlaceServer", "Server"), server.Length > 0 ? server : KmhText.Get("KMH_Fed_SelfMark", "this server"));
            if (!s.InGuild)
            {
                Pair(l, KmhText.Get("KMH_Fed_PlaceGuild", "Guild"), KmhText.Get("KMH_Fed_SyncNoGuild", "Join a guild first"));
                return;
            }
            Pair(l, KmhText.Get("KMH_Fed_PlaceGuild", "Guild"), s.Guild);
            Pair(l, KmhText.Get("KMH_Fed_PlaceRank", "Your rank"), FederationText.Rank(s.Rank));
            l.Label(KmhText.Get("KMH_Fed_YouCan", "You can:"));
            foreach (string line in YouCan(s)) l.Label("• " + line);
            foreach (string line in NotYet(s)) l.Label(Muted("○ " + line));
        }

        // What this player may do, from the server's may_manage and may_lead, never guessed from the rank alone.
        internal static List<string> YouCan(FederationSnapshot s)
        {
            var can = new List<string> { KmhText.Get("KMH_Fed_CanView", "See the federation, its member servers, objectives and history") };
            if (s.MayManage)
                can.Add(FederationCache.IsLiveLink(s)
                    ? KmhText.Get("KMH_Fed_CanManage", "Invite servers, withdraw invites and take your guild out of the federation")
                    : KmhText.Get("KMH_Fed_CanStart", "Start a federation, or join one with an invite"));
            if (s.MayLead)
                can.Add(KmhText.Get("KMH_Fed_CanLead", "Lead it: edit the profile, remove member servers, hand over leadership or disband"));
            if (s.Verified && FederationCache.IsLiveLink(s))
                can.Add(KmhText.Get("KMH_Fed_CanCount", "Earn renown: your quests and daily activity count toward the federation"));
            return can;
        }

        internal static List<string> NotYet(FederationSnapshot s)
        {
            var not = new List<string>();
            if (!s.MayManage)
                not.Add(ManageRefusal(s));
            else if (FederationCache.IsLiveLink(s) && !s.MayLead)
                not.Add(LeadRefusal(s));
            if (!s.Verified)
                not.Add(KmhText.Get("KMH_Fed_NotCounting", "What you do does not count toward a federation until Colony Nexus Global confirms who you are on this server."));
            return not;
        }

        private static void DrawGuildBox(Listing_Standard l, FederationSnapshot s)
        {
            l.Label(Muted(KmhText.Get("KMH_Fed_BoxGuildBody", "Members and ranks, the guild vault, perks, sites, guild chat and alliances with other guilds here. The Guild Hall manages them, and none of it leaves this server.")));
            Guilds.Dto.GuildSnapshot g = s.InGuild ? Guilds.GuildCache.Guild : null;
            if (g == null) return;
            int allies = 0;
            if (g.Relationships != null)
                foreach (KeyValuePair<string, string> kv in g.Relationships) if (kv.Value == Guilds.Dto.GuildSnapshot.RelationAllied) allies++;
            Pair(l, KmhText.Get("KMH_Fed_PlaceGuild", "Guild"),
                 KmhText.Get("KMH_Fed_BoxGuildLine", "{0} member(s) · {1} local alliance(s)", g.Members?.Count ?? 0, allies));
            Rect row = l.GetRect(28f);
            string open = KmhText.Get("KMH_Fed_OpenHall", "Open the Guild Hall");
            if (Widgets.ButtonText(new Rect(row.x, row.y, DialogLayout.ButtonW(open, 140f, row.width), 26f), open)
                && !Find.WindowStack.IsOpen<Guilds.Dialog_KMHGuildHall>())
                Find.WindowStack.Add(new Guilds.Dialog_KMHGuildHall());
        }

        private static void DrawFederationBox(Listing_Standard l, FederationSnapshot s, bool linked)
        {
            if (!linked)
            {
                l.Label(s.InGuild ? KmhText.Get("KMH_Fed_NotLinked", "Your guild is not in a federation.")
                                  : KmhText.Get("KMH_Fed_SyncNoGuild", "Join a guild first"));
                l.Label(Muted(KmhText.Get("KMH_Fed_About", "A Guild Federation joins guilds on different servers under one name and tag. Together they earn renown, share weekly objectives and unlock small bonuses on each member's own server. Nothing moves silver, items or resources between servers.")));
                if (s.InGuild)
                    l.Label(Muted(s.MayManage
                        ? KmhText.Get("KMH_Fed_StartHint", "To start one, or to join with an invite, open Manage.")
                        : KmhText.Get("KMH_Fed_StartHintOfficer", "An Admin or the Owner of your guild can start one, or join one with an invite, under Manage.")));
                return;
            }

            FederationProfile f = s.Federation;
            Text.Font = GameFont.Medium;
            string tag = FederationCache.TagMarkup(f);
            DialogLayout.LabelTrunc(l.GetRect(Text.LineHeight), (tag.Length > 0 ? tag + " " : "") + f.Name);
            Text.Font = GameFont.Small;
            if (f.Description.Length > 0) l.Label(f.Description);

            l.Label(KmhText.Get("KMH_Fed_LevelLine", "Level {0} · {1} renown", f.Level, f.Renown.ToString("N0")));
            DialogLayout.DrawProgress(l.GetRect(16f).ContractedBy(0f, 5f), LevelProgress(f));
            l.Label(Muted(f.NextLevelAt > f.Renown
                ? KmhText.Get("KMH_Fed_NextLevel", "{0} renown to the next level", (f.NextLevelAt - f.Renown).ToString("N0"))
                : KmhText.Get("KMH_Fed_TopLevel", "The highest level there is.")));
            l.Label(Muted(KmhText.Get("KMH_Fed_Today", "Today: {0} of the {1} renown the federation can earn in a day",
                                      f.RenownToday.ToString("N0"), f.RenownDayCap.ToString("N0"))));
            l.Gap(4f);

            Pair(l, KmhText.Get("KMH_Fed_Status", "Status"), FederationText.FederationState(f.State));
            Pair(l, KmhText.Get("KMH_Fed_Leader", "Leader"), ServerGuild(f.LeaderServer, f.LeaderGuild));
            Pair(l, KmhText.Get("KMH_Fed_Founded", "Founded"),
                 KmhText.Get("KMH_Fed_FoundedBy", "{0} by {1}", FederationText.When(f.CreatedAt), ServerGuild(f.FounderServer, f.FounderGuild)));
            Pair(l, KmhText.Get("KMH_Fed_MembersLabel", "Member servers"),
                 KmhText.Get("KMH_Fed_MemberCounts", "{0} now · {1} ever", f.ActiveMembers, f.LifetimeMembers));
            Pair(l, KmhText.Get("KMH_Fed_Verified", "Players who took part"),
                 KmhText.Get("KMH_Fed_VerifiedCounts", "{0} in the last 30 days · {1} ever", f.VerifiedPlayers30d, f.LifetimeVerifiedPlayers));
            Pair(l, KmhText.Get("KMH_Fed_ObjectivesDone", "Objectives completed"), f.ObjectivesCompleted.ToString("N0"));
            Pair(l, KmhText.Get("KMH_Fed_WeekLabel", "This week"), WeekLine(s));
            Pair(l, KmhText.Get("KMH_Fed_Achievements", "Achievements"),
                 KmhText.Get("KMH_Fed_AchievedCount", "{0} of {1} earned", s.Achievements.Count, FederationText.AchievementKeys.Length));
            Pair(l, KmhText.Get("KMH_Fed_LastEvent", "Latest"), s.Events.Count == 0
                 ? KmhText.Get("KMH_Fed_NoEvents", "Nothing has happened yet.")
                 : FederationText.When(s.Events[0].At) + "  " + FederationText.EventKind(s.Events[0].Kind) + "  " + ServerGuild(s.Events[0].Server, s.Events[0].Guild));
            Pair(l, KmhText.Get("KMH_Fed_ThisServer", "This server"), LinkLine(s));
        }

        private static string WeekLine(FederationSnapshot s)
        {
            if (s.Objectives.Count == 0) return KmhText.Get("KMH_Fed_WeekNone", "no objectives set yet");
            int done = 0;
            long ends = 0;
            foreach (FederationObjective o in s.Objectives)
            {
                if (o.State == "Completed") done++;
                if (o.EndsAt > ends) ends = o.EndsAt;
            }
            return KmhText.Get("KMH_Fed_WeekCounts", "{0} of {1} objectives done, ends in {2}", done, s.Objectives.Count, FederationText.TimeLeft(ends));
        }

        private static void DrawServerBox(Listing_Standard l, FederationSnapshot s)
        {
            l.Label(Muted(KmhText.Get("KMH_Fed_BoxServerBody", "Each member server applies the federation's bonuses on its own, capped by its owner, and makes its own reward caches for completed objectives, put in your guild's vault here.")));
            Heading(l, KmhText.Get("KMH_Fed_Benefits", "Benefits"));
            DrawBenefits(l, s);
            if (s.PendingReports > 0)
                l.Label(Muted(KmhText.Get("KMH_Fed_PendingReports", "{0} contribution(s) from this server are waiting to reach Colony Nexus Global.", s.PendingReports)));
            if (s.LostReports > 0)
                l.Label(Muted(KmhText.Get("KMH_Fed_LostReports", "{0} contribution(s) from this server were never counted: Global was out of reach for too long, or the queue was full.", s.LostReports)));
        }

        internal static float LevelProgress(FederationProfile f)
        {
            if (f == null) return 0f;
            long span = f.NextLevelAt - f.LevelFloor;
            if (f.NextLevelAt <= 0 || span <= 0) return 1f;
            return Mathf.Clamp01((f.Renown - f.LevelFloor) / (float)span);
        }

        private static string LinkLine(FederationSnapshot s)
        {
            string line = FederationText.LinkState(s.Link.State);
            if (s.Link.IsLeader) line += " · " + KmhText.Get("KMH_Fed_LeaderMark", "leader");
            if (s.Link.JoinedAt > 0) line += " · " + KmhText.Get("KMH_Fed_Joined", "joined {0}", FederationText.When(s.Link.JoinedAt));
            return line;
        }

        private static void DrawBenefits(Listing_Standard l, FederationSnapshot s)
        {
            l.Label(Muted(KmhText.Get("KMH_Fed_BenefitsHelp", "Benefits apply on each member's own server while its link and the federation are active. If Colony Nexus Global cannot be reached they keep working for 72 hours, then pause until it answers.")));
            if (s.Benefits.Count == 0)
            {
                l.Label(Muted(KmhText.Get("KMH_Fed_NoBenefits", "No benefits reported yet.")));
                return;
            }
            foreach (FederationBenefit b in s.Benefits)
            {
                string text = FederationText.Benefit(b.Key, b.Value);
                string from = KmhText.Get("KMH_Fed_FromLevel", "from level {0}", b.Level);
                l.Label(b.Active ? Good("✓ " + text) + "  " + Muted(from) : Muted("○ " + text + "  " + from));
            }
        }

        private void DrawAchievements(Listing_Standard l, FederationSnapshot s)
        {
            // Rebuilt only when a new snapshot lands, not on every frame the tab is open.
            if (!ReferenceEquals(_achievedFor, s))
            {
                _achieved.Clear();
                foreach (FederationAchievement a in s.Achievements)
                    if (a.Key.Length > 0 && !_achieved.ContainsKey(a.Key)) _achieved[a.Key] = a.AchievedAt;
                _achievedFor = s;
            }
            foreach (string key in FederationText.AchievementKeys) AchievementRow(l, key);
            foreach (KeyValuePair<string, long> kv in _achieved)
                if (Array.IndexOf(FederationText.AchievementKeys, kv.Key) < 0) AchievementRow(l, kv.Key);
        }

        private void AchievementRow(Listing_Standard l, string key)
        {
            Rect row = l.GetRect(DialogLayout.TextRowH);
            string text = _achieved.TryGetValue(key, out long at)
                ? Good("✓ " + FederationText.Achievement(key)) + "  " + Muted(FederationText.When(at))
                : Muted("○ " + FederationText.Achievement(key));
            DialogLayout.LabelTrunc(row, text);
            string why = FederationText.AchievementWhy(key);
            if (why.Length > 0 && Mouse.IsOver(row)) TooltipHandler.TipRegion(row, why);
        }

        private static void DrawMembers(Listing_Standard l, FederationSnapshot s)
        {
            if (s.Members.Count == 0)
            {
                l.Label(Muted(KmhText.Get("KMH_Fed_NoMembers", "No member servers to show.")));
                return;
            }
            if (!FederationGate.LeaderActionsOffered(s) && FederationCache.IsLiveLink(s))
            {
                l.Label(Muted(Reason(FederationText.Why(s.LeadWhyCode, s.LeadWhy), KmhText.Get("KMH_Fed_LeadOnly", "Only the guild Owner on the leader server can remove member servers or hand over leadership."))));
                l.Gap(4f);
            }

            for (int i = 0; i < s.Members.Count; i++)
            {
                FederationMember m = s.Members[i];
                Rect row = l.GetRect(MemberRowH);
                if (i % 2 == 0) Widgets.DrawAltRect(row);
                bool act = FederationGate.MayTarget(s, m);
                string leadLabel = KmhText.Get("KMH_Fed_MakeLeader", "Make leader"), removeLabel = KmhText.Get("KMH_Fed_Remove", "Remove");
                // Measured: "Zum Anführer machen" does not fit the 112px that suits "Make leader".
                float leadW = DialogLayout.ButtonW(leadLabel, 112f, row.width * 0.3f), removeW = DialogLayout.ButtonW(removeLabel, 110f, row.width * 0.25f);
                float buttons = act ? leadW + removeW + 14f : 0f;
                Rect first = new Rect(row.x + 6f, row.y + 3f, Mathf.Max(1f, row.width - buttons - 12f), 22f);

                string marks = (m.IsLeader ? "  " + Good(KmhText.Get("KMH_Fed_LeaderMark", "leader")) : "")
                             + (m.IsSelf ? "  " + Muted(KmhText.Get("KMH_Fed_SelfMark", "this server")) : "");
                DialogLayout.LabelTrunc(first, "<b>" + Or(m.ServerName, m.ServerId) + "</b>  " + m.Guild + marks);

                string second = FederationText.LinkState(m.State);
                if (FederationRules.IsLiveLinkState(m.State))
                    second += " · " + (m.Online ? "● " + KmhText.Get("KMH_Fed_Online", "online") : "○ " + KmhText.Get("KMH_Fed_Offline", "offline"));
                second += " · " + KmhText.Get("KMH_Fed_Joined", "joined {0}", FederationText.When(m.JoinedAt))
                        + " · " + KmhText.Get("KMH_Fed_MemberRenown", "{0} renown, {1} in the last 7 days",
                                              m.RenownTotal.ToString("N0"), m.Renown7d.ToString("N0"));
                DialogLayout.LabelTrunc(new Rect(first.x, row.y + 25f, first.width, 22f), Muted(second));

                if (act)
                {
                    FederationMember target = m;
                    if (Widgets.ButtonText(new Rect(row.xMax - removeW - 10f - leadW, row.y + 11f, leadW, 28f), leadLabel))
                        ConfirmTransfer(s, target);
                    if (Widgets.ButtonText(new Rect(row.xMax - removeW - 6f, row.y + 11f, removeW, 28f), removeLabel))
                        ConfirmRemove(s, target);
                }
                l.Gap(2f);
            }
        }

        private static void ConfirmTransfer(FederationSnapshot s, FederationMember m)
        {
            string linkId = m.LinkId;
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                KmhText.Get("KMH_Fed_TransferConfirm", "Make {0} the leader of {1}? Its guild Owner can then edit the profile, remove members and disband the federation, and this server no longer can.",
                            ServerGuild(m.ServerName, m.Guild), s.Federation.Name),
                () => FederationHandler.TryTransfer(linkId), destructive: true));
        }

        private static void ConfirmRemove(FederationSnapshot s, FederationMember m)
        {
            string linkId = m.LinkId;
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                KmhText.Get("KMH_Fed_RemoveConfirm", "Remove {0} from {1}? It can only come back with a new invite.",
                            ServerGuild(m.ServerName, m.Guild), s.Federation.Name),
                () => FederationHandler.TryRemove(linkId), destructive: true));
        }

        private static void DrawObjectives(Listing_Standard l, FederationSnapshot s)
        {
            if (s.Objectives.Count == 0)
            {
                l.Label(Muted(KmhText.Get("KMH_Fed_NoObjectives", "No objectives for this week yet. They are set the first time anyone looks each week.")));
                return;
            }
            long ends = 0;
            foreach (FederationObjective o in s.Objectives) if (o.EndsAt > ends) ends = o.EndsAt;
            if (ends > 0) l.Label(Muted(KmhText.Get("KMH_Fed_WeekEnds", "This week's objectives end in {0}.", FederationText.TimeLeft(ends))));
            l.Gap(4f);

            foreach (FederationObjective o in s.Objectives)
            {
                Heading(l, FederationText.Category(o.Category) + "  " + Muted(FederationText.ObjectiveState(o.State)));
                DialogLayout.DrawProgress(l.GetRect(14f).ContractedBy(0f, 4f), o.Target > 0 ? Mathf.Clamp01(o.Progress / (float)o.Target) : 0f);
                l.Label(KmhText.Get("KMH_Fed_Progress", "{0} of {1} · your guild: {2}",
                                    o.Progress.ToString("N0"), o.Target.ToString("N0"), o.MyProgress.ToString("N0")));
                l.Label(Muted(KmhText.Get("KMH_Fed_Reward", "Reward: {0} renown for the federation", o.RewardRenown.ToString("N0"))));
                if (o.RewardText.Length > 0) l.Label(Muted(o.RewardText));
                if (o.State == "Completed")
                    l.Label(o.RewardEligible
                        ? Good(o.RewardGranted
                            ? KmhText.Get("KMH_Fed_RewardGranted", "Your guild's share has been put in its vault on this server.")
                            : KmhText.Get("KMH_Fed_RewardPending", "Your guild earned a share. It arrives in the guild vault on this server."))
                        : Muted(KmhText.Get("KMH_Fed_RewardIneligible", "Your guild added nothing to this one, so it earns no share.")));
                l.Gap(6f);
            }
        }

        private static void DrawHistory(Listing_Standard l, FederationSnapshot s)
        {
            if (s.Events.Count == 0)
            {
                l.Label(Muted(KmhText.Get("KMH_Fed_NoEvents", "Nothing has happened yet.")));
                return;
            }
            // Global sends only its newest events, so a short list does not mean a short history.
            l.Label(Muted(KmhText.Get("KMH_Fed_HistoryNote", "Shows the last {0} events at most, newest first.", HistoryShown)));
            foreach (FederationEvent e in s.Events)
            {
                string who = ServerGuild(e.Server, e.Guild);
                string detail = FederationText.EventDetail(e.Kind, e.Detail);
                l.Label(Muted(FederationText.When(e.At)) + "  <b>" + FederationText.EventKind(e.Kind) + "</b>"
                        + (who.Length > 0 ? "  " + who : "") + (detail.Length > 0 ? "  " + Muted(detail) : ""));
            }
        }

        private void DrawManage(Listing_Standard l, FederationSnapshot s)
        {
            Heading(l, KmhText.Get("KMH_Fed_CodeTitle", "Server ID"));
            if (s.ServerId.Length > 0)
            {
                CopyRow(l, s.ServerId);
                l.Label(Muted(KmhText.Get("KMH_Fed_CodeHelp", "To join a federation, give this Server ID to an Admin or the Owner of a guild already in it, so they can invite this server.")));
            }
            else l.Label(Muted(KmhText.Get("KMH_Fed_NoCode", "This server has no Server ID until it has a Colony Nexus Global identity.")));
            l.GapLine(10f);

            // The notice above already says what to do without a guild; may_lead never holds without may_manage.
            if (!s.InGuild) return;
            if (!s.MayManage)
            {
                l.Label(Muted(ManageRefusal(s)));
                return;
            }
            if (!FederationGate.OfficerActionsOffered(s))
            {
                l.Label(Muted(KmhText.Get("KMH_Fed_RefusedWhileDown", "Until it answers, federation changes are refused, not queued: try again later. Your guild works normally.")));
                return;
            }

            if (!FederationCache.IsLiveLink(s))
            {
                DrawCreate(l);
                l.GapLine(10f);
                DrawJoin(l);
                return;
            }

            if (s.Link.State == FederationRules.LinkReproof)
            {
                if (l.ButtonText(KmhText.Get("KMH_Fed_Reprove", "Prove this server's link again"))) FederationHandler.TryReprove();
                l.GapLine(10f);
            }
            DrawInvites(l, s);
            l.GapLine(10f);
            DrawLeave(l, s);
            l.GapLine(10f);
            DrawLeader(l, s);
        }

        private void DrawCreate(Listing_Standard l)
        {
            Heading(l, KmhText.Get("KMH_Fed_CreateTitle", "Start a federation"));
            l.Label(Muted(KmhText.Get("KMH_Fed_CreateHelp", "Your guild becomes its founder and leader. Other guilds join with an invite from you.")));
            _createName = LabeledField(l, KmhText.Get("KMH_Fed_Name", "Name"), "create_name", _createName, FederationRules.NameMax);
            _createTag = LabeledField(l, KmhText.Get("KMH_Fed_Tag", "Tag"), "create_tag", _createTag, FederationRules.TagMax).ToUpperInvariant();
            _createDescription = LabeledArea(l, KmhText.Get("KMH_Fed_Description", "Description"), "create_desc", _createDescription, FederationRules.DescriptionMax);
            _createColor = ColourPicker(l, _createColor);

            string problem = ProfileProblem(_createName, _createTag, _createDescription, _createColor);
            Rect row = l.GetRect(30f);
            if (Widgets.ButtonText(row.LeftPart(0.5f), KmhText.Get("KMH_Fed_Create", "Create the federation"), active: problem == null) && problem == null)
                FederationHandler.TryCreate(_createName, _createTag, _createDescription, _createColor);
            if (problem != null) l.Label(Muted(problem));
        }

        private void DrawJoin(Listing_Standard l)
        {
            Heading(l, KmhText.Get("KMH_Fed_JoinTitle", "Join a federation"));
            l.Label(Muted(KmhText.Get("KMH_Fed_JoinHelp", "Paste the invite an Admin or the Owner of a member guild made for this server. Each invite works once, only for this server, for 30 minutes.")));
            Rect row = l.GetRect(30f);
            _joinCode = Field(new Rect(row.x, row.y, Mathf.Max(1f, row.width - 120f), 28f), "join", _joinCode, 64);
            bool ready = FederationRules.NormalizeInvite(_joinCode).Length > 0;
            if (Widgets.ButtonText(new Rect(row.xMax - 110f, row.y, 110f, 28f), KmhText.Get("KMH_Fed_Join", "Join"), active: ready) && ready)
                FederationHandler.TryJoin(_joinCode);
        }

        private void DrawInvites(Listing_Standard l, FederationSnapshot s)
        {
            Heading(l, KmhText.Get("KMH_Fed_InviteTitle", "Invite a server"));
            l.Label(Muted(KmhText.Get("KMH_Fed_InviteHelp", "Paste the other server's Server ID. The invite works once, only for that server, for 30 minutes.")));
            Rect row = l.GetRect(30f);
            _inviteTarget = Field(new Rect(row.x, row.y, Mathf.Max(1f, row.width - 120f), 28f), "invite", _inviteTarget, 64);
            bool ready = _inviteTarget.Trim().Length > 0;
            if (Widgets.ButtonText(new Rect(row.xMax - 110f, row.y, 110f, 28f), KmhText.Get("KMH_Fed_Invite", "Invite"), active: ready) && ready
                && FederationHandler.TryInvite(_inviteTarget))
                _inviteTarget = "";

            FederationFreshInvite fresh = s.FreshInvite;
            if (fresh.Code.Length > 0)
            {
                l.Gap(4f);
                l.Label(Good(KmhText.Get("KMH_Fed_FreshInvite", "New invite for {0}, valid for {1}:", Or(fresh.TargetServer, "—"), FederationText.TimeLeft(fresh.ExpiresAt))));
                CopyRow(l, fresh.Code);
                l.Label(Muted(KmhText.Get("KMH_Fed_FreshHelp", "Send this code to an Admin or the Owner of that server's guild. It is shown only to you, and only now.")));
            }

            if (s.Invites.Count == 0) return;
            l.Gap(6f);
            l.Label(KmhText.Get("KMH_Fed_Outstanding", "Invites not used yet"));
            foreach (FederationInvite inv in s.Invites)
            {
                Rect r = l.GetRect(28f);
                string who = inv.TargetName.Length > 0 ? inv.TargetName + "  " + Muted(inv.TargetServer) : inv.TargetServer;
                DialogLayout.LabelTrunc(new Rect(r.x, r.y + 3f, Mathf.Max(1f, r.width - 236f), 24f), who);
                DialogLayout.LabelTrunc(new Rect(r.xMax - 230f, r.y + 3f, 112f, 24f), Muted(FederationText.TimeLeft(inv.ExpiresAt)));
                if (Widgets.ButtonText(new Rect(r.xMax - 112f, r.y, 110f, 26f), KmhText.Get("KMH_Fed_Revoke", "Withdraw")))
                    FederationHandler.TryRevokeInvite(inv.InviteId);
            }
        }

        private static void DrawLeave(Listing_Standard l, FederationSnapshot s)
        {
            Heading(l, KmhText.Get("KMH_Fed_LeaveTitle", "Leaving"));
            if (s.Link.IsLeader && s.Federation.ActiveMembers > 1)
            {
                l.Label(Muted(KmhText.Get("KMH_Fed_LeaveLeader", "This server leads the federation, so it hands leadership to another member before it can leave.")));
                return;
            }
            if (s.Federation.ActiveMembers == 1)
            {
                l.Label(Muted(KmhText.Get("KMH_Fed_LeaveLast", "This server is the last member. A federation cannot be left empty, so disband it instead.")));
                return;
            }
            l.Label(Muted(KmhText.Get("KMH_Fed_LeaveHelp", "Your guild stops counting for the federation. Its history stays, and coming back takes a new invite.")));
            if (l.ButtonText(KmhText.Get("KMH_Fed_Leave", "Leave the federation")))
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                    KmhText.Get("KMH_Fed_LeaveConfirm", "Take {0} out of {1}?", s.Guild, s.Federation.Name),
                    () => FederationHandler.TryLeave(), destructive: true));
        }

        private void DrawLeader(Listing_Standard l, FederationSnapshot s)
        {
            Heading(l, KmhText.Get("KMH_Fed_LeaderTitle", "Leader"));
            if (!FederationGate.LeaderActionsOffered(s))
            {
                l.Label(Muted(LeadRefusal(s)));
                return;
            }
            DrawEdit(l, s.Federation);
            l.GapLine(10f);
            DrawTransfer(l, s);
            l.GapLine(10f);
            DrawDisband(l, s);
        }

        private void DrawEdit(Listing_Standard l, FederationProfile f)
        {
            if (_editLoadedFor != f.Id) LoadEdit(f);
            l.Label(KmhText.Get("KMH_Fed_EditTitle", "Edit the profile"));
            _editName = LabeledField(l, KmhText.Get("KMH_Fed_Name", "Name"), "edit_name", _editName, FederationRules.NameMax);
            _editTag = LabeledField(l, KmhText.Get("KMH_Fed_Tag", "Tag"), "edit_tag", _editTag, FederationRules.TagMax).ToUpperInvariant();
            _editDescription = LabeledArea(l, KmhText.Get("KMH_Fed_Description", "Description"), "edit_desc", _editDescription, FederationRules.DescriptionMax);
            _editColor = ColourPicker(l, _editColor);

            string problem = ProfileProblem(_editName, _editTag, _editDescription, _editColor);
            bool changed = _editName != f.Name || _editTag != f.Tag || _editDescription != f.Description
                        || !string.Equals(_editColor, f.Color, StringComparison.OrdinalIgnoreCase);
            Rect row = l.GetRect(30f);
            bool ready = problem == null && changed;
            if (Widgets.ButtonText(row.LeftPart(0.48f), KmhText.Get("KMH_Fed_Save", "Save the profile"), active: ready) && ready)
                FederationHandler.TryUpdate(_editName, _editTag, _editDescription, _editColor);
            if (Widgets.ButtonText(row.RightPart(0.48f), KmhText.Get("KMH_Fed_UndoEdit", "Undo my changes"), active: changed) && changed)
                LoadEdit(f);
            if (problem != null) l.Label(Muted(problem));
        }

        private void LoadEdit(FederationProfile f)
        {
            _editName = f.Name;
            _editTag = f.Tag;
            _editDescription = f.Description;
            _editColor = f.Color;
            _editLoadedFor = f.Id;
        }

        private static void DrawTransfer(Listing_Standard l, FederationSnapshot s)
        {
            l.Label(KmhText.Get("KMH_Fed_TransferTitle", "Hand over leadership"));
            bool any = false;
            foreach (FederationMember m in s.Members) if (FederationGate.MayTarget(s, m)) { any = true; break; }
            if (!any)
            {
                l.Label(Muted(KmhText.Get("KMH_Fed_NoOneToLead", "There is no other member server to hand leadership to.")));
                return;
            }
            if (!l.ButtonText(KmhText.Get("KMH_Fed_ChooseLeader", "Choose the new leader…"))) return;
            var options = new List<FloatMenuOption>();
            foreach (FederationMember m in s.Members)
            {
                if (!FederationGate.MayTarget(s, m)) continue;
                FederationMember target = m;
                options.Add(new FloatMenuOption(ServerGuild(m.ServerName, m.Guild), () => ConfirmTransfer(s, target)));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void DrawDisband(Listing_Standard l, FederationSnapshot s)
        {
            string tag = s.Federation.Tag;
            l.Label(Warn(KmhText.Get("KMH_Fed_DisbandTitle", "Disband the federation")));
            l.Label(Muted(KmhText.Get("KMH_Fed_DisbandHelp", "This is permanent: every member is let go, renown and level freeze, and the federation can never become active again. Its history is kept.")));
            _disbandConfirm = LabeledField(l, KmhText.Get("KMH_Fed_DisbandTypeLabel", "Type {0} to confirm", tag), "disband_confirm", _disbandConfirm, FederationCache.MaxTag);
            _disbandReason = LabeledField(l, KmhText.Get("KMH_Fed_DisbandReason", "Reason (optional)"), "disband_reason", _disbandReason, FederationRules.ReasonMax);

            bool confirmed = FederationRules.DisbandConfirmed(_disbandConfirm, tag);
            Rect row = l.GetRect(30f);
            if (Widgets.ButtonText(row.LeftPart(0.5f), KmhText.Get("KMH_Fed_Disband", "Disband it"), active: confirmed) && confirmed)
            {
                string typed = _disbandConfirm, reason = _disbandReason;
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                    KmhText.Get("KMH_Fed_DisbandConfirm", "Disband {0} for good? This cannot be undone.", s.Federation.Name),
                    () =>
                    {
                        if (!FederationHandler.TryDisband(typed, reason)) return;
                        _disbandConfirm = "";
                        _disbandReason = "";
                    }, destructive: true));
            }
            if (!confirmed) l.Label(Muted(KmhText.Get("KMH_Fed_DisbandNeedsTag", "The button works once the tag is typed exactly as shown, capitals included.")));
        }

        // Null when the form would be accepted; otherwise the first thing to fix, in the same words the handler uses.
        private static string ProfileProblem(string name, string tag, string description, string color)
        {
            string n = FederationRules.NormalizeName(name), t = FederationRules.NormalizeTag(tag);
            if (n.Length == 0 || t.Length == 0) return KmhText.Get("KMH_Fed_FillIn", "Give it a name and a tag.");
            if (!FederationRules.ValidName(n)) return KmhText.Get("KMH_Fed_BadName", "A federation name is 3 to 32 characters, without < or >.");
            if (!FederationRules.ValidTag(t)) return KmhText.Get("KMH_Fed_BadTag", "A tag is 2 to 5 letters or digits.");
            if (!FederationRules.ValidDescription(FederationRules.NormalizeDescription(description)))
                return KmhText.Get("KMH_Fed_BadDescription", "A description is at most 280 characters, without < or >.");
            if (!FederationRules.ValidColor(color)) return KmhText.Get("KMH_Fed_BadColor", "Pick one of the offered colours, or none.");
            return null;
        }

        private static string Field(Rect r, string name, string value, int max)
        {
            GUI.SetNextControlName(FieldPrefix + name);
            string v = Widgets.TextField(r, value ?? "");
            return v.Length > max ? v.Substring(0, max) : v;
        }

        private static string LabeledField(Listing_Standard l, string label, string name, string value, int max)
        {
            Rect row = l.GetRect(30f);
            DialogLayout.LabelTrunc(new Rect(row.x, row.y + 3f, LabelW, 24f), label);
            return Field(new Rect(row.x + LabelW + 6f, row.y, Mathf.Max(1f, row.width - LabelW - 6f), 28f), name, value, max);
        }

        private static string LabeledArea(Listing_Standard l, string label, string name, string value, int max)
        {
            Rect row = l.GetRect(66f);
            DialogLayout.LabelTrunc(new Rect(row.x, row.y + 3f, LabelW, 24f), label);
            GUI.SetNextControlName(FieldPrefix + name);
            string v = Widgets.TextArea(new Rect(row.x + LabelW + 6f, row.y, Mathf.Max(1f, row.width - LabelW - 6f), 64f), value ?? "");
            return v.Length > max ? v.Substring(0, max) : v;
        }

        private static string ColourPicker(Listing_Standard l, string current)
        {
            const float size = 22f, pitch = 26f;
            Rect row = l.GetRect(30f);
            DialogLayout.LabelTrunc(new Rect(row.x, row.y + 3f, LabelW, 24f), KmhText.Get("KMH_Fed_Colour", "Colour"));
            string picked = current ?? "";
            float x = row.x + LabelW + 6f;

            Rect none = new Rect(x, row.y + 4f, size, size);
            Widgets.DrawBoxSolid(none, new Color(0.16f, 0.16f, 0.16f));
            Widgets.Label(none, "  -");
            if (picked.Length == 0) Outline(none);
            if (Widgets.ButtonInvisible(none)) picked = "";
            TooltipHandler.TipRegion(none, KmhText.Get("KMH_Fed_NoColour", "No colour"));

            foreach (string hex in FederationRules.Palette)
            {
                x += pitch;
                if (x + size > row.xMax) break;
                Rect sw = new Rect(x, row.y + 4f, size, size);
                if (ColorUtility.TryParseHtmlString(hex, out Color c)) Widgets.DrawBoxSolid(sw, c);
                if (string.Equals(hex, picked, StringComparison.OrdinalIgnoreCase)) Outline(sw);
                if (Widgets.ButtonInvisible(sw)) picked = hex;
                TooltipHandler.TipRegion(sw, hex);
            }
            return picked;
        }

        private static void Outline(Rect r)
        {
            Color old = GUI.color;
            GUI.color = Color.white;
            Widgets.DrawBox(r.ExpandedBy(2f), 2);
            GUI.color = old;
        }

        private static void CopyRow(Listing_Standard l, string value)
        {
            Rect row = l.GetRect(30f);
            DialogLayout.LabelTrunc(new Rect(row.x, row.y + 4f, Mathf.Max(1f, row.width - 120f), 24f), "<b>" + value + "</b>");
            if (!Widgets.ButtonText(new Rect(row.xMax - 110f, row.y, 110f, 28f), KmhText.Get("KMH_Fed_Copy", "Copy"))) return;
            try { GUIUtility.systemCopyBuffer = value; }
            catch (Exception) { }
            Messages.Message(KmhText.Get("KMH_Fed_Copied", "Copied to the clipboard."), MessageTypeDefOf.TaskCompletion, false);
        }

        private static void Pair(Listing_Standard l, string label, string value)
        {
            Rect row = l.GetRect(DialogLayout.TextRowH);
            DialogLayout.LabelTrunc(new Rect(row.x, row.y, LabelW, row.height), Muted(label));
            DialogLayout.LabelTrunc(new Rect(row.x + LabelW + 6f, row.y, Mathf.Max(1f, row.width - LabelW - 6f), row.height), value);
        }

        private static void Heading(Listing_Standard l, string text) => l.Label("<b>" + text + "</b>");

        // Every argument is already inert: the cache made it so on the way in.
        private static string ServerGuild(string server, string guild)
        {
            if (server.Length == 0) return guild;
            return guild.Length == 0 ? server : server + " · " + guild;
        }

        private static string Or(string value, string fallback) => string.IsNullOrEmpty(value) ? fallback : value;

        private static string Reason(string given, string fallback) => string.IsNullOrEmpty(given) ? fallback : given;

        private static string ManageRefusal(FederationSnapshot s)
            => Reason(FederationText.Why(s.ManageWhyCode, s.ManageWhy),
                      KmhText.Get("KMH_Fed_ManageOnly", "Only a guild Admin or the Owner, verified with Colony Nexus Global, can manage the guild's federation."));

        private static string LeadRefusal(FederationSnapshot s)
            => Reason(FederationText.Why(s.LeadWhyCode, s.LeadWhy),
                      KmhText.Get("KMH_Fed_LeaderOnly", "Only the guild Owner on the leader server can edit the profile, hand over leadership or disband the federation."));

        // This game knows what Global said and what address it dialled, which the server cannot; its words win.
        internal static string UnverifiedWhy(FederationSnapshot s, string clientProblem)
            => !string.IsNullOrEmpty(clientProblem) ? clientProblem : FederationText.Why(s?.VerifiedWhyCode, s?.VerifiedWhy);

        private static string Muted(string s) => "<color=#9AA4B2>" + s + "</color>";
        private static string Warn(string s)  => "<color=#f0c04f>" + s + "</color>";
        private static string Good(string s)  => "<color=#4cc38a>" + s + "</color>";
    }
}
