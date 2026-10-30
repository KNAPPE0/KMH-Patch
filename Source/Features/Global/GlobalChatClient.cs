using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using KMHPatch.Diagnostics;
using KMHPatch.Features.Account;
using Newtonsoft.Json.Linq;

namespace KMHPatch.Features.Global
{
    internal enum GlobalChatState { Off, NeedsAccount, Connecting, Online, Reconnecting }

    internal sealed class GlobalChatEntry
    {
        public string MessageId = "";
        public string UserId = "";
        public string Username = "";
        public string Body = "";
        public string OriginServerName = "";
        // Set ("discord"/"ingame") for a server-relayed name, which proves nothing and must never render like an account.
        public string RelaySource = "";
        public long SentAtUnix;
        // Zero from a Global that does not send ticks; Order falls back to seconds.
        public long SentAtTicks;
    }

    internal static class GlobalChatClient
    {
        public const int MaxHeld = 300;
        private const int HistoryOnConnect = 50;

        // Client-side code: Global refuses extra sessions with a bare PolicyViolation close and no error body.
        internal const string TooManySessions = "KMH-CHT-SESSIONS";

        public static GlobalChatState State { get; private set; } = GlobalChatState.Off;
        public static string LastErrorCode { get; private set; } = "";

        private static readonly object _lock = new object();
        private static readonly List<GlobalChatEntry> _messages = new List<GlobalChatEntry>();
        private static readonly HashSet<string> _seen = new HashSet<string>(StringComparer.Ordinal);

        private static CancellationTokenSource _cts;
        private static int _running;

        // Bumped when the session ends, or a frame already past ReceiveAsync at sign-out lands in the next account's log.
        // Separate from KmhIdentityGeneration so a socket-only stop keeps other polls alive; that counter is folded in.
        private static int _generation;
        internal static int SessionGeneration
            => Volatile.Read(ref _generation) + KmhIdentityGeneration.Current;

        public static bool Available =>
            KmhGlobalCapabilities.Online && KmhGlobalCapabilities.GlobalChat;

        // Hidden only when the directory answered without chat; an unfinished or failed probe still offers the tab.
        public static bool Offered =>
            Available || KmhGlobalCapabilities.State != KmhGlobalCapabilities.ProbeState.Online;

        public static string StatusSuffix
        {
            get
            {
                string Muted(string s) => "  <color=#A9AFB9>" + s + "</color>";
                string connecting = UI.KmhText.Get("KMH_Comms_GlobalConnecting", "(connecting…)");
                if (!KmhGlobalCapabilities.Online)  return Muted(connecting);
                if (!Linked)                       return Muted(UI.KmhText.Get("KMH_Comms_GlobalSignIn", "(sign in)"));
                switch (State)
                {
                    case GlobalChatState.Online:       return "";
                    case GlobalChatState.Reconnecting:
                        return Muted(LastErrorCode == TooManySessions
                            ? UI.KmhText.Get("KMH_Comms_GlobalTooMany", "(too many sessions)")
                            : UI.KmhText.Get("KMH_Comms_GlobalReconnecting", "(reconnecting…)"));
                    default:                           return Muted(connecting);
                }
            }
        }

        public static bool Linked => KmhAccountStore.HasToken(GlobalHttp.BaseUrl);

        public static List<GlobalChatEntry> Snapshot()
        {
            lock (_lock) return new List<GlobalChatEntry>(_messages);
        }

        internal static TimeSpan BackoffFor(int attempt) => KmhRealtimeBackoff.For(attempt);

        internal static Action<GlobalChatEntry> Arrived;

        // Only a launch's first history load is primed (no toasts); later loads are catch-up after an outage and are news.
        internal static bool PrimingHistory { get; private set; }
        private static bool _primed;

