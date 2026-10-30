using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using KMHPatch.Diagnostics;

namespace KMHPatch.Features.Discord
{
    // DiscordRPC.dll ships with RWT and is reached by reflection: a reshaped member or a missing file must mean
    // "no presence", never a type load that takes the mod down at startup.
    internal static class KmhDiscordRpc
    {
        private static bool _probed;
        private static Assembly _asm;
        private static Type _client, _presence, _assets, _party, _secrets, _timestamps, _button, _privacy;

        internal static string Unavailable { get; private set; } = "";

        internal static bool ForceUnavailableForTest;

        public static bool Available
        {
            get { Probe(); return !ForceUnavailableForTest && _asm != null; }
        }

        private static void Probe()
        {
            if (_probed) return;
            _probed = true;
            try
            {
                _asm = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => string.Equals(a.GetName().Name, "DiscordRPC", StringComparison.Ordinal));
                if (_asm == null) { Unavailable = "DiscordRPC.dll is not loaded"; return; }

                _client     = _asm.GetType("DiscordRPC.DiscordRpcClient");
                _presence   = _asm.GetType("DiscordRPC.RichPresence");
                _assets     = _asm.GetType("DiscordRPC.Assets");
                _party      = _asm.GetType("DiscordRPC.Party");
                _secrets    = _asm.GetType("DiscordRPC.Secrets");
                _timestamps = _asm.GetType("DiscordRPC.Timestamps");
                _button     = _asm.GetType("DiscordRPC.Button");
                _privacy    = _asm.GetType("DiscordRPC.Party+PrivacySetting");

                if (_client == null || _presence == null) { _asm = null; Unavailable = "DiscordRPC is a shape Colony Nexus does not know"; }
            }
            catch (Exception ex) { _asm = null; Unavailable = ex.GetType().Name; }
        }

        // RimWorld on Steam. Discord launches the game through this when somebody accepts an invite.
        private const string SteamAppId = "294100";

        // An unused pipe, so "Discord not running" is testable without touching the player's Discord.
        internal static int PipeForTest = -1;

        public static object CreateClient(string applicationId, bool wantJoin)
        {
            Probe();
            if (_asm == null || string.IsNullOrEmpty(applicationId)) return null;
            try
            {
                object c;
                ConstructorInfo piped = PipeForTest >= 0
                    ? _client.GetConstructors().FirstOrDefault(k => k.GetParameters().Length == 5
                          && k.GetParameters()[1].ParameterType == typeof(int))
                    : null;
                if (piped != null) c = piped.Invoke(new object[] { applicationId, PipeForTest, null, true, null });
                else c = _client.GetConstructor(new[] { typeof(string) })?.Invoke(new object[] { applicationId });
                if (c == null) return null;
                if (wantJoin) RegisterUriScheme(c);
                _client.GetMethod("Initialize")?.Invoke(c, null);
                return c;
            }
            catch (Exception ex) { KmhLog.Debug($"Discord: could not start the RPC client ({ex.GetType().Name})."); return null; }
        }

        // Without this the library refuses any presence carrying a join secret, freezing the whole status.
        // Leaving Join on is the player's consent to the one per-user key it writes.
        private static void RegisterUriScheme(object client)
        {
            try
            {
                MethodInfo m = _client.GetMethod("RegisterUriScheme");
                if (m == null) { KmhLog.Debug("Discord: this DiscordRPC build cannot register a URI scheme - Join is off."); return; }

                object[] args = m.GetParameters().Length == 2
                    ? new object[] { SteamAppId, null }
                    : new object[] { SteamAppId };
                bool ok = m.Invoke(client, args) as bool? ?? false;
                if (!ok) KmhLog.Debug("Discord: the URI scheme could not be registered - Join will not be offered.");
            }
            catch (Exception ex) { KmhLog.Debug($"Discord: URI scheme registration failed ({ex.GetType().Name})."); }
        }

