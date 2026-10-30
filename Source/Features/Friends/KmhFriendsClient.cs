using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using KMHPatch.Diagnostics;
using KMHPatch.Features.Account;
using KMHPatch.Features.Global;
using Newtonsoft.Json.Linq;

namespace KMHPatch.Features.Friends
{
    internal enum FriendsState { Unknown, Loading, Loaded, NotLinked, Unavailable }

    internal sealed class FriendDto
    {
        public string PlayerGlobalId = "";
        public string DisplayName = "";
        public string Username = "";
        public string State = "";
        public string Direction = "";
        public bool   Online;
        public string ServerId = "";
        public long?  LastSeenUnix;

        // Another player chose this name, and it is drawn with Widgets.Label.
        public string Shown => UI.KmhDisplayText.Inert(
            string.IsNullOrEmpty(Username) ? (string.IsNullOrEmpty(DisplayName) ? "(unnamed)" : DisplayName) : Username, 48);
    }

    // Each call may fail without touching the game; nothing is cached to disk, a stale list isn't worth keeping.
    internal static class KmhFriendsClient
    {
        public static FriendsState State { get; private set; } = FriendsState.Unknown;
        public static string ErrorCode { get; private set; } = "";
        // The last action Global refused, kept apart from State: one refused request does not mean Friends is down.
        public static string ActionError { get; private set; } = "";

        public static List<FriendDto> Friends { get; private set; } = new List<FriendDto>();
        public static List<FriendDto> Incoming { get; private set; } = new List<FriendDto>();
        public static List<FriendDto> Outgoing { get; private set; } = new List<FriendDto>();
        public static List<FriendDto> Blocked { get; private set; } = new List<FriendDto>();

        private static readonly TimeSpan RefreshEvery = TimeSpan.FromSeconds(30);
        private static readonly Global.KmhPollGate _gate = new Global.KmhPollGate(RefreshEvery);

        public static bool Busy => _gate.Busy;
        public static DateTime LastRefreshUtc => _gate.LastSuccessUtc;

        private static string Directory => GlobalHttp.BaseUrl;
        private static string Token => KmhAccountStore.TokenFor(Directory);

        // Every answer is the whole list, from two callers: an answer to an older request must not replace a newer one.
        private static readonly object _applyLock = new object();
        private static long _issued, _applied, _floor;
        private static HashSet<string> _seenIncoming = new HashSet<string>(StringComparer.Ordinal);
        private static bool _seenBaseline;

        // Swapped by the self-test.
        internal static Action<List<FriendDto>> Announce = AnnounceToPlayer;

        static KmhFriendsClient() => KmhIdentityTeardown.Register("friends", Reset);

        public static void Reset()
        {
            State = FriendsState.Unknown; ErrorCode = ""; ActionError = "";
            AddState = AddStage.Idle; AddMessage = ""; MyCode = null; Offline = false;
            Friends = new List<FriendDto>(); Incoming = new List<FriendDto>();
            Outgoing = new List<FriendDto>(); Blocked = new List<FriendDto>();
            _gate.Reset();
            lock (_applyLock)
            {
                // Anything already in flight answers for the identity that just went away.
                _floor = Interlocked.Read(ref _issued);
                _applied = _floor;
                _seenIncoming = new HashSet<string>(StringComparer.Ordinal);
                _seenBaseline = false;
            }
        }

        public static void ClearActionError() => ActionError = "";

        // Taken when a request leaves, so its answer can be ordered against the others.
        internal static long NextRead() => Interlocked.Increment(ref _issued);

        public static void RefreshIfDue()
        {
            if (_gate.Due(DateTime.UtcNow)) Refresh();
        }

        public static void Refresh() => Call("GET", "/api/v1/friends", null, null);

        // Quick Add's own outcome, so the box says "sent" only once Global has said so.
        internal enum AddStage { Idle, Sending, Sent, Refused, Failed }
        public static AddStage AddState { get; private set; } = AddStage.Idle;
        public static string AddMessage { get; private set; } = "";
        public static void ClearAdd() { if (AddState != AddStage.Sending) { AddState = AddStage.Idle; AddMessage = ""; } }

