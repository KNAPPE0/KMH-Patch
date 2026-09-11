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

        // Namespace root for RWT's client types; scopes ResolveType's fallback scan.
        public const string ClientNsRoot =
#if RWT_RT
            "RTClient";
#else
            "GameClient";
#endif
        private static PacketHeader? _chatHeader;

        // Resolve by name at runtime; RWT renumbers PacketHeader between builds so a baked ordinal mis-tags KMH chat
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

        // The next two are reflected, not called: RWT reshaped both across 26.7.25.1 -> 26.8.31.1, and a direct call binds at JIT time, throwing before any try block is entered.

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
                    try
                    {
                        _versionProp  = AccessTools.Property(typeof(CommonValues), "ExecutableVersion");
                        _versionField = AccessTools.Field(typeof(CommonValues), "ExecutableVersion");
                    }
                    catch { }
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
            try { _disconnect.Invoke(null, null); return true; }
            catch (Exception ex) { Diagnostics.KmhLog.Warn($"RWT disconnect failed: {ex.Message}"); return false; }
        }

        // Resolve an RWT type across versions: known full names first, else any matching simple name under nsRoot (RWT moves dialogs between namespaces)
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