        // Asked of the library: a registration can report success yet leave the flag false.
        public static bool HasUriScheme(object client)
        {
            try { return _client.GetProperty("HasRegisteredUriScheme")?.GetValue(client) as bool? ?? false; }
            catch { return false; }
        }

        // The library disposes itself on a fatal connection failure, then silently accepts SetPresence forever.
        public static bool IsAlive(object client)
        {
            if (client == null) return false;
            try { return _client.GetProperty("IsDisposed")?.GetValue(client) as bool? != true; }
            catch { return true; }
        }

        // The library takes the first discord-ipc pipe that answers, so with two clients it may pick the other account.
        public static string ConnectionInfo(object client)
        {
            try
            {
                object pipe = _client.GetProperty("TargetPipe")?.GetValue(client);
                object user = _client.GetProperty("CurrentUser")?.GetValue(client);
                return $"pipe {pipe?.ToString() ?? "?"}, account {(user == null ? "(not ready)" : user.ToString())}";
            }
            catch { return "unknown"; }
        }

        public static void Dispose(object client)
        {
            try { (client as IDisposable)?.Dispose(); } catch { }
        }

        // The library pumps its callbacks on whatever thread calls this, so the caller does it from the game loop.
        public static void Invoke(object client)
        {
            try { _client.GetMethod("Invoke")?.Invoke(client, null); } catch { }
        }

        public static void SetPresence(object client, object presence)
        {
            try { _client.GetMethod("SetPresence", new[] { _presence })?.Invoke(client, new[] { presence }); }
            catch (Exception ex) { KmhLog.Debug($"Discord: SetPresence failed ({ex.GetType().Name})."); }
        }

        public static void ClearPresence(object client)
        {
            try { _client.GetMethod("ClearPresence")?.Invoke(client, null); } catch { }
        }

        private static Action<string> _onJoin;
        private static PropertyInfo _joinSecret;

        // Relaxed delegate binding lets object stand in for JoinMessage, so KMH never names a type it does not reference.
        private static void OnJoinShim(object sender, object args)
        {
            try { _onJoin?.Invoke((string)_joinSecret?.GetValue(args)); }
            catch (Exception ex) { KmhLog.Debug($"Discord: join callback threw ({ex.GetType().Name})."); }
        }

        public static bool SubscribeJoin(object client, Action<string> onJoin)
        {
            try
            {
                EventInfo ev = _client.GetEvent("OnJoin");
                MethodInfo invoke = ev?.EventHandlerType?.GetMethod("Invoke");
                if (invoke == null) return false;

                // The event's second argument is a JoinMessage carrying a Secret property.
                _joinSecret = invoke.GetParameters().Last().ParameterType.GetProperty("Secret");
                if (_joinSecret == null) return false;
                _onJoin = onJoin;

                Delegate handler = Delegate.CreateDelegate(ev.EventHandlerType, null,
                    typeof(KmhDiscordRpc).GetMethod(nameof(OnJoinShim), BindingFlags.NonPublic | BindingFlags.Static),
                    throwOnBindFailure: false);
                if (handler == null) { KmhLog.Debug("Discord: the OnJoin handler shape is not one KMH can bind."); return false; }

                ev.AddEventHandler(client, handler);
                Subscribe(client, "Join");
                return true;
            }
            catch (Exception ex) { KmhLog.Debug($"Discord: could not subscribe to joins ({ex.GetType().Name})."); return false; }
        }

        // Written on the library's thread, read and logged on the main thread.
        private static volatile string _echo = "", _error = "";
        private static volatile bool _connected;
        private static int _echoSeen, _echoLogged, _errorSeen, _errorLogged, _failures;

        internal static bool Connected => _connected;
        internal static int EchoCount => _echoSeen;
        internal static string LastEcho => _echo;
        internal static string LastError => _error;

