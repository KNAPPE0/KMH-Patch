// The ONLY place RWT's renames live, so feature code never names a generation itself.
#if RWT_RT
global using RTNetwork.Components;
global using RTNetwork.Packets;
global using RTShared.Misc;
global using RTShared.Files;
global using RTShared.Files.Guilds;
global using RTClient.Misc;
global using RTClient.Managers;
global using RTClient.Dialogs;
global using RTClient.Dialogs.ServerBrowser;
global using RTClient.PacketManagers;
global using static RTClient.Hooks.TCPNetwork.ClientNetwork;
global using SessionHandler = RTClient.Managers.SessionManager;
global using MainThreadHandler = RTClient.Managers.MainThreadManager;
global using TCPNetwork = RTNetwork.Components;
#elif RWT_NEW
global using RTNetwork.Components;
global using RTNetwork.Packets;
global using RTShared;
global using RTShared.Files;
global using RTShared.Files.Guilds;
global using GameClient.Misc;
global using GameClient.Managers;
global using GameClient.Dialogs;
global using GameClient.Dialogs.ServerBrowser;
global using GameClient.PacketManagers;
global using static GameClient.Hooks.TCPNetwork.ClientNetwork;
global using SessionHandler = GameClient.Managers.SessionManager;
global using MainThreadHandler = GameClient.Managers.MainThreadManager;
global using TCPNetwork = RTNetwork.Components;
#else
global using TCPNetwork;
global using TCPNetwork.Packets;
global using Shared;
global using Shared.Files;
global using Shared.Files.Guilds;
global using GameClient.Misc;
global using GameClient.Managers;
global using GameClient.Dialogs;
global using GameClient.Dialogs.ServerBrowser;
global using GameClient.PacketManagers;
global using static GameClient.Hooks.TCPNetwork.ClientNetwork;
global using SessionHandler = GameClient.Misc.SessionHandler;
global using MainThreadHandler = GameClient.Misc.MainThreadHandler;
#endif