        internal static bool Accept(GlobalChatEntry entry)
        {
            if (entry == null || string.IsNullOrEmpty(entry.MessageId)) return false;
            bool added;
            lock (_lock)
            {
                if (!_seen.Add(entry.MessageId)) return false;
                _messages.Add(entry);
                _messages.Sort((a, b) => Order(a).CompareTo(Order(b)));
                if (_messages.Count > MaxHeld)
                {
                    int drop = _messages.Count - MaxHeld;
                    for (int i = 0; i < drop; i++) _seen.Remove(_messages[i].MessageId);
                    _messages.RemoveRange(0, drop);
                }
                added = true;
            }

            // Outside the lock: the chat layer takes its own, and holding both invites a deadlock.
            if (added) { try { Arrived?.Invoke(entry); } catch (Exception) { } }
            return added;
        }

        internal static Action<GlobalChatEntry> Withdrawn;

        // The id stays seen, so a replayed history cannot bring a withdrawn message back.
        internal static int Withdraw(string messageId = null, string fromUserId = null)
        {
            if (string.IsNullOrEmpty(messageId) && string.IsNullOrEmpty(fromUserId)) return 0;
            List<GlobalChatEntry> gone = new List<GlobalChatEntry>();
            lock (_lock)
            {
                for (int i = _messages.Count - 1; i >= 0; i--)
                {
                    GlobalChatEntry m = _messages[i];
                    bool hit = !string.IsNullOrEmpty(messageId)
                        ? string.Equals(m.MessageId, messageId, StringComparison.Ordinal)
                        : string.IsNullOrEmpty(m.RelaySource) && string.Equals(m.UserId, fromUserId, StringComparison.Ordinal);
                    if (!hit) continue;
                    gone.Add(m);
                    _messages.RemoveAt(i);
                }
            }
            foreach (GlobalChatEntry e in gone) { try { Withdrawn?.Invoke(e); } catch (Exception) { } }
            return gone.Count;
        }

        // Every chat surface calls this, not Start: chat is reachable without the main menu, before the probe answers.
        public static void EnsureRunning()
        {
            KmhGlobalCapabilities.EnsureProbed();
            if (!Available) return;
            Features.Chat.GlobalChatBridge.Install();
            RegisterTeardown();
            if (Linked) Start();
        }

        internal static void EnsureRunningForTest() => RegisterTeardown();

        private static int _teardownRegistered;

        // Registered where the feature is switched on, so a cache that exists has already said how to forget it.
        private static void RegisterTeardown()
        {
            if (Interlocked.CompareExchange(ref _teardownRegistered, 1, 0) != 0) return;
            KmhIdentityTeardown.Register("global chat", Forget);
            KmhIdentityTeardown.Register("global dms", KmhGlobalDmClient.Reset);
            KmhIdentityTeardown.Register("chat log", Features.Chat.ChatCache.ForgetGlobalChannels);
        }

        // The socket belongs to one account on one directory; left running it fed another account's log.
        public static void Forget()
        {
            Stop();
            Interlocked.Increment(ref _generation);
            Interlocked.Exchange(ref _running, 0);
            lock (_lock) { _messages.Clear(); _seen.Clear(); _primed = false; }
            State = GlobalChatState.Off;
            LastErrorCode = "";
        }

        public static void Start()
        {
            if (!Available) { State = GlobalChatState.Off; return; }
            if (!Linked) { State = GlobalChatState.NeedsAccount; return; }
            if (Interlocked.CompareExchange(ref _running, 1, 0) != 0) return;

            CancellationTokenSource mine = new CancellationTokenSource();
            lock (_lock) _cts = mine;
            CancellationToken ct = mine.Token;
            int gen = SessionGeneration;
            State = GlobalChatState.Connecting;
            // Disposed by the owning loop, not Stop: disposing while the loop holds its token throws on the next read.
            Task.Run(async () =>
            {
                try { await RunAsync(ct, gen).ConfigureAwait(false); }
                catch (OperationCanceledException) { }
                catch (Exception ex) { KmhLog.Debug($"Global chat: loop ended: {ex.GetType().Name}"); }
                finally
                {
                    Interlocked.Exchange(ref _running, 0);
                    State = GlobalChatState.Off;
                    mine.Dispose();
                }
            });
        }

        public static void Stop()
        {
            Interlocked.Increment(ref _generation);
            // Taken under the same lock that Start sets it under, or a Stop racing a Start cancels the wrong one.
            CancellationTokenSource cts;
            lock (_lock) { cts = _cts; _cts = null; }
            try { cts?.Cancel(); } catch (Exception) { }
        }