        // A player ID goes as one, which every Global understands; a friend code or username goes as "target".
        public static bool QuickAdd(string typed)
        {
            string raw = (typed ?? "").Trim();
            if (raw.Length == 0) return false;
            string id = NormalizeId(raw);
            JObject body = id.Length > 0 ? new JObject { ["player_global_id"] = id } : new JObject { ["target"] = raw.TrimStart('@') };
            if (!Call("POST", "/api/v1/friends/request", body, raw, quick: true)) return false;
            AddState = AddStage.Sending;
            AddMessage = UI.KmhText.Get("KMH_Friends_Add_Sending", "Sending your request…");
            return true;
        }

        // This player's shareable code, fetched once; null until Global answers, "" where Global has none.
        public static string MyCode { get; private set; }
        private static int _codeAsking;

        public static void FetchCode(bool regenerate = false)
        {
            if (Token.Length == 0 || Interlocked.Exchange(ref _codeAsking, 1) == 1) return;
            string token = Token;
            Task.Run(async () =>
            {
                try
                {
                    GlobalHttp.Result r = regenerate
                        ? await GlobalHttp.PostAsync("/api/v1/friends/code/regenerate", "{}", token, CancellationToken.None).ConfigureAwait(false)
                        : await GlobalHttp.GetAsync("/api/v1/friends/code", token, CancellationToken.None).ConfigureAwait(false);
                    if (r.ErrorCode == GlobalHttp.IdentityChangedCode) return;
                    if (r.Ok) MyCode = (string)r.Body?["code"] ?? "";
                    // A Global without codes answers 404; anything else leaves the old value to try again later.
                    else if (r.Status == 404 || r.Status == 405) MyCode = "";
                    else if (regenerate) Say(() => RefusalText(r.ErrorCode.Length > 0 ? r.ErrorCode : "http." + r.Status), positive: false);
                }
                catch (Exception ex) { KmhLog.Debug("Friends: friend code failed: " + ex.Message); }
                finally { Interlocked.Exchange(ref _codeAsking, 0); }
            });
        }

        internal static string FormatCode(string code)
        {
            string c = (code ?? "").Replace("-", "").Replace(" ", "").ToUpperInvariant();
            return c.Length == 8 ? c.Substring(0, 4) + "-" + c.Substring(4) : c;
        }

        public static bool Request(string playerGlobalId, string shown = null) => Act("/api/v1/friends/request", playerGlobalId, shown);
        public static bool Accept(string playerGlobalId, string shown = null)  => Act("/api/v1/friends/accept", playerGlobalId, shown);
        // Decline, cancel and remove are one call: the pair stops existing.
        public static bool Remove(string playerGlobalId)  => Act("/api/v1/friends/remove", playerGlobalId, null);
        public static bool Block(string playerGlobalId, string shown = null) => Act("/api/v1/friends/block", playerGlobalId, shown);
        public static bool Unblock(string playerGlobalId) => Act("/api/v1/friends/unblock", playerGlobalId, null);

        // What a pasted id becomes, or "" when it is not one: Global's own rule, 32 hex characters in any case.
        internal static string NormalizeId(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            var sb = new System.Text.StringBuilder(32);
            foreach (char c in raw)
            {
                if (char.IsWhiteSpace(c)) continue;
                char l = char.ToLowerInvariant(c);
                if (!((l >= '0' && l <= '9') || (l >= 'a' && l <= 'f'))) return "";
                sb.Append(l);
            }
            return sb.Length == 32 ? sb.ToString() : "";
        }

        public static string FriendshipWith(string playerGlobalId)
        {
            if (string.IsNullOrEmpty(playerGlobalId)) return "";
            if (Contains(Friends, playerGlobalId)) return "friend";
            if (Contains(Incoming, playerGlobalId)) return "incoming";
            if (Contains(Outgoing, playerGlobalId)) return "outgoing";
            return "";
        }

