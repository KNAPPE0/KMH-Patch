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
        public const PacketHeader ChatHeader = PacketHeader.Chat;
#else
        public const PacketHeader ChatHeader = PacketHeader.ChatManager;
#endif
    }
}