        private static async Task RunAsync(CancellationToken ct, int gen)
        {
            int attempt = 0;
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await LoadHistoryAsync(ct, gen).ConfigureAwait(false);
                    // Only a connection that carried data resets backoff: a session-limit refusal is a clean close.
                    if (await StreamAsync(ct, gen).ConfigureAwait(false)) attempt = 0;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    KmhLog.Debug($"Global chat: {ex.GetType().Name}; reconnecting.");
                }

                if (ct.IsCancellationRequested) return;
                State = GlobalChatState.Reconnecting;
                try { await Task.Delay(BackoffFor(attempt++), ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
            }
        }

        private static async Task LoadHistoryAsync(CancellationToken ct, int gen)
        {
            string token = KmhAccountStore.TokenFor(GlobalHttp.BaseUrl);
            if (token.Length == 0) return;

            GlobalHttp.Result r = await GlobalHttp.GetAsync(
                $"/api/v1/chat/global?limit={HistoryOnConnect}", token, ct).ConfigureAwait(false);
            if (!r.Ok || r.Body == null) { LastErrorCode = r.ErrorCode; return; }

            if (!(r.Body["messages"] is JArray arr)) return;

            List<GlobalChatEntry> batch = new List<GlobalChatEntry>();
            foreach (JToken t in arr) batch.Add(Parse(t as JObject));
            AcceptHistory(batch, gen);
        }

        // Shared by the real loader and the suite so a test cannot prove a priming rule the real load skips.
        internal static void AcceptHistory(IEnumerable<GlobalChatEntry> batch)
            => AcceptHistory(batch, SessionGeneration);

        internal static void AcceptHistory(IEnumerable<GlobalChatEntry> batch, int generation)
        {
            if (batch == null || generation != SessionGeneration) return;

            // Set only around a load that returned, or a failed first attempt primes on nothing and the backlog toasts.
            PrimingHistory = !_primed;
            try { foreach (GlobalChatEntry e in batch) Accept(e); }
            finally { PrimingHistory = false; _primed = true; }
        }

        private static async Task<bool> StreamAsync(CancellationToken ct, int gen)
        {
            string token = KmhAccountStore.TokenFor(GlobalHttp.BaseUrl);
            if (token.Length == 0) return false;

            string url = GlobalHttp.BaseUrl.TrimEnd('/').Replace("https://", "wss://").Replace("http://", "ws://")
                       + "/api/v1/chat/global/stream";

            using (ClientWebSocket socket = new ClientWebSocket())
            {
                socket.Options.SetRequestHeader("Authorization", "Bearer " + token);
                await socket.ConnectAsync(new Uri(url), ct).ConfigureAwait(false);
                State = GlobalChatState.Online;
                LastErrorCode = "";

                bool carried = false;
                byte[] buffer = new byte[16 * 1024];
                while (!ct.IsCancellationRequested && socket.State == WebSocketState.Open)
                {
                    WebSocketReceiveResult got = await socket.ReceiveAsync(
                        new ArraySegment<byte>(buffer), ct).ConfigureAwait(false);
                    if (got.MessageType == WebSocketMessageType.Close)
                    {
                        // PolicyViolation is Global's session-limit refusal: show why and let the backoff escalate.
                        if (socket.CloseStatus == WebSocketCloseStatus.PolicyViolation)
                        {
                            LastErrorCode = TooManySessions;
                            return false;
                        }
                        break;
                    }
                    if (got.Count <= 0) continue;
                    carried = true;

                    try
                    {
                        JObject env = JObject.Parse(Encoding.UTF8.GetString(buffer, 0, got.Count));
                        // A frame received before a sign-out must not land in the next account's log.
                        if (gen != SessionGeneration) return carried;
                        string type = (string)env["type"];
                        if (type == "message") Accept(Parse(env["message"] as JObject));
                        else if (type == "deleted") Withdraw((string)env["message_id"]);
                    }
                    catch (Exception) { }   // a malformed frame is one lost message, never a dropped connection
                }
                return carried;
            }
        }

