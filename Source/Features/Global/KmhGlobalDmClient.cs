using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using KMHPatch.Diagnostics;
using KMHPatch.Features.Account;
using Newtonsoft.Json.Linq;

namespace KMHPatch.Features.Global
{
    internal enum GlobalDmState { Unknown, Loading, Loaded, NotLinked, Unavailable }

    internal sealed class GlobalDmConversation
    {
        public string PlayerGlobalId = "";
        public string DisplayName = "";
        public string Username = "";
        public int    Unread;
        public long?  LastMessageAtUnix;
        public bool   MayReply = true;

        public string Shown => UI.KmhDisplayText.Inert(
            string.IsNullOrEmpty(Username) ? (string.IsNullOrEmpty(DisplayName) ? "(unnamed)" : DisplayName) : Username, 48);
    }

    internal sealed class GlobalDmMessage
    {
        public string MessageId = "";
        public string From = "";
        public string Body = "";
        public long   CreatedAtUnix;
        public bool   Mine;

        // Another player's text, drawn with Widgets.Label.
        public string Shown => UI.KmhDisplayText.Inert(Body ?? "", 600);
    }

    internal static class KmhGlobalDmClient
    {
        public static GlobalDmState State { get; private set; } = GlobalDmState.Unknown;
        public static string ErrorCode { get; private set; } = "";

        public static List<GlobalDmConversation> Conversations { get; private set; } = new List<GlobalDmConversation>();
        public static int UnreadTotal { get; private set; }

        // Every access to all four maps: poll, click, teardown and OnGUI race, and a racing Dictionary can spin forever.
        private static readonly object _mapsLock = new object();
        private static readonly Dictionary<string, List<GlobalDmMessage>> _history =
            new Dictionary<string, List<GlobalDmMessage>>(StringComparer.Ordinal);
        private static readonly Dictionary<string, string> _cursors = new Dictionary<string, string>(StringComparer.Ordinal);
        private static readonly Dictionary<string, bool> _mayReply = new Dictionary<string, bool>(StringComparer.Ordinal);
        // Sent back when marking read so "read" means what was shown, not when the request arrived.
        private static readonly Dictionary<string, string> _readThrough = new Dictionary<string, string>(StringComparer.Ordinal);

        // The open conversation, so the poll keeps it live; without it the pane went stale and the badge never cleared.
        private static string _openPeer = "";

        // Two slots: the sidebar poll runs before the pane in the same OnGUI pass and would starve the player's click.
        private static int _inFlight;
        private static int _pollInFlight;
        public static bool Busy => Volatile.Read(ref _inFlight) != 0;

        private static DateTime _nextPollUtc = DateTime.MinValue;
        private static readonly TimeSpan PollEvery = TimeSpan.FromSeconds(20);

        private static string Directory => GlobalHttp.BaseUrl;
        private static string Token => KmhAccountStore.TokenFor(Directory);

        public static bool Linked => KmhAccountStore.HasToken(GlobalHttp.BaseUrl);

        public static void Reset()
        {
            State = GlobalDmState.Unknown; ErrorCode = ""; UnreadTotal = 0;
            Conversations = new List<GlobalDmConversation>();
            lock (_mapsLock) { _history.Clear(); _cursors.Clear(); _mayReply.Clear(); _readThrough.Clear(); }
            _openPeer = "";
            _nextPollUtc = DateTime.MinValue;
        }

        // A page is replaced, never appended to, so the list handed out here is not written again.
        public static List<GlobalDmMessage> History(string peerId)
        {
            lock (_mapsLock)
                return _history.TryGetValue(peerId ?? "", out List<GlobalDmMessage> list) ? list : new List<GlobalDmMessage>();
        }

        public static bool MayReply(string peerId)
        {
            lock (_mapsLock) return !_mayReply.TryGetValue(peerId ?? "", out bool v) || v;
        }

        public static bool HasMore(string peerId) => Cursor(peerId).Length > 0;

        private static string Cursor(string peerId)
        {
            lock (_mapsLock) return _cursors.TryGetValue(peerId ?? "", out string c) && c != null ? c : "";
        }

        private static string ReadThrough(string peerId)
        {
            lock (_mapsLock) return _readThrough.TryGetValue(peerId ?? "", out string t) && t != null ? t : "";
        }

        public static void Showing(string peerId) => _openPeer = peerId ?? "";

        public static void PollIfDue()
        {
            if (Busy || !Linked) return;
            if (DateTime.UtcNow < _nextPollUtc) return;
            _nextPollUtc = DateTime.UtcNow.Add(PollEvery);

            string open = _openPeer;
            if (open.Length == 0)
            {
                Run(RefreshConversationsInline, background: true);
                return;
            }
            Run(ct => FetchThenRefreshAsync(open, ct), background: true);
        }

