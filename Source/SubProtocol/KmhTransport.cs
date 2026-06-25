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

        // Short label for the KMH tab.
        public static string StatusLabel
        {
            get
            {
                switch (Status)
                {
                    case KmhTransportStatus.ApiConnected:    return "KMH API connected";
                    case KmhTransportStatus.ApiConnecting:   return "KMH API connecting…";
                    case KmhTransportStatus.ChatFallback:    return "RWT chat fallback";
                    case KmhTransportStatus.VersionMismatch: return "Version mismatch";
                    case KmhTransportStatus.AuthFailed:      return "KMH auth failed";
                    default:                                 return "KMH offline";
                }
            }
        }
    }
}