        public static bool IsBlocked(string playerGlobalId) => Contains(Blocked, playerGlobalId);

        private static bool Contains(List<FriendDto> rows, string id)
        {
            foreach (FriendDto f in rows) if (string.Equals(f.PlayerGlobalId, id, StringComparison.Ordinal)) return true;
            return false;
        }

        private static bool Act(string path, string playerGlobalId, string shown)
        {
            string id = NormalizeId(playerGlobalId);
            if (id.Length == 0) { ActionError = "KMH-FRD-752"; return false; }
            return Call("POST", path, new JObject { ["player_global_id"] = id }, shown);
        }

        private static bool Call(string method, string path, JObject body, string shown, bool quick = false)
        {
            if (!_gate.TryEnter()) return false;
            if (Token.Length == 0) { State = FriendsState.NotLinked; _gate.Exit(); return false; }
            // A list already on screen stays there while it refreshes; only a first load shows "asking".
            if (State != FriendsState.Loaded) State = FriendsState.Loading;
            long seq = NextRead();
            string target = (string)body?["player_global_id"] ?? "";

            Task.Run(async () =>
            {
                try
                {
                    GlobalHttp.Result r = method == "GET"
                        ? await GlobalHttp.GetAsync(path, Token, CancellationToken.None).ConfigureAwait(false)
                        : await GlobalHttp.PostAsync(path, body?.ToString(Newtonsoft.Json.Formatting.None),
                                                     Token, CancellationToken.None).ConfigureAwait(false);
                    Apply(r, seq, method != "GET", path, target, shown, quick);
                }
                catch (Exception ex)
                {
                    KmhLog.Debug($"Friends: {path} failed: {ex.Message}");
                    if (quick) AddDone(AddStage.Failed, UnconfirmedText());
                    else if (State != FriendsState.Loaded) State = FriendsState.Unavailable;
                    else Offline = true;
                }
                finally { _gate.Exit(); }
            });
            return true;
        }

        public static bool Offline { get; private set; }

        private static void AddDone(AddStage stage, string message) { AddState = stage; AddMessage = message ?? ""; }

        // Sent but unanswered: it may or may not have arrived, and saying either would be a guess.
        private static string UnconfirmedText()
            => UI.KmhText.Get("KMH_Friends_Add_Unconfirmed",
                "Colony Nexus Global did not answer, so the request is not confirmed. If it is not under Sent, try again.");