        public static void RefreshConversations() => Run(RefreshConversationsInline);

        // The panel latches on the result, so a click that lost the slot race retries next frame instead of blanking.
        public static bool OpenConversation(string peerId)
        {
            if (string.IsNullOrEmpty(peerId)) return false;
            _openPeer = peerId;
            return Run(ct => FetchThenRefreshAsync(peerId, ct));
        }

        private static async Task FetchThenRefreshAsync(string peerId, CancellationToken ct)
        {
            await FetchAsync(peerId, ct).ConfigureAwait(false);
            await RefreshConversationsInline(ct).ConfigureAwait(false);
        }

        // Marks read only up to what was fetched; marking to "now" swallowed messages that landed mid-fetch.
        private static async Task FetchAsync(string peerId, CancellationToken ct)
        {
            GlobalHttp.Result r = await GlobalHttp.GetAsync(
                "/api/v1/dm/history?with=" + Uri.EscapeDataString(peerId), Token, ct).ConfigureAwait(false);
            ApplyHistory(peerId, r, append: false);
            if (!r.Ok) return;

            var payload = new JObject { ["with"] = peerId };
            string through = ReadThrough(peerId);
            if (through.Length > 0) payload["up_to"] = through;
            await GlobalHttp.PostAsync("/api/v1/dm/read",
                payload.ToString(Newtonsoft.Json.Formatting.None), Token, ct).ConfigureAwait(false);
        }

        public static void LoadOlder(string peerId)
        {
            string before = Cursor(peerId);
            if (before.Length == 0) return;
            Run(async ct =>
            {
                GlobalHttp.Result r = await GlobalHttp.GetAsync(
                    "/api/v1/dm/history?with=" + Uri.EscapeDataString(peerId)
                    + "&before=" + Uri.EscapeDataString(before), Token, ct).ConfigureAwait(false);
                ApplyHistory(peerId, r, append: true);
            });
        }

        internal enum SendStage { Idle, Sending, Sent, Refused, Failed }
        public static SendStage LastSend { get; private set; } = SendStage.Idle;
        public static string LastSendPeer { get; private set; } = "";
        public static string LastSendNote { get; private set; } = "";

        // False when nothing was sent (slot busy): the draft must stay in the box.
        public static bool Send(string peerId, string body)
        {
            if (string.IsNullOrWhiteSpace(body) || string.IsNullOrEmpty(peerId)) return false;
            // One key per send, not per attempt, so a retried post cannot appear twice.
            string key = Guid.NewGuid().ToString("N");
            bool went = Run(async ct =>
            {
                var payload = new JObject
                {
                    ["to"] = peerId,
                    ["body"] = body.Trim(),
                    ["idempotency_key"] = key,
                };
                GlobalHttp.Result r = await GlobalHttp.PostAsync("/api/v1/dm/send",
                    payload.ToString(Newtonsoft.Json.Formatting.None), Token, ct).ConfigureAwait(false);
                if (!r.Ok)
                {
                    // A refusal is about this message; only no answer at all means Global is unreachable.
                    if (r.Status >= 400 && r.Status < 500 && r.Status != 401)
                    {
                        SendDone(peerId, SendStage.Refused, Refusal(r.ErrorCode.Length > 0 ? r.ErrorCode : "http." + r.Status));
                        return;
                    }
                    State = r.Status == 401 ? GlobalDmState.NotLinked : GlobalDmState.Unavailable;
                    ErrorCode = r.ErrorCode;
                    SendDone(peerId, SendStage.Failed, UI.KmhText.Get("KMH_Dm_Unconfirmed",
                        "Colony Nexus Global did not answer, so this message is not confirmed. It is still in the box; send it again."));
                    return;
                }

                SendDone(peerId, SendStage.Sent, "");
                await FetchThenRefreshAsync(peerId, ct).ConfigureAwait(false);
            });
            if (went) { LastSendPeer = peerId; LastSend = SendStage.Sending; LastSendNote = ""; }
            return went;
        }

        private static void SendDone(string peerId, SendStage stage, string note)
        {
            LastSendPeer = peerId; LastSendNote = note ?? ""; LastSend = stage;
        }

        internal static void ClearSend() { if (LastSend != SendStage.Sending) { LastSend = SendStage.Idle; LastSendNote = ""; } }