        // Ticks are the only order between two messages sent within the same second; older Globals send seconds.
        internal static long Order(GlobalChatEntry e)
            => e.SentAtTicks > 0 ? e.SentAtTicks
             : DateTimeOffset.FromUnixTimeSeconds(e.SentAtUnix).UtcTicks;

        internal static GlobalChatEntry Parse(JObject o)
        {
            if (o == null) return null;
            return new GlobalChatEntry
            {
                MessageId        = (string)o["message_id"] ?? "",
                UserId           = (string)o["player_global_id"] ?? "",
                Username         = (string)o["username"] ?? "",
                Body             = (string)o["body"] ?? "",
                OriginServerName = (string)o["origin_server_name"] ?? "",
                RelaySource      = (string)o["relay_source"] ?? "",
                SentAtUnix       = (long?)o["sent_at"] ?? 0,
                SentAtTicks      = (long?)o["sent_at_ticks"] ?? 0,
            };
        }

        // Empty means sendable; otherwise the caller keeps the draft, since a silent drop reads as delivered.
        public static string WhyCannotSend()
        {
            if (!KmhGlobalCapabilities.Online)   return "Global is unreachable right now.";
            if (!KmhGlobalCapabilities.GlobalChat) return "This directory does not offer Global Chat.";
            if (!Linked)                         return "Link your Colony Nexus account to use Global Chat.";
            if (LastErrorCode == TooManySessions)
                return UI.KmhText.Get("KMH_Chat_TooManySessions",
                    "This Colony Nexus account already has the maximum number of Global Chat sessions open. "
                  + "Close one of your other copies of RimWorld.");
            return "";
        }

        public static bool Send(string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return false;
            if (!Available || !Linked) return false;
            string text = body.Trim();
            // Empty from the main menu is valid: Global posts it with no origin server.
            string origin = SubProtocol.KmhDispatcher.ServerId ?? "";

            Task.Run(async () =>
            {
                try
                {
                    string token = KmhAccountStore.TokenFor(GlobalHttp.BaseUrl);
                    if (token.Length == 0) return;
                    string json = Newtonsoft.Json.JsonConvert.SerializeObject(
                        new SendBody { body = text, origin_server_id = string.IsNullOrEmpty(origin) ? null : origin });
                    GlobalHttp.Result r = await GlobalHttp.PostAsync("/api/v1/chat/global", json, token,
                                                                    CancellationToken.None).ConfigureAwait(false);
                    if (r.Ok) return;
                    LastErrorCode = r.ErrorCode;
                    // The post is accepted optimistically, so a refusal has to reach the player here or nowhere.
                    string why = Explain(r.ErrorCode);
                    if (why.Length > 0) KmhMainThread.Post(() => Notifications.KmhNotifications.Rejected(why));
                }
                catch (Exception ex) { KmhLog.Debug($"Global chat: send failed: {ex.GetType().Name}"); }
            });
            return true;
        }

        public static string Explain(string code)
        {
            switch (code)
            {
                case "KMH-GCH-901": return "Global Chat refused an empty message.";
                case "KMH-GCH-902": return "Choose a Colony Nexus username before posting to Global Chat.";
                case "KMH-GCH-903": return "You are muted in Global Chat.";
                case "KMH-GCH-904": return "You are banned from Global Chat.";
                case "KMH-GCH-905": return "Slow down - too many messages.";
                case "KMH-GCH-907": return "That message was not posted: it contains words Global Chat does not allow.";
                case "KMH-LNK-705": return "Link your Colony Nexus account to use Global Chat.";
                default:            return code.Length > 0 ? $"Global Chat refused that ({code})." : "";
            }
        }

        private sealed class SendBody
        {
            public string body;
            public string origin_server_id;
        }

        internal static void ResetForTest()
        {
            Stop();
            lock (_lock) { _messages.Clear(); _seen.Clear(); }
            PrimingHistory = false;
            _primed = false;
            State = GlobalChatState.Off;
            LastErrorCode = "";
        }
    }
}