        // Every call answers with the whole list, so a client that missed a reply converges on the next one.
        private static void Apply(GlobalHttp.Result r, long seq, bool action, string path, string target, string shown, bool quick = false)
        {
            // Signed out or switched account mid-call: the answer is about nobody here any more.
            if (r.ErrorCode == GlobalHttp.IdentityChangedCode) { if (quick) AddDone(AddStage.Idle, ""); return; }
            if (!r.Ok || r.Body == null)
            {
                // Global answered and said no to this one action: Friends itself is still up.
                if (action && r.Status >= 400 && r.Status < 500 && r.Status != 401)
                {
                    string code = r.ErrorCode.Length > 0 ? r.ErrorCode : "http." + r.Status;
                    ActionError = code;
                    // "No player has that ID" is wrong for a typed code or username, which is what an empty id means here.
                    if (quick) AddDone(AddStage.Refused, target.Length == 0 && code == "KMH-FRD-752"
                        ? UI.KmhText.Get("KMH_Friends_Err_NoMatch", "No Colony Nexus player matches that friend code or username. Check it with your friend.")
                        : RefusalText(code));
                    else if (shown != null) Say(() => RefusalText(code), positive: false);
                    return;
                }
                // A button the player pressed gets an answer even when Global failed: silence reads as "it worked".
                if (action && !quick && r.Status != 401)
                    Say(() => UI.KmhText.Get("KMH_Friends_ActionFailed", "Colony Nexus Global did not confirm that. If the list has not changed, try again in a moment."), positive: false);
                if (quick) AddDone(r.Status == 401 ? AddStage.Refused : AddStage.Failed,
                                   r.Status == 401 ? UI.KmhText.Get("KMH_Friends_Add_SignedOut", "Your Colony Nexus sign-in has ended. Link again under Account & links.")
                                                   : UnconfirmedText());
                // 401 is the one answer that means the link is gone rather than that Global is unreachable.
                if (r.Status == 401) State = FriendsState.NotLinked;
                else if (State != FriendsState.Loaded) State = FriendsState.Unavailable;
                else Offline = true;
                ErrorCode = r.ErrorCode;
                _gate.Failed();
                return;
            }

            Offline = false;
            ApplyLists(r.Body, seq);
            if (action) ActionError = "";
            _gate.Succeeded();
            if (quick)
            {
                // Global names whom a code or username reached; an older Global only answers id requests.
                string id = (string)r.Body["resolved"]?["player_global_id"] ?? target;
                string who = (string)r.Body["resolved"]?["shown"];
                if (string.IsNullOrEmpty(who)) who = NameFor(id) ?? shown;
                who = UI.KmhDisplayText.Inert(who ?? "", 48);
                AddDone(AddStage.Sent, FriendshipWith(id) == "friend"
                    ? UI.KmhText.Get("KMH_Friends_NowFriends", "You and {0} are now friends.", who)
                    : UI.KmhText.Get("KMH_Friends_RequestSent", "Friend request sent to {0}.", who));
                return;
            }
            if (action && path.EndsWith("/block", StringComparison.Ordinal)) Features.Chat.GlobalChatBridge.DropSender(target);
            if (action && shown != null) Confirm(path, target, shown);
        }

        private static string NameFor(string id)
        {
            foreach (List<FriendDto> rows in new[] { Friends, Outgoing, Incoming })
                foreach (FriendDto f in rows) if (string.Equals(f.PlayerGlobalId, id, StringComparison.Ordinal)) return f.Shown;
            return null;
        }

        // The presence heartbeat answers with the same lists, so badges stay current without a call of their own.
        internal static void ApplyPresence(JObject body, long seq)
        {
            if (body == null) return;
            ApplyLists(body, seq);
        }

        private static void ApplyLists(JObject body, long seq)
        {
            List<FriendDto> fresh, accepted;
            lock (_applyLock)
            {
                if (seq <= _floor || seq < _applied) return;
                _applied = seq;
                bool baseline = _seenBaseline;
                List<FriendDto> askedBefore = Outgoing;
                Friends  = Parse(body["friends"]);
                Incoming = Parse(body["incoming"]);
                Outgoing = Parse(body["outgoing"]);
                Blocked  = Parse(body["blocked"]);
                ErrorCode = "";
                State = FriendsState.Loaded;
                fresh = NoteIncoming(Incoming);
                accepted = baseline ? NowFriends(askedBefore, Friends) : new List<FriendDto>();
            }
            if (fresh.Count > 0) Announce?.Invoke(fresh);
            if (accepted.Count > 0) AnnounceAccepted?.Invoke(accepted);
        }

        internal static List<FriendDto> NowFriends(List<FriendDto> askedBefore, List<FriendDto> friendsNow)
        {
            var asked = new HashSet<string>(StringComparer.Ordinal);
            foreach (FriendDto f in askedBefore ?? new List<FriendDto>()) asked.Add(f.PlayerGlobalId);
            var yes = new List<FriendDto>();
            foreach (FriendDto f in friendsNow ?? new List<FriendDto>()) if (asked.Contains(f.PlayerGlobalId)) yes.Add(f);
            return yes;
        }

        internal static Action<List<FriendDto>> AnnounceAccepted = accepted =>
        {
            string who = accepted[0].Shown;
            KmhMainThread.Post(() => Notifications.KmhNotifications.Positive(accepted.Count == 1
                ? UI.KmhText.Get("KMH_Friends_Accepted", "{0} accepted your friend request.", who)
                : UI.KmhText.Get("KMH_Friends_AcceptedMany", "{0} people accepted your friend requests.", accepted.Count)));
        };