        public static void SubscribeDiagnostics(object client)
        {
            _connected = false;
            Bind(client, "OnReady", nameof(OnReadyShim));
            Bind(client, "OnError", nameof(OnErrorShim));
            Bind(client, "OnConnectionFailed", nameof(OnFailedShim));
            Bind(client, "OnClose", nameof(OnFailedShim));
            Bind(client, "OnRpcMessage", nameof(OnRpcShim));
        }

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> _messages
            = new System.Collections.Concurrent.ConcurrentDictionary<string, int>(StringComparer.Ordinal);

        // DiscordRPC 1.6.1's OnPresenceUpdate never fires, so the raw PresenceUpdate message is read here instead.
        private static void OnRpcShim(object sender, object message)
        {
            try
            {
                string type = message?.GetType().GetProperty("Type")?.GetValue(message)?.ToString() ?? "?";
                _messages.AddOrUpdate(type, 1, (_, n) => n + 1);
                if (type == "PresenceUpdate") OnPresenceShim(sender, message);
            }
            catch { }
        }

        private static void Bind(object client, string eventName, string shim)
        {
            try
            {
                EventInfo ev = _client.GetEvent(eventName);
                if (ev == null) return;
                Delegate d = Delegate.CreateDelegate(ev.EventHandlerType, null,
                    typeof(KmhDiscordRpc).GetMethod(shim, BindingFlags.NonPublic | BindingFlags.Static), throwOnBindFailure: false);
                if (d != null) ev.AddEventHandler(client, d);
            }
            catch { }
        }

        private static void OnReadyShim(object sender, object args) => _connected = true;

        private static void OnFailedShim(object sender, object args) { _connected = false; Interlocked.Increment(ref _failures); }

        private static void OnPresenceShim(object sender, object args)
        {
            _connected = true;
            Interlocked.Increment(ref _echoSeen);
            try
            {
                object rp = args?.GetType().GetProperty("Presence")?.GetValue(args);
                object assets = rp?.GetType().GetProperty("Assets")?.GetValue(rp);
                string details = rp?.GetType().GetProperty("Details")?.GetValue(rp) as string ?? "";
                string state = rp?.GetType().GetProperty("State")?.GetValue(rp) as string ?? "";
                string large = AssetEcho(assets, "Large"), small = AssetEcho(assets, "Small");
                _echo = $"details='{details}' state='{state}' large={large} small={small}";
                KmhPresenceAssets.NoteEcho(large, small);
            }
            catch (Exception ex) { _echo = "unreadable (" + ex.GetType().Name + ")"; }
        }

        // Discord answers an uploaded art key with the asset's numeric id; anything else means it has no such image.
        private static string AssetEcho(object assets, string which)
        {
            if (assets == null) return "none";
            object id = assets.GetType().GetProperty(which + "ImageID")?.GetValue(assets);
            string key = assets.GetType().GetProperty(which + "ImageKey")?.GetValue(assets) as string;
            if (id != null) return "id:" + id;
            if (!string.IsNullOrEmpty(key) && key.All(char.IsDigit)) return "id:" + key;
            return string.IsNullOrEmpty(key) ? "none" : "key:" + key;
        }

        private static void OnErrorShim(object sender, object args)
        {
            try
            {
                string code = args?.GetType().GetProperty("Code")?.GetValue(args)?.ToString() ?? "?";
                string message = args?.GetType().GetProperty("Message")?.GetValue(args) as string ?? "";
                _error = code + ": " + Trim(message, 200);
                Interlocked.Increment(ref _errorSeen);
            }
            catch { }
        }

        // Main thread, from the presence tick.
        public static void LogDiagnostics()
        {
            int echo = _echoSeen;
            if (echo != _echoLogged)
            {
                _echoLogged = echo;
                if (KmhLog.DebugEnabled) KmhLog.Debug($"Discord: accepted #{echo} {_echo}");
            }
            int err = _errorSeen;
            if (err != _errorLogged)
            {
                _errorLogged = err;
                KmhLog.Warn($"Discord: refused a status update ({_error}).");
            }
        }

