using KMHPatch.Diagnostics;
using RimWorld;
using Verse;

namespace KMHPatch.Notifications
{
    // Everything is mirrored to KmhLog, so a dismissed flash still leaves a record of a server-driven event.
    public static class KmhNotifications
    {
        // Set during the self-test, whose real handlers must not put its made-up chats and letters on a player's screen.
        internal static bool Muted;

        // Marshalled: handlers run on the network thread, and Messages.Message mutates a list the main thread draws from.
        public static void Flash(string message, MessageTypeDef type = null)
        {
            if (string.IsNullOrEmpty(message) || Muted) return;

            string prefixed = $"[Colony Nexus] {message}";
            KmhMainThread.Post(() => Messages.Message(prefixed, type ?? MessageTypeDefOf.NeutralEvent, historical: true));
            KmhLog.Info($"Flash: {message}");
        }

        // Repeats of one key collapse into a cumulative line rather than stacking a message per click.
        private static readonly System.Collections.Generic.Dictionary<string, (System.DateTime last, int count)> _coalesce
            = new System.Collections.Generic.Dictionary<string, (System.DateTime, int)>();
        public static void FlashCoalesced(string key, string firstMessage, System.Func<int, string> summary,
                                          MessageTypeDef type = null, double windowSeconds = 8.0)
        {
            if (Muted) return;
            System.DateTime now = System.DateTime.UtcNow;
            int count;
            lock (_coalesce)   // reached from network threads; the window state is shared
            {
                if (!_coalesce.TryGetValue(key, out var s) || (now - s.last).TotalSeconds > windowSeconds)
                {
                    _coalesce[key] = (now, 1);
                    Flash(firstMessage, type);
                    return;
                }
                count = s.count + 1;
                _coalesce[key] = (now, count);
            }
            string text = summary?.Invoke(count) ?? firstMessage;
            KmhMainThread.Post(() => Messages.Message($"[Colony Nexus] {text}", type ?? MessageTypeDefOf.NeutralEvent, historical: false));
            KmhLog.Info($"Flash (coalesced x{count}): {text}");
        }

        public static void Letter(string label, string body, LetterDef def = null) => Letter(() => "Colony Nexus", label, body, def);

        // A server's own words wear its name, never Colony Nexus's; null names the current server, read on the main thread.
        public static void ServerLetter(string label, string body, LetterDef def = null, string server = null)
            => Letter(() => server ?? SubProtocol.KmhServerLabel.Current(), label, body, def);

        public static void FromServer(string message, MessageTypeDef type = null)
        {
            if (string.IsNullOrEmpty(message) || Muted) return;
            KmhMainThread.Post(() => Messages.Message($"[{SubProtocol.KmhServerLabel.Current()}] {message}", type ?? MessageTypeDefOf.NeutralEvent, historical: true));
            KmhLog.Info($"Server notice: {message}");
        }

        private static void Letter(System.Func<string> source, string label, string body, LetterDef def)
        {
            if (string.IsNullOrEmpty(label) || string.IsNullOrEmpty(body) || Muted) return;

            // Check and send on the main thread; there is no game (so no letter stack) on RWT's new-colony pages.
            KmhMainThread.Post(() =>
            {
                string prefix = $"[{source()}] ";
                LetterStack stack = Current.Game?.letterStack;
                if (stack == null) Messages.Message($"{prefix}{label}: {body}", MessageTypeDefOf.NeutralEvent, historical: true);
                else stack.ReceiveLetter(prefix + label, body, def ?? LetterDefOf.NeutralEvent);
            });
            KmhLog.Info($"Letter: {label} - {body}");
        }

        public static void Positive(string message) => Flash(message, MessageTypeDefOf.PositiveEvent);
        public static void Negative(string message) => Flash(message, MessageTypeDefOf.NegativeEvent);
        public static void Rejected(string message) => Flash(message, MessageTypeDefOf.RejectInput);
        public static void Neutral (string message) => Flash(message, MessageTypeDefOf.NeutralEvent);

        // A letter, not a flash: charged-but-undelivered value must survive the player dismissing a toast.
        public static void Alert(string label, string body)
        {
            KmhLog.Error($"{label}: {body}");
            Letter(label, body, LetterDefOf.NegativeEvent);
        }

        public static void NotConnected() => Rejected("Not connected to a Colony Nexus server");
    }
}
