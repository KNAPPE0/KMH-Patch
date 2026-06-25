// Maps RWT's renamed 26.6.9.1 API onto the names the codebase uses, so one source tree builds against both RWT
// generations (csproj RwtFlavor Old/New)
#if RWT_NEW
global using RTNetwork.Components;
global using RTNetwork.Packets;
global using RTShared;
global using RTShared.Files;
global using RTShared.Files.Guilds;
global using SessionHandler = GameClient.Managers.SessionManager;
global using MainThreadHandler = GameClient.Managers.MainThreadManager;
global using TCPNetwork = RTNetwork.Components;
#else
global using TCPNetwork;
global using TCPNetwork.Packets;
global using Shared;
global using Shared.Files;
global using Shared.Files.Guilds;
global using SessionHandler = GameClient.Misc.SessionHandler;
global using MainThreadHandler = GameClient.Misc.MainThreadHandler;
#endif

namespace KMHPatch
{
    // Wire-level names that differ between generations.
    internal static class RwtCompat
    {
#if RWT_NEW
        private const string       ChatHeaderName     = "Chat";
        private const PacketHeader ChatHeaderFallback = PacketHeader.Chat;
#else
        private const string       ChatHeaderName     = "ChatManager";
        private const PacketHeader ChatHeaderFallback = PacketHeader.ChatManager;
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
    }
}