        internal static string DiagnosticsLine()
            => $"discord={(_connected ? "connected" : "waiting")} accepted={_echoSeen} refused={_errorSeen} failures={_failures} "
             + $"rpc=[{string.Join(",", _messages.Select(kv => kv.Key + ":" + kv.Value).ToArray())}] "
             + $"echo=[{_echo}]{(_error.Length > 0 ? " lastError=[" + _error + "]" : "")}";

        internal static void ResetDiagnosticsForTest()
        {
            _echo = _error = ""; _connected = false; _messages.Clear();
            _echoSeen = _echoLogged = _errorSeen = _errorLogged = _failures = 0;
        }

        private static void Subscribe(object client, string flagName)
        {
            try
            {
                Type flags = _asm.GetType("DiscordRPC.EventType");
                if (flags == null) return;
                object flag = Enum.Parse(flags, flagName);
                _client.GetMethod("Subscribe", new[] { flags })?.Invoke(client, new[] { flag });
            }
            catch { }
        }

        public sealed class Presence
        {
            public string Details = "", State = "";
            public string LargeImageUrl = "", LargeImageText = "", SmallImageUrl = "", SmallImageText = "";
            // Asset names uploaded to the Discord app; without one Discord draws no image at all.
            public string LargeImageKey = "", SmallImageKey = "";
            public string PartyId = "";
            public int    PartySize, PartyMax;
            public bool   PartyPublic;
            public string JoinSecret = "";
            public DateTime? StartedUtc;
            public string Button1Label = "", Button1Url = "", Button2Label = "", Button2Url = "";
        }

        public static object Build(Presence p)
        {
            Probe();
            if (_asm == null) return null;
            try
            {
                object rp = Activator.CreateInstance(_presence);
                Set(rp, "Details", Trim(p.Details, 128));
                Set(rp, "State", Trim(p.State, 128));

                if (!string.IsNullOrEmpty(p.LargeImageUrl) || !string.IsNullOrEmpty(p.SmallImageUrl)
                    || !string.IsNullOrEmpty(p.LargeImageKey) || !string.IsNullOrEmpty(p.SmallImageKey)
                    || !string.IsNullOrEmpty(p.LargeImageText) || !string.IsNullOrEmpty(p.SmallImageText))
                {
                    object a = Activator.CreateInstance(_assets);
                    // A Url needs no asset uploaded to the Discord app, which lets a server show its own picture.
                    if (!string.IsNullOrEmpty(p.LargeImageKey)) Set(a, "LargeImageKey", p.LargeImageKey);
                    if (!string.IsNullOrEmpty(p.SmallImageKey)) Set(a, "SmallImageKey", p.SmallImageKey);
                    if (!string.IsNullOrEmpty(p.LargeImageUrl)) Set(a, "LargeImageUrl", p.LargeImageUrl);
                    if (!string.IsNullOrEmpty(p.LargeImageText)) Set(a, "LargeImageText", Trim(p.LargeImageText, 128));
                    if (!string.IsNullOrEmpty(p.SmallImageUrl)) Set(a, "SmallImageUrl", p.SmallImageUrl);
                    if (!string.IsNullOrEmpty(p.SmallImageText)) Set(a, "SmallImageText", Trim(p.SmallImageText, 128));
                    Set(rp, "Assets", a);
                }

                if (!string.IsNullOrEmpty(p.PartyId) && p.PartyMax > 0)
                {
                    object party = Activator.CreateInstance(_party);
                    Set(party, "ID", p.PartyId);
                    Set(party, "Size", Math.Max(1, p.PartySize));
                    Set(party, "Max", p.PartyMax);
                    if (_privacy != null) Set(party, "Privacy", Enum.Parse(_privacy, p.PartyPublic ? "Public" : "Private"));
                    Set(rp, "Party", party);
                }

                if (!string.IsNullOrEmpty(p.JoinSecret))
                {
                    object s = Activator.CreateInstance(_secrets);
                    Set(s, "JoinSecret", p.JoinSecret);
                    Set(rp, "Secrets", s);
                }
                // Discord renders buttons or Join, never both; a button silently replacing Join is worse than none.
                else if (!string.IsNullOrEmpty(p.Button1Url) || !string.IsNullOrEmpty(p.Button2Url))
                {
                    var list = new System.Collections.Generic.List<object>();
                    if (!string.IsNullOrEmpty(p.Button1Url)) list.Add(Button(p.Button1Label, p.Button1Url));
                    if (!string.IsNullOrEmpty(p.Button2Url)) list.Add(Button(p.Button2Label, p.Button2Url));
                    Array arr = Array.CreateInstance(_button, list.Count);
                    for (int i = 0; i < list.Count; i++) arr.SetValue(list[i], i);
                    Set(rp, "Buttons", arr);
                }

                if (p.StartedUtc.HasValue)
                {
                    object t = Activator.CreateInstance(_timestamps);
                    Set(t, "Start", p.StartedUtc.Value);
                    Set(rp, "Timestamps", t);
                }
                return rp;
            }
            catch (Exception ex) { KmhLog.Debug($"Discord: could not build a presence ({ex.GetType().Name})."); return null; }
        }

