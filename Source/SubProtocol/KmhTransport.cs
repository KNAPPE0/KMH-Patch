namespace KMHPatch.SubProtocol
{
    public enum KmhTransportStatus
    {
        Offline,          // not on a KMH server (or disconnected)
        ApiConnecting,
        ApiConnected,
        ChatFallback,     // talking over the legacy RWT-chat transport
        VersionMismatch,
        AuthFailed,
    }

    public static class KmhTransport
    {
        public static KmhTransportStatus Status { get; internal set; } = KmhTransportStatus.Offline;

        // Set only when an advertised transport could not be reached; a chat-only server and a dead port look alike without it.
        public static string DegradedReason { get; internal set; }

        public static string StatusLabel
        {
            get
            {
                switch (Status)
                {
                    case KmhTransportStatus.ApiConnected:    return "Colony Nexus API connected";
                    case KmhTransportStatus.ApiConnecting:   return "Colony Nexus API connecting…";
                    case KmhTransportStatus.ChatFallback:
                        return string.IsNullOrEmpty(DegradedReason) ? "RWT chat fallback"
                                                                    : "RWT chat - Colony Nexus port unreachable";
                    case KmhTransportStatus.VersionMismatch: return "Version mismatch";
                    case KmhTransportStatus.AuthFailed:      return "Colony Nexus auth failed";
                    default:                                 return "Colony Nexus offline";
                }
            }
        }
    }
}
