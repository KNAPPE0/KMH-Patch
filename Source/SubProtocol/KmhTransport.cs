namespace KMHPatch.SubProtocol
{
    // Which transport KMH is using to reach the server, surfaced one-line in the KMH tab.
    public enum KmhTransportStatus
    {
        Offline,          // not on a KMH server (or disconnected)
        ApiConnecting,    // KMH API socket dialing
        ApiConnected,     // talking over the KMH API transport
        ChatFallback,     // talking over the legacy RWT-chat transport
        VersionMismatch,  // server speaks a different KMH protocol version
        AuthFailed,       // KMH API auth rejected
    }

    public static class KmhTransport
    {
        public static KmhTransportStatus Status { get; internal set; } = KmhTransportStatus.Offline;

        // Set only when an advertised transport could not be reached; a chat-only server and a dead port look alike without it.
        public static string DegradedReason { get; internal set; }

        // Short label for the KMH tab.
        public static string StatusLabel
        {
            get
            {
                switch (Status)
                {
                    case KmhTransportStatus.ApiConnected:    return "KMH API connected";
                    case KmhTransportStatus.ApiConnecting:   return "KMH API connecting…";
                    case KmhTransportStatus.ChatFallback:
                        return string.IsNullOrEmpty(DegradedReason) ? "RWT chat fallback"
                                                                    : "RWT chat - KMH port unreachable";
                    case KmhTransportStatus.VersionMismatch: return "Version mismatch";
                    case KmhTransportStatus.AuthFailed:      return "KMH auth failed";
                    default:                                 return "KMH offline";
                }
            }
        }
    }
}