        private static object Button(string label, string url)
        {
            object b = Activator.CreateInstance(_button);
            Set(b, "Label", Trim(string.IsNullOrEmpty(label) ? "Open" : label, 31));
            Set(b, "Url", url);
            return b;
        }

        // Logged once per missing property: a silent SetValue hides a renamed member quietly doing nothing.
        private static readonly System.Collections.Generic.HashSet<string> _missingLogged
            = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);

        private static void Set(object target, string property, object value)
        {
            try
            {
                System.Reflection.PropertyInfo pi = target.GetType().GetProperty(property);
                if (pi == null)
                {
                    if (_missingLogged.Add(target.GetType().FullName + "." + property))
                        Diagnostics.KmhLog.Debug($"Discord: this DiscordRPC build has no "
                                               + $"{target.GetType().Name}.{property} - that part of the status is skipped.");
                    return;
                }
                pi.SetValue(target, value);
            }
            catch { }
        }

        // Bidi overrides let a name render as something else, and char.IsControl misses them (they are format chars).
        // Joiners and marks used by real scripts and emoji are deliberately left alone.
        private static bool IsBidiOverride(char c)
            => c == '‎' || c == '‏' || (c >= '‪' && c <= '‮') || (c >= '⁦' && c <= '⁩');

        // Mostly owner-written wire text: Discord rejects (not truncates) a presence over its UTF-8 byte limit,
        // and a control character can take its parser down.
        internal static string Trim(string s, int maxBytes)
        {
            if (string.IsNullOrEmpty(s) || maxBytes <= 0) return "";

            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                if (c == '\t' || c == '\n' || c == '\r') sb.Append(' ');
                else if (!char.IsControl(c) && !IsBidiOverride(c)) sb.Append(c);
            }
            string clean = sb.ToString().Trim();
            if (clean.Length == 0) return "";

            var utf8 = Encoding.UTF8;
            if (utf8.GetByteCount(clean) <= maxBytes) return clean;

            const string More = "…";
            int budget = maxBytes - utf8.GetByteCount(More);
            if (budget <= 0) return "";

            // Advanced a whole character at a time, so a cut never lands inside a surrogate pair.
            int used = 0, cut = 0;
            for (int i = 0; i < clean.Length; )
            {
                int len = char.IsHighSurrogate(clean[i]) && i + 1 < clean.Length ? 2 : 1;
                int size = utf8.GetByteCount(clean.ToCharArray(i, len), 0, len);
                if (used + size > budget) break;
                used += size;
                i += len;
                cut = i;
            }
            return clean.Substring(0, cut).TrimEnd() + More;
        }
    }
}