        // The first answer only sets the baseline; later, an id missing from the previous answer is new.
        private static List<FriendDto> NoteIncoming(List<FriendDto> incoming)
        {
            var fresh = new List<FriendDto>();
            var now = new HashSet<string>(StringComparer.Ordinal);
            foreach (FriendDto f in incoming)
            {
                now.Add(f.PlayerGlobalId);
                if (_seenBaseline && !_seenIncoming.Contains(f.PlayerGlobalId)) fresh.Add(f);
            }
            _seenIncoming = now;
            _seenBaseline = true;
            return fresh;
        }

        private static void AnnounceToPlayer(List<FriendDto> fresh)
        {
            string who = fresh[0].Shown;
            int n = fresh.Count;
            KmhMainThread.Post(() => Notifications.KmhNotifications.Positive(n == 1
                ? UI.KmhText.Get("KMH_Friends_NewRequest", "{0} sent you a friend request. Answer it under Friends.", who)
                : UI.KmhText.Get("KMH_Friends_NewRequests", "{0} new friend requests. Answer them under Friends.", n)));
        }

        private static void Confirm(string path, string target, string shown)
        {
            // Asking someone who had already asked you is an accept, so the answer decides which sentence is true.
            bool friends = FriendshipWith(target) == "friend";
            if (path.EndsWith("/request", StringComparison.Ordinal) || path.EndsWith("/accept", StringComparison.Ordinal))
                Say(() => friends
                    ? UI.KmhText.Get("KMH_Friends_NowFriends", "You and {0} are now friends.", shown)
                    : UI.KmhText.Get("KMH_Friends_RequestSent", "Friend request sent to {0}.", shown), positive: true);
            else if (path.EndsWith("/block", StringComparison.Ordinal))
                Say(() => UI.KmhText.Get("KMH_Friends_BlockedNow", "{0} is blocked on every Colony Nexus server.", shown), positive: true);
        }

        // Composed and shown on the main thread: the answer arrives on a pool thread.
        private static void Say(Func<string> text, bool positive)
            => KmhMainThread.Post(() =>
            {
                string t = text();
                if (positive) Notifications.KmhNotifications.Positive(t);
                else Notifications.KmhNotifications.Rejected(t);
            });

        // A block reads the same from both sides, as Global intends.
        internal static string RefusalText(string code)
        {
            switch (code)
            {
                case "KMH-FRD-751": return UI.KmhText.Get("KMH_Friends_Err_Blocked", "You cannot add this player.");
                case "KMH-FRD-752": return UI.KmhText.Get("KMH_Friends_Err_NoSuchId", "No Colony Nexus player has that ID.");
                case "KMH-FRD-753": return UI.KmhText.Get("KMH_Friends_Err_Self", "That is your own player ID.");
                case "KMH-FRD-754": return UI.KmhText.Get("KMH_Friends_Err_Closed", "This player is not accepting friend requests.");
                case "KMH-FRD-755": return UI.KmhText.Get("KMH_Friends_Err_Gone", "That request no longer exists.");
                case "KMH-FRD-757":
                case "http.429":    return UI.KmhText.Get("KMH_Friends_Err_Limit", "Too many friend actions for now. Try again later.");
                case "KMH-FRD-758": return UI.KmhText.Get("KMH_Friends_Err_NoMatch", "No Colony Nexus player matches that friend code or username. Check it with your friend.");
                case "KMH-FRD-759": return UI.KmhText.Get("KMH_Friends_Add_Empty", "Type or paste a friend code, player ID or username first.");
                case "KMH-FRD-760": return UI.KmhText.Get("KMH_Friends_Err_Misses", "Too many tries matched nobody. Wait a few minutes, then check the code with your friend.");
                case "KMH-FRD-761": return UI.KmhText.Get("KMH_Friends_Err_RenewLimit", "You can get a new friend code three times a day. Try again tomorrow.");
                default:            return UI.KmhText.Get("KMH_Friends_Err_Other", "Colony Nexus Global refused that ({0}).", code ?? "");
            }
        }

