using System.Collections.Generic;

namespace KMHPatch.SubProtocol
{
    // What THIS client can do, declared in the hello ack - the opposite direction from KmhCapabilities.
    internal static class KmhClientCapabilities
    {
        // The client stores queued notices and acknowledges them, so the server can hold them until it hears back.
        public const string DurableNotifications = "durable_notifications";

        // Sent verbatim; a server that does not know a token simply never asks for it.
        public static readonly List<string> All = new List<string> { DurableNotifications, Features.Enforcement.EnforcementAdmission.Capability };
    }
}