        // Refusal codes are shared with friend requests.
        internal static string Refusal(string code)
        {
            switch (code)
            {
                case "KMH-FRD-751":
                case "KMH-DM-771":  return UI.KmhText.Get("KMH_Dm_CannotReply", "This player is not accepting messages from you.");
                case "KMH-FRD-753": return UI.KmhText.Get("KMH_Dm_Self", "That is you.");
                case "KMH-GCH-901": return UI.KmhText.Get("KMH_Dm_TooLong", "That message is too long or empty.");
                case "KMH-GCH-905":
                case "http.429":    return UI.KmhText.Get("KMH_Dm_Slow", "You are sending messages too quickly. Wait a moment.");
                default:            return Friends.KmhFriendsClient.RefusalText(code);
            }
        }

        private static bool Run(Func<CancellationToken, Task> work) => Run(work, background: false);

        private static bool Run(Func<CancellationToken, Task> work, bool background)
        {
            if (background)
            {
                if (Interlocked.CompareExchange(ref _pollInFlight, 1, 0) != 0) return false;
            }
            else if (Interlocked.CompareExchange(ref _inFlight, 1, 0) != 0) return false;

            if (Token.Length == 0)
            {
                State = GlobalDmState.NotLinked;
                Release(background);
                return false;
            }
            if (State != GlobalDmState.Loaded) State = GlobalDmState.Loading;

            Task.Run(async () =>
            {
                try { await work(CancellationToken.None).ConfigureAwait(false); }
                catch (Exception ex) { KmhLog.Debug($"Global DM: {ex.Message}"); State = GlobalDmState.Unavailable; }
                finally { Release(background); }
            });
            return true;
        }

        private static void Release(bool background)
            => Interlocked.Exchange(ref background ? ref _pollInFlight : ref _inFlight, 0);

        private static async Task RefreshConversationsInline(CancellationToken ct)
            => Apply(await GlobalHttp.GetAsync("/api/v1/dm/conversations", Token, ct).ConfigureAwait(false));

        private static bool MarkFailed(GlobalHttp.Result r)
        {
            if (r.Ok && r.Body != null) return false;
            State = r.Status == 401 ? GlobalDmState.NotLinked : GlobalDmState.Unavailable;
            ErrorCode = r.ErrorCode;
            return true;
        }

        private static void Apply(GlobalHttp.Result r)
        {
            if (MarkFailed(r)) return;

            var list = new List<GlobalDmConversation>();
            int unread = 0;
            if (r.Body["conversations"] is JArray arr)
                foreach (JToken t in arr)
                {
                    if (!(t is JObject o)) continue;
                    string id = (string)o["player_global_id"];
                    if (string.IsNullOrEmpty(id)) continue;
                    var c = new GlobalDmConversation
                    {
                        PlayerGlobalId = id,
                        DisplayName = (string)o["display_name"] ?? "",
                        Username = (string)o["username"] ?? "",
                        Unread = (int?)o["unread"] ?? 0,
                        LastMessageAtUnix = (long?)o["last_message_at"],
                        // Absent from an older Global: assume yes and let the send itself be the authority.
                        MayReply = (bool?)o["may_reply"] ?? true,
                    };
                    unread += c.Unread;
                    list.Add(c);
                }

            lock (_mapsLock)
                foreach (GlobalDmConversation c in list) _mayReply[c.PlayerGlobalId] = c.MayReply;
            Conversations = list;
            UnreadTotal = (int?)r.Body["unread_total"] ?? unread;
            ErrorCode = "";
            State = GlobalDmState.Loaded;
        }

        // Global returns newest first; the pane reads oldest first, so it is reversed once here.
        private static void ApplyHistory(string peerId, GlobalHttp.Result r, bool append)
        {
            if (MarkFailed(r)) return;

            var page = new List<GlobalDmMessage>();
            if (r.Body["messages"] is JArray arr)
                foreach (JToken t in arr)
                {
                    if (!(t is JObject o)) continue;
                    string id = (string)o["message_id"];
                    if (string.IsNullOrEmpty(id)) continue;
                    page.Add(new GlobalDmMessage
                    {
                        MessageId = id,
                        From = (string)o["from"] ?? "",
                        Body = (string)o["body"] ?? "",
                        CreatedAtUnix = (long?)o["created_at"] ?? 0,
                        Mine = (bool?)o["mine"] ?? false,
                    });
                }
            page.Reverse();

            string readThrough = (string)r.Body["read_through"] ?? "";
            string cursor = (string)r.Body["next_cursor"] ?? "";
            bool mayReply = (bool?)r.Body["may_reply"] ?? true;
            lock (_mapsLock)
            {
                if (append && _history.TryGetValue(peerId, out List<GlobalDmMessage> existing)) page.AddRange(existing);
                _history[peerId] = page;

                // Only the newest page carries a read mark; paging backwards must not move it.
                if (!append) _readThrough[peerId] = readThrough;
                _cursors[peerId] = cursor;
                _mayReply[peerId] = mayReply;
            }
            ErrorCode = "";
            State = GlobalDmState.Loaded;
        }
    }
}