using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace KMHPatch
{
    internal static class RwtCompat
    {
#if RWT_NEW || RWT_RT
        private const string       ChatHeaderName     = "Chat";
        private const PacketHeader ChatHeaderFallback = PacketHeader.Chat;
#else
        private const string       ChatHeaderName     = "ChatManager";
        private const PacketHeader ChatHeaderFallback = PacketHeader.ChatManager;
#endif

        public const string ClientNsRoot =
#if RWT_RT
            "RTClient";
#else
            "GameClient";
#endif
        private static PacketHeader? _chatHeader;

        // By name at runtime: RWT renumbers PacketHeader between builds, so a baked ordinal mis-tags KMH chat.
        public static PacketHeader ChatHeader
        {
            get
            {
                if (_chatHeader.HasValue) return _chatHeader.Value;
                try { _chatHeader = (PacketHeader)System.Enum.Parse(typeof(PacketHeader), ChatHeaderName); }
                catch { _chatHeader = ChatHeaderFallback; }
                return _chatHeader.Value;
            }
        }

        // The next two are reflected, not called: RWT reshaped both, and a direct call fails at JIT time, before any try runs.

        private static bool       _versionResolved;
        private static PropertyInfo _versionProp;
        private static FieldInfo    _versionField;

        public static string ExecutableVersion
        {
            get
            {
                if (!_versionResolved)
                {
                    _versionResolved = true;
                    try { ResolveVersion(); } catch { }
                }
                try
                {
                    if (_versionProp  != null) return _versionProp.GetValue(null)  as string ?? "";
                    if (_versionField != null) return _versionField.GetValue(null) as string ?? "";
                }
                catch { }
                return "";
            }
        }

        // NoInlining: naming AccessTools in the getter makes the JIT load Harmony before its try, so the catch cannot run.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ResolveVersion()
        {
            _versionProp  = AccessTools.Property(typeof(CommonValues), "ExecutableVersion");
            _versionField = AccessTools.Field(typeof(CommonValues), "ExecutableVersion");
        }

        private static bool       _disconnectResolved;
        private static MethodInfo _disconnect;

        // False means RWT offered no way back to the menu, so the caller has to say so rather than look like it worked.
        public static bool DisconnectToMainMenu()
        {
            if (!_disconnectResolved)
            {
                _disconnectResolved = true;
                try
                {
                    Type t = typeof(DisconnectionManager);
                    // HandleDisconnect asks "Connection lost. Save game?" - wrong for a deliberate leave, so it is only the fallback.
                    _disconnect = AccessTools.Method(t, "DisconnectToMenu") ?? AccessTools.Method(t, "HandleDisconnect");
                }
                catch { }
            }
            if (_disconnect == null) return false;
            try
            {
                _disconnect.Invoke(null, null);
                // DisconnectToMenu only loads the menu, so close the connection as RWT's Cancel does, once the menu is up.
                Verse.LongEventHandler.ExecuteWhenFinished(CloseConnection);
                return true;
            }
            catch (Exception ex) { Diagnostics.KmhLog.Warn($"RWT disconnect failed: {ex.Message}"); return false; }
        }

        private static void CloseConnection()
        {
            try
            {
                object endpoint = Network.ServerEndpoint;
                if (endpoint == null) return;
                MethodInfo mark = AccessTools.Method(endpoint.GetType(), "MarkForDisconnect");
                if (mark == null) { Diagnostics.KmhLog.Warn("RWT disconnect: this RWT build cannot close the connection; it stays open until it times out."); return; }
                mark.Invoke(endpoint, mark.GetParameters().Length == 1 ? new object[] { true } : new object[0]);
            }
            catch (Exception ex) { Diagnostics.KmhLog.Warn($"RWT disconnect: closing the connection failed: {ex.Message}"); }
        }

        // Exactly RWT's login-dialog connect path. Keep this two-argument overload: reflection callers bind to its signature.
        public static bool ConnectTo(string host, int port) => ConnectTo(host, port, null);

        // With the listing's server id, a server running on this PC is dialled locally (KmhLocalServer).
        public static bool ConnectTo(string host, int port, string expectedServerId)
        {
            if (string.IsNullOrWhiteSpace(host) || port <= 0 || port > 65535) return false;
            try
            {
                TCPNetwork.Network.Ip = Features.Global.KmhLocalServer.DialHost(host, port, expectedServerId).Trim();
                TCPNetwork.Network.Port = port;
                StartFeature();
                return true;
            }
            catch (Exception ex)
            {
                Diagnostics.KmhLog.Warn($"RWT connect failed: {ex.Message}");
                return false;
            }
        }

        // Disconnected, Connecting or Connected, as RWT's own session reports it.
        public static string NetworkState
        {
            get
            {
                try { return SessionHandler.CurrentNetworkState.ToString(); }
                catch { return ""; }
            }
        }

        private static bool       _loginResolved;
        private static MethodInfo _loginValid, _loginPrompt;

        // RWT's Join refuses without a saved account and asks for one, so KMH asks too; null when this build has no check.
        public static bool? LoginLooksValid()
        {
            ResolveLogin();
            if (_loginValid == null) return null;
            try { return (bool)_loginValid.Invoke(null, null); }
            catch (Exception ex) { Diagnostics.KmhLog.Debug($"RWT login check failed: {ex.Message}"); return null; }
        }

        // RWT's "Account Setup" dialog; it saves the account and leaves connecting to whoever asked.
        public static bool PromptLogin()
        {
            ResolveLogin();
            if (_loginPrompt == null) return false;
            try { _loginPrompt.Invoke(null, null); return true; }
            catch (Exception ex) { Diagnostics.KmhLog.Warn($"RWT account prompt failed: {ex.Message}"); return false; }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ResolveLogin()
        {
            if (_loginResolved) return;
            _loginResolved = true;
            try
            {
                Type menu = ResolveType(ClientNsRoot, "MainMenuPatch", ClientNsRoot + ".Patches.Pages.MainMenuPatch");
                _loginValid = menu == null ? null : AccessTools.Method(menu, "CheckIfLoginIsValid");
                if (_loginValid != null && _loginValid.ReturnType != typeof(bool)) _loginValid = null;
                Type login = ResolveType(ClientNsRoot, "PM_Login", ClientNsRoot + ".PacketManagers.PM_Login");
                _loginPrompt = login == null ? null : AccessTools.Method(login, "PromptCreateAccount", Type.EmptyTypes);
            }
            catch { }
        }

        private static bool       _changeVersionResolved;
        private static MethodInfo _changeVersion;

        // RWT's version downloader is absent before 26.7.25.1, so the browser offers a switch only when it exists.
        private static MethodInfo ChangeVersionMethod()
        {
            if (!_changeVersionResolved)
            {
                _changeVersionResolved = true;
                try
                {
                    Type t = ResolveType("RTClient", "VersionManager", "RTClient.Managers.VersionManager");
                    _changeVersion = t == null ? null : AccessTools.Method(t, "ChangeVersion", new[] { typeof(string) });
                }
                catch { }
            }
            return _changeVersion;
        }

        public static bool CanChangeRwtVersion => ChangeVersionMethod() != null;

        public static bool ChangeRwtVersion(string version)
        {
            MethodInfo m = ChangeVersionMethod();
            if (m == null || string.IsNullOrWhiteSpace(version)) return false;
            try { m.Invoke(null, new object[] { version.Trim() }); return true; }
            catch (Exception ex)
            {
                Diagnostics.KmhLog.Warn($"RWT version switch failed: {ex.Message}");
                return false;
            }
        }

        // Known full names first, then any matching simple name under nsRoot: RWT moves dialogs between namespaces.
        public static Type ResolveType(string nsRoot, string simpleName, params string[] fullNames)
        {
            foreach (string fn in fullNames)
            {
                Type t = AccessTools.TypeByName(fn);
                if (t != null) return t;
            }
            foreach (Type t in AccessTools.AllTypes())
                if (t.Name == simpleName && (t.Namespace?.StartsWith(nsRoot, StringComparison.Ordinal) ?? false))
                    return t;
            return null;
        }
    }
}