        internal static List<FriendDto> Parse(JToken token)
        {
            var list = new List<FriendDto>();
            if (!(token is JArray arr)) return list;
            foreach (JToken t in arr)
            {
                if (!(t is JObject o)) continue;
                string id = (string)o["player_global_id"];
                if (string.IsNullOrEmpty(id)) continue;
                list.Add(new FriendDto
                {
                    PlayerGlobalId = id,
                    DisplayName    = (string)o["display_name"] ?? "",
                    Username       = (string)o["username"] ?? "",
                    State          = (string)o["state"] ?? "",
                    Direction      = (string)o["direction"] ?? "",
                    Online         = (bool?)o["online"] ?? false,
                    // Absent means the friend's privacy did not allow it. It is a directory id, never an address.
                    ServerId       = (string)o["server_id"] ?? "",
                    LastSeenUnix   = (long?)o["last_seen"],
                });
            }
            return list;
        }

        // Null is normal: not every friend is on a server this client has listed.
        public static ServerCardDto CardFor(FriendDto friend)
            => friend == null ? null : KmhDirectoryClient.CardById(friend.ServerId);

        // Global sends a server id only for a listed server the friend allows shown, and omits blocked players.
        public static List<FriendDto> OnServers()
        {
            var on = new List<FriendDto>();
            foreach (FriendDto f in Friends) if (f.Online && !string.IsNullOrEmpty(f.ServerId)) on.Add(f);
            return on;
        }

        internal static void SetFriendsForTest(List<FriendDto> friends) => Friends = friends ?? new List<FriendDto>();

        // Everything the suite changes, so a run in game puts the player's real lists back.
        internal sealed class Saved
        {
            internal FriendsState State; internal string ErrorCode, ActionError;
            internal List<FriendDto> Friends, Incoming, Outgoing, Blocked;
            internal long Applied, Floor; internal HashSet<string> Seen; internal bool Baseline;
            internal Action<List<FriendDto>> Announce;
        }

        internal static Saved SaveForTest()
        {
            lock (_applyLock)
                return new Saved
                {
                    State = State, ErrorCode = ErrorCode, ActionError = ActionError,
                    Friends = Friends, Incoming = Incoming, Outgoing = Outgoing, Blocked = Blocked,
                    Applied = _applied, Floor = _floor, Seen = _seenIncoming, Baseline = _seenBaseline, Announce = Announce,
                };
        }

        internal static void RestoreForTest(Saved s)
        {
            if (s == null) return;
            lock (_applyLock)
            {
                State = s.State; ErrorCode = s.ErrorCode; ActionError = s.ActionError;
                Friends = s.Friends; Incoming = s.Incoming; Outgoing = s.Outgoing; Blocked = s.Blocked;
                _applied = s.Applied; _floor = s.Floor; _seenIncoming = s.Seen; _seenBaseline = s.Baseline;
                Announce = s.Announce;
            }
        }

        // A clean slate for the lists and the ordering, leaving the gate and any call in flight alone.
        internal static void ForgetForTest()
        {
            lock (_applyLock)
            {
                Friends = new List<FriendDto>(); Incoming = new List<FriendDto>();
                Outgoing = new List<FriendDto>(); Blocked = new List<FriendDto>();
                _floor = Interlocked.Read(ref _issued);
                _applied = _floor;
                _seenIncoming = new HashSet<string>(StringComparer.Ordinal);
                _seenBaseline = false;
            }
        }

        public static int OnlineWithoutServer()
        {
            int n = 0;
            foreach (FriendDto f in Friends) if (f.Online && string.IsNullOrEmpty(f.ServerId)) n++;
            return n;
        }
    }
}
