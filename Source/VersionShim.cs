// The ONLY place RWT's renames live (csproj RwtFlavor Old/New/RT):
//   Old (26.5.24.1)  Shared/TCPNetwork,  GameClient assembly, GameClient.* namespaces
//   New (26.6.9.1)   RTShared/RTNetwork, RTClient assembly,   GameClient.* namespaces
//   RT  (26.7.25.1)  RTShared/RTNetwork, RTClient assembly,   RTClient.* namespaces, RTShared moved into .Misc
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
using HarmonyLib;

namespace KMHPatch
{
    // Wire-level names that differ between generations.
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
